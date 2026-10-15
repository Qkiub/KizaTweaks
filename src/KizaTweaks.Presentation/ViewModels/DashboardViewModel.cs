using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Hardware;
using KizaTweaks.Core.Platform;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class RecommendationViewModel
{
    public required Recommendation Rec { get; init; }
    public string Title => Rec.Title;
    public string Detail => Rec.Detail;
    public string SeverityLabel => Labels.Severity(Rec.Severity).text;
    public string SeverityColor => Labels.Severity(Rec.Severity).color;
    public bool HasAction => Rec.TweakId is not null || Rec.SettingsUri is not null;
    public string ActionText => Rec.TweakId is not null ? "Aplicar" : "Abrir configuración";
    public required System.Windows.Input.ICommand ActionCommand { get; init; }
}

public sealed class DiskViewModel
{
    public required DiskInfo Disk { get; init; }
    public string Model => Disk.Model;
    public string Kind => Disk.Kind;
    public string Size => Labels.Bytes(Disk.SizeBytes);
    public string Letters => Disk.Letters.Count == 0 ? "sin letra" : string.Join(" ", Disk.Letters);
    public string Health => Disk.Health;
    public string HealthColor => Disk.Health switch { "Buena" => Labels.Green, "Advertencia" => Labels.Amber, "Mala" => Labels.Red, _ => Labels.Slate };
    public string Temp => Disk.TemperatureC is { } t ? $"{t} °C" : "—";
    public string Wear => Disk.WearPercent is { } w ? $"{w} % desgaste" : "";
    public bool IsSystem => Disk.HasSystem;
}

[SupportedOSPlatform("windows")]
public sealed class DashboardViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private readonly TweaksViewModel _tweaks;

    public ObservableCollection<RecommendationViewModel> Recommendations { get; } = new();
    public ObservableCollection<DiskViewModel> Disks { get; } = new();
    public AsyncCommand LoadCommand { get; }
    public AsyncCommand ApplyGamerCommand { get; }
    public AsyncCommand RestorePointCommand { get; }
    public RelayCommand GoTweaksCommand { get; }

    public DashboardViewModel(AppServices s, IDialogService dialogs, IShellHost shell, TweaksViewModel tweaks)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        _tweaks = tweaks;
        LoadCommand = new AsyncCommand(LoadAsync);
        ApplyGamerCommand = new AsyncCommand(ApplyGamerAsync);
        RestorePointCommand = new AsyncCommand(CreateRestorePointAsync);
        GoTweaksCommand = new RelayCommand(() => _shell.Navigate("tweaks"));
    }

    private HardwareReport? _report;
    public HardwareReport? Report
    {
        get => _report;
        private set
        {
            SetProperty(ref _report, value);
            foreach (var p in new[] { nameof(OsText), nameof(CpuText), nameof(CpuDetail), nameof(GpuText), nameof(GpuDetail),
                                      nameof(RamText), nameof(RamDetail), nameof(DisplayText), nameof(DisplayDetail),
                                      nameof(BoardText), nameof(DeviceType) })
                OnPropertyChanged(p);
        }
    }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    private int _score;
    public int Score { get => _score; private set { SetProperty(ref _score, value); OnPropertyChanged(nameof(ScoreLabel)); OnPropertyChanged(nameof(ScoreColor)); } }
    public string ScoreLabel => Score >= 85 ? "Excelente" : Score >= 65 ? "Bien, con margen" : Score >= 40 ? "Mejorable" : "Necesita atención";
    public string ScoreColor => Score >= 85 ? Labels.Green : Score >= 65 ? Labels.Cyan : Score >= 40 ? Labels.Amber : Labels.Red;

    public string OsText => Report?.Os ?? "…";
    public string DeviceType => Report is null ? "" : Report.IsLaptop ? "Portátil" : "Escritorio";
    public string CpuText => Report?.Cpu.Name ?? "…";
    public string CpuDetail => Report is null ? "" : $"{Report.Cpu.Cores} núcleos / {Report.Cpu.Threads} hilos · {Report.Cpu.MaxClockMhz} MHz";
    public string GpuText => Report?.MainGpu?.Name ?? "…";
    public string GpuDetail => Report?.MainGpu is { } g
        ? $"{Labels.Bytes(g.VramBytes)} VRAM · driver {g.DriverVersion}{(g.DriverDate is { } d ? $" ({d:dd/MM/yyyy})" : "")}"
        : "";
    public string RamText => Report is null ? "…" : $"{Labels.Bytes(Report.RamTotalBytes)} {Report.Ram.FirstOrDefault()?.Type}";
    public string RamDetail
    {
        get
        {
            if (Report is null || Report.Ram.Count == 0) return "";
            var conf = Report.Ram.Min(r => r.ConfiguredMhz);
            var rated = Report.Ram.Max(r => r.RatedMhz);
            var ch = Report.Ram.Count == 1 ? "1 módulo" : $"{Report.Ram.Count} módulos";
            return $"{ch} · funcionando a {conf} MHz" + (rated > conf ? $" (soporta {rated})" : "");
        }
    }
    public string DisplayText => Report?.Displays.FirstOrDefault() is { } d ? $"{d.Width}×{d.Height} a {d.CurrentHz} Hz" : "…";
    public string DisplayDetail => Report?.Displays.FirstOrDefault() is { } d
        ? $"{d.MonitorName} · máximo {d.MaxHz} Hz" + (Report.Displays.Count > 1 ? $" · {Report.Displays.Count} pantallas" : "")
        : "";
    public string BoardText => Report is null ? "" : $"{Report.Motherboard} · BIOS {Report.Bios}{(Report.BiosDate is { } b ? $" ({b:yyyy})" : "")}";

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var report = await Task.Run(() => _s.Hardware.Collect());
            Report = report;
            Disks.Clear();
            foreach (var d in report.Disks.OrderByDescending(d => d.HasSystem)) Disks.Add(new DiskViewModel { Disk = d });

            if (!_tweaks.Loaded) await _tweaks.RefreshAsync();
            var states = _tweaks.All.ToDictionary(t => t.Def.Id, t => t.State);
            var recs = RecommendationEngine.Build(report, id => states.GetValueOrDefault(id, TweakState.Unknown));
            Recommendations.Clear();
            foreach (var r in recs)
                Recommendations.Add(new RecommendationViewModel { Rec = r, ActionCommand = new AsyncCommand(() => ActAsync(r)) });
            Score = RecommendationEngine.Score(recs);
        }
        finally { IsLoading = false; }
    }

    private async Task ActAsync(Recommendation r)
    {
        if (r.TweakId is not null && _s.Catalog.Find(r.TweakId) is { } t)
        {
            await _tweaks.ApplyAsync(new[] { t });
            await LoadAsync();
        }
        else if (r.SettingsUri is not null)
        {
            ProcessRunner.OpenShell(r.SettingsUri);
        }
    }

    private async Task ApplyGamerAsync()
    {
        _tweaks.SelectProfile(Profiles.Gamer);
        var targets = _tweaks.All.Where(t => t.IsSelected).Select(t => t.Def).ToList();
        if (targets.Count == 0)
        {
            await _dialogs.InfoAsync("Perfil Gamer", "Todos los ajustes del perfil Gamer ya están aplicados.");
            return;
        }
        await _tweaks.ApplyAsync(targets);
        await LoadAsync();
    }

    private async Task CreateRestorePointAsync()
    {
        (bool ok, string msg) result = default;
        await _shell.RunBusyAsync("Creando punto de restauración…", async _ =>
            result = await Task.Run(() => SystemActions.CreateRestorePoint("KizaTweaks - manual")));
        if (result.ok) _s.RestorePointCreated = true;
        _shell.Toast(result.ok ? "Punto de restauración creado." : $"No se pudo crear: {result.msg}", !result.ok);
    }
}
