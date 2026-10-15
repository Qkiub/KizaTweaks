using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace KizaTweaks.Core.Platform;

[SupportedOSPlatform("windows")]
public static class SystemActions
{
    public static bool IsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static int OsBuild()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return int.TryParse(k?.GetValue("CurrentBuildNumber") as string, out var b) ? b : Environment.OSVersion.Version.Build;
    }

    public static string OsDisplayVersion()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var display = k?.GetValue("DisplayVersion") as string ?? k?.GetValue("ReleaseId") as string ?? "";
        var ubr = k?.GetValue("UBR") is int u ? $".{u}" : "";
        var name = OsBuild() >= 22000 ? "Windows 11" : "Windows 10";
        var edition = k?.GetValue("EditionID") as string ?? "";
        return $"{name} {edition} {display} (build {OsBuild()}{ubr})".Replace("  ", " ");
    }

    /// <summary>
    /// Crea un punto de restauración. Windows limita a uno cada 24 h por defecto; se levanta
    /// temporalmente ese límite y se restaura después.
    /// </summary>
    public static (bool ok, string message) CreateRestorePoint(string description)
    {
        var desc = new string(description.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.').ToArray());
        if (desc.Length > 60) desc = desc[..60];
        var script = $$"""
            $k='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
            $old=(Get-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
            New-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value 0 -PropertyType DWord -Force | Out-Null
            try {
              Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
              Checkpoint-Computer -Description {{ProcessRunner.PsQuote(desc)}} -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
              'KIZA_OK'
            } catch { 'KIZA_ERR:' + $_.Exception.Message }
            finally {
              if ($null -eq $old) { Remove-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }
              else { Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value $old }
            }
            """;
        var r = ProcessRunner.PowerShell(script, 600_000);
        if (r.Output.Contains("KIZA_OK")) return (true, "Punto de restauración creado.");
        var err = r.Output.Split('\n').FirstOrDefault(l => l.Contains("KIZA_ERR:"))?.Replace("KIZA_ERR:", "").Trim();
        return (false, string.IsNullOrWhiteSpace(err) ? r.Output.Trim() : err);
    }

    /// <summary>Reinicia el Explorador para aplicar cambios de barra de tareas/menús sin cerrar sesión.</summary>
    public static void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
                p.Dispose();
            }
            // Windows suele relanzar el shell solo; si no, se inicia manualmente.
            Thread.Sleep(2000);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo reiniciar el Explorador", ex);
        }
    }

    public static void Reboot() =>
        ProcessRunner.Run(ProcessRunner.System32("shutdown.exe"), "/r /t 5 /c \"KizaTweaks: reiniciando para aplicar cambios\"");
}
