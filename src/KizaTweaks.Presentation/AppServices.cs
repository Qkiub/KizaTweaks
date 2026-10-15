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
    public const string Green = "#22C55E";
    public const string Amber = "#F59E0B";
    public const string Red = "#EF4444";
    public const string Cyan = "#22D3EE";
    public const string Violet = "#A78BFA";
    public const string Slate = "#94A3B8";
    public const string Pink = "#F472B6";
    public const string Blue = "#60A5FA";

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
