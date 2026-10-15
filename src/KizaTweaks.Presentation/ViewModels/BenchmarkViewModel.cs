using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using KizaTweaks.Core;
using KizaTweaks.Core.Benchmark;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class SnapshotViewModel
{
    public required SystemSnapshot Snapshot { get; init; }
    public string Title => $"{Snapshot.Label} · {Snapshot.Time:dd/MM HH:mm}";
    public string Summary =>
        $"{Snapshot.Processes} procesos · CPU {Snapshot.CpuIdlePercent:0.#} % · RAM {Snapshot.RamUsedGb:0.0}/{Snapshot.RamTotalGb:0} GB" +
        (Snapshot.LastBootSeconds is { } b ? $" · arranque {b:0.#} s" : "");
    public override string ToString() => Title;
}

public sealed class FpsResultViewModel
{
    public required FpsResult Result { get; init; }
    public string Title => $"{Result.Label} · {Result.Game}";
    public string When => $"{Result.Time:dd/MM HH:mm} · {Result.Seconds} s · {Result.Frames} frames";
    public string Avg => $"{Result.AvgFps:0.#}";
    public string Low1 => $"{Result.Low1:0.#}";
    public string Low01 => $"{Result.Low01:0.#}";
    public string P99 => $"{Result.P99FrameTimeMs:0.0} ms";
}

public sealed record GameOption(string ExeName, string Title)
{
    public override string ToString() => $"{Title}  ({ExeName}.exe)";
}

[SupportedOSPlatform("windows")]
public sealed class BenchmarkViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;

    public ObservableCollection<SnapshotViewModel> Snapshots { get; } = new();
    public ObservableCollection<SnapshotRow> Comparison { get; } = new();
    public ObservableCollection<GameOption> Games { get; } = new();
    public ObservableCollection<FpsResultViewModel> FpsResults { get; } = new();
    public int[] Durations { get; } = { 30, 60, 90, 120 };

    public AsyncCommand TakeSnapshotCommand { get; }
    public AsyncCommand LoadCommand { get; }
    public RelayCommand RefreshGamesCommand { get; }
    public AsyncCommand DownloadPresentMonCommand { get; }
    public AsyncCommand RunFpsCommand { get; }

    public BenchmarkViewModel(AppServices s, IDialogService dialogs, IShellHost shell)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        TakeSnapshotCommand = new AsyncCommand(TakeSnapshotAsync);
        LoadCommand = new AsyncCommand(LoadAsync);
        RefreshGamesCommand = new RelayCommand(RefreshGames);
        DownloadPresentMonCommand = new AsyncCommand(DownloadPresentMonAsync);
        RunFpsCommand = new AsyncCommand(RunFpsAsync);
    }

    private string _snapshotLabel = "Antes";
    public string SnapshotLabel { get => _snapshotLabel; set => SetProperty(ref _snapshotLabel, value); }

    private SnapshotViewModel? _a, _b;
    public SnapshotViewModel? CompareA { get => _a; set { if (SetProperty(ref _a, value)) Compare(); } }
    public SnapshotViewModel? CompareB { get => _b; set { if (SetProperty(ref _b, value)) Compare(); } }

    private bool _presentMonReady;
    public bool PresentMonReady { get => _presentMonReady; private set => SetProperty(ref _presentMonReady, value); }

    private GameOption? _game;
    public GameOption? SelectedGame { get => _game; set => SetProperty(ref _game, value); }

    private int _duration = 60;
    public int Duration { get => _duration; set => SetProperty(ref _duration, value); }

    private string _fpsLabel = "Antes";
    public string FpsLabel { get => _fpsLabel; set => SetProperty(ref _fpsLabel, value); }

    public async Task LoadAsync()
    {
        var snaps = await Task.Run(SnapshotService.LoadAll);
        Snapshots.Clear();
        foreach (var x in snaps) Snapshots.Add(new SnapshotViewModel { Snapshot = x });
        if (Snapshots.Count >= 2) { CompareB = Snapshots[0]; CompareA = Snapshots[1]; }
        FpsResults.Clear();
        foreach (var r in _s.FpsResults.Load()) FpsResults.Add(new FpsResultViewModel { Result = r });
        PresentMonReady = _s.PresentMon.FindExe() is not null;
        RefreshGames();
    }

    private async Task TakeSnapshotAsync()
    {
        var label = string.IsNullOrWhiteSpace(SnapshotLabel) ? "Medición" : SnapshotLabel.Trim();
        SystemSnapshot? snap = null;
        await _shell.RunBusyAsync("Midiendo el sistema en reposo (no uses el PC unos segundos)…", async _ =>
            snap = await Task.Run(() => _s.Snapshots.Take(label)));
        if (snap is null) return;
        var vm = new SnapshotViewModel { Snapshot = snap };
        Snapshots.Insert(0, vm);
        if (Snapshots.Count >= 2) { CompareA = Snapshots[1]; CompareB = vm; }
        SnapshotLabel = "Después";
        _shell.Toast("Medición guardada.");
    }

    private void Compare()
    {
        Comparison.Clear();
        if (_a is null || _b is null || ReferenceEquals(_a, _b)) return;
        foreach (var row in SnapshotService.Compare(_a.Snapshot, _b.Snapshot)) Comparison.Add(row);
    }

    private void RefreshGames()
    {
        Games.Clear();
        var me = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == me || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    if (p.ProcessName is "explorer" or "ApplicationFrameHost" or "TextInputHost" or "SystemSettings") continue;
                    Games.Add(new GameOption(p.ProcessName, p.MainWindowTitle));
                }
                catch { }
            }
        }
        SelectedGame ??= Games.FirstOrDefault();
    }

    private async Task DownloadPresentMonAsync()
    {
        if (!await _dialogs.ConfirmAsync("Activar medición de FPS",
                "Para medir FPS, KizaTweaks descarga una sola vez su componente de medición (PresentMon, de código abierto) " +
                $"y lo guarda en:\n{AppPaths.ToolsDir}", "Descargar")) return;
        try
        {
            await _shell.RunBusyAsync("Descargando componente de medición…", async progress =>
                await _s.PresentMon.DownloadLatestAsync(progress));
            PresentMonReady = true;
            _shell.Toast("Medición de FPS lista.");
        }
        catch (Exception ex)
        {
            await _dialogs.InfoAsync("No se pudo descargar", ex.Message);
        }
    }

    private async Task RunFpsAsync()
    {
        if (SelectedGame is not { } game)
        {
            await _dialogs.InfoAsync("Elige un juego", "Abre el juego, vuelve aquí, pulsa «Actualizar lista» y selecciónalo.");
            return;
        }
        if (!await _dialogs.ConfirmAsync("Medir FPS",
                $"Se medirán {Duration} segundos de {game.Title}.\n\nAl pulsar «Empezar» tendrás 5 segundos para volver al juego. " +
                "Juega la MISMA escena en cada medición (por ejemplo, el mismo recorrido o el benchmark integrado del juego) " +
                "para que la comparación sea justa.", "Empezar")) return;
        try
        {
            FpsResult? result = null;
            await _shell.RunBusyAsync("Vuelve al juego…", async progress =>
            {
                for (int i = 5; i > 0; i--)
                {
                    progress.Report($"Vuelve al juego: la medición empieza en {i}…");
                    await Task.Delay(1000);
                }
                progress.Report($"Midiendo {Duration} s… sigue jugando.");
                result = await _s.PresentMon.CaptureAsync(game.ExeName, Duration, string.IsNullOrWhiteSpace(FpsLabel) ? "Medición" : FpsLabel.Trim());
            });
            if (result is null) return;
            _s.FpsResults.Add(result);
            FpsResults.Insert(0, new FpsResultViewModel { Result = result });
            await _dialogs.InfoAsync("Resultado",
                $"{game.Title}\n\nFPS promedio: {result.AvgFps:0.#}\n1 % low: {result.Low1:0.#}\n0,1 % low: {result.Low01:0.#}\n" +
                $"Tiempo de frame p99: {result.P99FrameTimeMs:0.0} ms\n\nEl 1 % low es lo que más se siente: si sube, el juego da menos tirones.");
            FpsLabel = "Después";
        }
        catch (Exception ex)
        {
            await _dialogs.InfoAsync("No se pudo medir", ex.Message);
        }
    }
}
