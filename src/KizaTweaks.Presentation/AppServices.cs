using System.Runtime.Versioning;
using KizaTweaks.Core;
using KizaTweaks.Core.Apps;
using KizaTweaks.Core.Benchmark;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Hardware;
using KizaTweaks.Core.Maintenance;
using KizaTweaks.Core.Platform;
using KizaTweaks.Core.Processes;

namespace KizaTweaks.Presentation;

/// <summary>Contenedor de servicios compartidos por todas las páginas.</summary>
[SupportedOSPlatform("windows")]
public sealed class AppServices
{
    public required TweakCatalog Catalog { get; init; }
    public required AppCatalog AppsCatalog { get; init; }
    public required ISystemBackend Backend { get; init; }
    public required TweakEngine Engine { get; init; }
    public required HardwareService Hardware { get; init; }
    public required AppService Apps { get; init; }
    public required ProcessService Processes { get; init; }
    public required StartupService Startup { get; init; }
    public required SnapshotService Snapshots { get; init; }
    public required PresentMonService PresentMon { get; init; }
    public required FpsResultStore FpsResults { get; init; }
    public required MaintenanceService Maintenance { get; init; }
    public int Build { get; init; }
    public bool IsAdmin { get; init; }
    public bool RestorePointCreated { get; set; }

    /// <summary>Última detección de hardware (la usan los perfiles y el Modo FPS).</summary>
    public HardwareReport? LastHardware { get; set; }

    public async Task<HardwareReport> GetHardwareAsync(bool refresh = false)
    {
        if (LastHardware is null || refresh) LastHardware = await Task.Run(() => Hardware.Collect());
        return LastHardware;
    }

    public static AppServices CreateForWindows()
    {
        Directory.CreateDirectory(AppPaths.StateDir);
        Log.Directory = AppPaths.LogsDir;
        var backend = new WindowsBackend();
        var startup = new StartupService(backend);
        var s = new AppServices
        {
            Catalog = CatalogLoader.LoadTweaks(),
            AppsCatalog = CatalogLoader.LoadApps(),
            Backend = backend,
            Engine = new TweakEngine(backend, new BackupStore(AppPaths.BackupsFile), AppPaths.HistoryFile),
            Hardware = new HardwareService(backend),
            Apps = new AppService(),
            Processes = new ProcessService(),
            Startup = startup,
            Snapshots = new SnapshotService(startup),
            PresentMon = new PresentMonService(),
            FpsResults = new FpsResultStore(),
            Maintenance = new MaintenanceService(),
            Build = SystemActions.OsBuild(),
            IsAdmin = SystemActions.IsAdministrator(),
        };
        Log.Info($"KizaTweaks iniciado. {SystemActions.OsDisplayVersion()} | admin={s.IsAdmin} | tweaks={s.Catalog.Tweaks.Count}");
        return s;
    }
}

/// <summary>Textos y colores para etiquetas (impacto, riesgo, estado).</summary>
public static class Labels
{
    public const string Green = "#3DDC97";
    public const string Amber = "#FFB547";
    public const string Red = "#FF5470";
    public const string Cyan = "#4FD1E8";
    public const string Violet = "#A98BFF";
    public const string Slate = "#8B95AB";
    public const string Pink = "#FF7AB6";
    public const string Blue = "#6EA8FF";

    public static (string text, string color) Impact(string impact) => impact switch
    {
        "fps" => ("FPS", Green),
        "latency" => ("Latencia", Cyan),
        "privacy" => ("Privacidad", Violet),
        "cleanup" => ("Limpieza", Slate),
        "appearance" => ("Apariencia", Pink),
        "storage" => ("Almacenamiento", Amber),
        "stability" => ("Estabilidad", Blue),
        _ => (impact, Slate),
    };

    public static (string text, string color) Risk(string risk) => risk switch
    {
        "safe" => ("Seguro", Green),
        "moderate" => ("Moderado", Amber),
        "risky" => ("Riesgo", Red),
        _ => (risk, Slate),
    };

    public static (string text, string color) State(TweakState s) => s switch
    {
        TweakState.Applied => ("Aplicado", Green),
        TweakState.Partial => ("Parcial", Amber),
        TweakState.NotApplied => ("No aplicado", Slate),
        _ => ("Desconocido", Slate),
    };

    public static (string text, string color) AppRecommendation(string r) => r switch
    {
        "safe" => ("Seguro de quitar", Green),
        "optional" => ("Opcional", Amber),
        "unsafe" => ("No recomendado", Red),
        _ => (r, Slate),
    };

    public static (string text, string color) Severity(RecSeverity s) => s switch
    {
        RecSeverity.High => ("Alto impacto", Red),
        RecSeverity.Medium => ("Recomendado", Amber),
        _ => ("Info", Cyan),
    };

    public static string Bytes(long b)
    {
        const double K = 1024;
        if (b <= 0) return "—";
        if (b >= K * K * K * K) return $"{b / (K * K * K * K):0.#} TB";
        if (b >= K * K * K) return $"{b / (K * K * K):0.#} GB";
        if (b >= K * K) return $"{b / (K * K):0} MB";
        return $"{b / K:0} KB";
    }
}
