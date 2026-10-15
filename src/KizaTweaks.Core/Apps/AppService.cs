using System.Diagnostics;
using System.Runtime.Versioning;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Platform;

namespace KizaTweaks.Core.Apps;

public sealed record AppRemovalResult(AppDefinition App, bool Ok, string Message);

/// <summary>Detecta y desinstala apps preinstaladas (Appx de la Store o paquetes WinGet).</summary>
[SupportedOSPlatform("windows")]
public sealed class AppService
{
    /// <summary>Nombres de paquetes Appx instalados (para cualquier usuario) o aprovisionados.</summary>
    public HashSet<string> GetInstalledAppxNames()
    {
        const string script = """
            $names = @()
            $names += Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
            $names += Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | ForEach-Object { $_.DisplayName }
            $names | Sort-Object -Unique
            """;
        var r = ProcessRunner.PowerShell(script, 180_000);
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string? WingetPath()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                 "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(local)) return local;
        // Como administrador elevado, el alias de usuario puede no existir: se busca en WindowsApps.
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            var dir = Directory.GetDirectories(root, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                               .OrderByDescending(d => d).FirstOrDefault();
            if (dir is not null && File.Exists(Path.Combine(dir, "winget.exe"))) return Path.Combine(dir, "winget.exe");
        }
        catch { /* sin acceso a WindowsApps */ }
        return null;
    }

    public bool IsInstalled(AppDefinition app, HashSet<string> appx)
    {
        if (app.Method.Equals("Appx", StringComparison.OrdinalIgnoreCase))
            return app.Ids.Any(appx.Contains);

        // WinGet: OneDrive se detecta por archivo (más rápido y fiable); el resto con winget list.
        if (app.Ids.Contains("Microsoft.OneDrive", StringComparer.OrdinalIgnoreCase))
            return OneDriveExe() is not null;
        var winget = WingetPath();
        if (winget is null) return false;
        foreach (var id in app.Ids)
        {
            var r = ProcessRunner.Run(winget, $"list --id {id} --exact --accept-source-agreements --disable-interactivity", 60_000);
            if (r.Ok && r.Output.Contains(id, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? OneDriveExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive", "OneDrive.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive", "OneDrive.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft OneDrive", "OneDrive.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public AppRemovalResult Remove(AppDefinition app)
    {
        try
        {
            return app.Method.Equals("WinGet", StringComparison.OrdinalIgnoreCase) ? RemoveWinget(app) : RemoveAppx(app);
        }
        catch (Exception ex)
        {
            Log.Error($"Error quitando {app.Name}", ex);
            return new AppRemovalResult(app, false, ex.Message);
        }
    }

    private AppRemovalResult RemoveAppx(AppDefinition app)
    {
        var errors = new List<string>();
        foreach (var id in app.Ids)
        {
            var script = $$"""
                $n = {{ProcessRunner.PsQuote(id)}}
                Get-AppxPackage -AllUsers -Name $n -ErrorAction SilentlyContinue | ForEach-Object {
                  try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop }
                  catch { try { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop } catch { 'KIZA_ERR:' + $_.Exception.Message } }
                }
                Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq $n } | ForEach-Object {
                  try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop | Out-Null }
                  catch { 'KIZA_ERR:' + $_.Exception.Message }
                }
                """;
            var r = ProcessRunner.PowerShell(script, 300_000);
            errors.AddRange(r.Output.Split('\n').Where(l => l.Contains("KIZA_ERR:")).Select(l => l.Replace("KIZA_ERR:", "").Trim()));
        }
        Log.Info($"Quitar app {app.Name}: {(errors.Count == 0 ? "ok" : string.Join(" | ", errors))}");
        return new AppRemovalResult(app, errors.Count == 0, errors.Count == 0 ? "Eliminada" : string.Join(" | ", errors.Distinct()));
    }

    private AppRemovalResult RemoveWinget(AppDefinition app)
    {
        if (app.Ids.Contains("Microsoft.OneDrive", StringComparer.OrdinalIgnoreCase))
        {
            foreach (var p in Process.GetProcessesByName("OneDrive"))
            {
                try { p.Kill(); } catch { }
            }
        }
        var winget = WingetPath();
        if (winget is null)
            return new AppRemovalResult(app, false, "WinGet no está disponible. Actualiza 'Instalador de aplicación' desde la Microsoft Store.");
        foreach (var id in app.Ids)
        {
            var r = ProcessRunner.Run(winget,
                $"uninstall --id {id} --exact --silent --accept-source-agreements --disable-interactivity", 600_000);
            Log.Info($"winget uninstall {id}: código {r.ExitCode}");
            if (r.Ok) return new AppRemovalResult(app, true, "Eliminada");
        }
        return new AppRemovalResult(app, false, "WinGet no pudo desinstalarla (puede que no esté instalada).");
    }
}
