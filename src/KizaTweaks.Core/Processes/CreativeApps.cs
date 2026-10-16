using KizaTweaks.Core.Engine;

namespace KizaTweaks.Core.Processes;

public sealed record WorkApp(string Name, string ExePath, bool Protected);

/// <summary>
/// Apps de trabajo (Photoshop, Premiere, Blender…) que se excluyen de las optimizaciones de DirectX
/// pensadas para juegos. Windows permite anularlas por programa en
/// HKCU\Software\Microsoft\DirectX\UserGpuPreferences\&lt;ruta del .exe&gt; (lo mismo que hace
/// Configuración &gt; Pantalla &gt; Gráficos &gt; Opciones de la app).
/// </summary>
public static class CreativeApps
{
    /// <summary>Ajustes globales de juegos que se apagan para la app protegida.</summary>
    public static readonly string[] ProtectedKeys = { "SwapEffectUpgradeEnable", "VRROptimizeEnable" };

    public static readonly IReadOnlyList<(string Exe, string Name)> Known = new[]
    {
        ("Photoshop.exe", "Photoshop"),
        ("Illustrator.exe", "Illustrator"),
        ("AfterFX.exe", "After Effects"),
        ("Adobe Premiere Pro.exe", "Premiere Pro"),
        ("Adobe Media Encoder.exe", "Media Encoder"),
        ("InDesign.exe", "InDesign"),
        ("Lightroom.exe", "Lightroom"),
        ("Animate.exe", "Animate"),
        ("Adobe Audition.exe", "Audition"),
        ("Adobe Substance 3D Painter.exe", "Substance Painter"),
        ("blender.exe", "Blender"),
        ("Resolve.exe", "DaVinci Resolve"),
    };

    private static readonly string[] Vendors = { "Adobe", "Blender Foundation", "Blackmagic Design" };

    /// <summary>Busca las apps conocidas instaladas (carpeta del fabricante y un nivel de subcarpetas).</summary>
    public static List<string> FindInstalled(IEnumerable<string>? programFilesRoots = null)
    {
        var roots = programFilesRoots ?? new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        var found = new List<string>();
        foreach (var pf in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var vendor in Vendors)
            {
                var vdir = Path.Combine(pf, vendor);
                if (!Directory.Exists(vdir)) continue;
                IEnumerable<string> dirs;
                try { dirs = new[] { vdir }.Concat(Directory.EnumerateDirectories(vdir)); }
                catch (Exception ex) { Log.Warn($"No se pudo revisar {vdir}: {ex.Message}"); continue; }
                foreach (var dir in dirs)
                    foreach (var (exe, _) in Known)
                    {
                        var p = Path.Combine(dir, exe);
                        if (File.Exists(p)) found.Add(p);
                    }
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string DisplayName(string exePath)
    {
        var exe = GameService.ExeName(exePath);
        var known = Known.FirstOrDefault(k => k.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
        if (known.Name is null) return Path.GetFileNameWithoutExtension(exe);
        // "Adobe Photoshop 2025" → usa el nombre de la carpeta si trae el año
        var folder = Path.GetFileName(Path.GetDirectoryName(exePath.Replace('\\', Path.DirectorySeparatorChar)) ?? "");
        return folder.Contains(known.Name, StringComparison.OrdinalIgnoreCase) ? folder : known.Name;
    }

    public static bool IsProtected(ISystemBackend b, string exePath)
    {
        var raw = b.GetValue("HKCU", DxSettings.Path, exePath)?.Text;
        return ProtectedKeys.All(k => DxSettings.Get(raw, k) == "0");
    }

    public static void Protect(ISystemBackend b, string exePath)
    {
        var raw = b.GetValue("HKCU", DxSettings.Path, exePath)?.Text;
        foreach (var k in ProtectedKeys) raw = DxSettings.With(raw, k, "0");
        b.SetValue("HKCU", DxSettings.Path, exePath, RegValue.Str(raw!));
        Log.Info($"App protegida de las optimizaciones de juegos: {exePath}");
    }

    public static void Unprotect(ISystemBackend b, string exePath)
    {
        var raw = b.GetValue("HKCU", DxSettings.Path, exePath)?.Text;
        foreach (var k in ProtectedKeys) raw = DxSettings.With(raw, k, null);
        if (string.IsNullOrEmpty(raw)) b.DeleteValue("HKCU", DxSettings.Path, exePath);
        else b.SetValue("HKCU", DxSettings.Path, exePath, RegValue.Str(raw));
        Log.Info($"App sin protección: {exePath}");
    }

    /// <summary>Apps detectadas + las que el usuario protegió a mano.</summary>
    public static List<WorkApp> List(ISystemBackend b, IEnumerable<string> detected)
    {
        var paths = new List<string>(detected);
        var snap = b.ExportKey("HKCU", DxSettings.Path);
        if (snap is not null)
        {
            foreach (var (name, v) in snap.Values)
            {
                if (name.Equals(DxSettings.ValueName, StringComparison.OrdinalIgnoreCase) || v.Kind != RegKind.String) continue;
                if (ProtectedKeys.All(k => DxSettings.Get(v.Text, k) == "0")) paths.Add(name);
            }
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new WorkApp(DisplayName(p), p, IsProtected(b, p)))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
