using KizaTweaks.Core.Engine;

namespace KizaTweaks.Core.Processes;

/// <summary>Un juego configurado por KizaTweaks: prioridad alta permanente y/o GPU de alto rendimiento.</summary>
public sealed record GameEntry(string ExeName, string? FullPath, bool HighPriority, bool DedicatedGpu);

/// <summary>
/// Configuración por juego que Windows aplica cada vez que se abre el .exe:
/// prioridad (Image File Execution Options) y GPU preferida (Configuración > Pantalla > Gráficos).
/// </summary>
public static class GameService
{
    public const string GpuPrefsPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string GlobalValue = "DirectXUserGlobalSettings";

    public static List<GameEntry> List(ISystemBackend b)
    {
        var map = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
        var snap = b.ExportKey("HKCU", GpuPrefsPath);
        if (snap is not null)
        {
            foreach (var (name, v) in snap.Values)
            {
                if (name.Equals(GlobalValue, StringComparison.OrdinalIgnoreCase) || v.Kind != RegKind.String) continue;
                if (!(v.Text ?? "").Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase)) continue;
                var exe = ExeName(name);
                map[exe] = new GameEntry(exe, name, false, true);
            }
        }
        foreach (var r in ProcessService.GetPriorityRules(b).Where(r => r.CpuPriorityClass == 3))
        {
            map[r.ExeName] = map.TryGetValue(r.ExeName, out var g)
                ? g with { HighPriority = true }
                : new GameEntry(r.ExeName, null, true, false);
        }
        return map.Values.OrderBy(g => g.ExeName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void Add(ISystemBackend b, string fullPath, bool highPriority = true, bool dedicatedGpu = true)
    {
        if (!fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Elige el archivo .exe del juego.");
        if (highPriority) ProcessService.SetPriorityRule(b, ExeName(fullPath), 3);
        if (dedicatedGpu) b.SetValue("HKCU", GpuPrefsPath, fullPath, RegValue.Str("GpuPreference=2;"));
        Log.Info($"Juego configurado: {fullPath} (prioridad alta={highPriority}, GPU dedicada={dedicatedGpu})");
    }

    /// <summary>Nombre del .exe a partir de una ruta de Windows (funciona igual en cualquier sistema).</summary>
    public static string ExeName(string path) => path[(path.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];

    public static void Remove(ISystemBackend b, GameEntry g)
    {
        if (g.HighPriority) ProcessService.RemovePriorityRule(b, g.ExeName);
        if (g.FullPath is not null) b.DeleteValue("HKCU", GpuPrefsPath, g.FullPath);
        Log.Info($"Juego quitado: {g.ExeName}");
    }
}
