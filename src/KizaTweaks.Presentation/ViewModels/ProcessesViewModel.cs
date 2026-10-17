using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using KizaTweaks.Core.Processes;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class ProcessItemViewModel
{
    public required ProcessInfo Info { get; init; }
    public string Name => Info.Name;
    public string Display => Info.Description ?? Info.WindowTitle ?? Info.Name;
    public int Pid => Info.Pid;
    public string Cpu => $"{Info.CpuPercent:0.0} %";
    public double CpuValue => Info.CpuPercent;
    public string Memory => $"{Info.MemoryBytes / 1048576.0:0} MB";
    public long MemoryValue => Info.MemoryBytes;
    public string Company => Info.Company ?? "";
    public string Priority => Info.Priority switch
    {
        "RealTime" => "Tiempo real", "High" => "Alta", "AboveNormal" => "Encima de normal", "Normal" => "Normal",
        "BelowNormal" => "Debajo de normal", "Idle" => "Baja", null => "—", var p => p,
    };
    public bool IsProtected => Info.Kind == ProcessKind.Protected;
    public string KindLabel => Info.Kind switch { ProcessKind.Protected => "Sistema (protegido)", ProcessKind.Windows => "Windows", _ => "App" };
    public string KindColor => Info.Kind switch { ProcessKind.Protected => Labels.Red, ProcessKind.Windows => Labels.Blue, _ => Labels.Green };
}

public sealed class CandidateViewModel : ObservableObject
{
    public required BackgroundCandidate Candidate { get; init; }
    public string Name => Candidate.FriendlyName;
    public string Why => Candidate.Why;
    private int _running;
    public int Running { get => _running; set { if (SetProperty(ref _running, value)) OnPropertyChanged(nameof(RunningLabel)); } }
    public string RunningLabel => Running == 0 ? "No está abierto" : Running == 1 ? "1 proceso abierto" : $"{Running} procesos abiertos";
    private bool _selected;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
}

public sealed class PriorityRuleViewModel
{
    public required PriorityRule Rule { get; init; }
    public string ExeName => Rule.ExeName;
    public string Label => Rule.Label;
    public required System.Windows.Input.ICommand RemoveCommand { get; init; }
}

[SupportedOSPlatform("windows")]
public sealed class ProcessesViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private List<ProcessItemViewModel> _all = new();

    public ObservableCollection<ProcessItemViewModel> Visible { get; } = new();
    public ObservableCollection<CandidateViewModel> Candidates { get; } = new();
    public ObservableCollection<PriorityRuleViewModel> PriorityRules { get; } = new();

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand KillCommand { get; }
    public AsyncCommand SetPriorityCommand { get; }
    public AsyncCommand CloseCandidatesCommand { get; }
    public RelayCommand RefreshCandidatesCommand { get; }

    public ProcessesViewModel(AppServices s, IDialogService dialogs, IShellHost shell)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        foreach (var c in ProcessService.BackgroundCandidates)
            Candidates.Add(new CandidateViewModel { Candidate = c, IsSelected = c.DefaultSelected });
        RefreshCommand = new AsyncCommand(RefreshAsync);
        KillCommand = new AsyncCommand(KillAsync, () => Selected is { IsProtected: false });
        SetPriorityCommand = new AsyncCommand(p => SetPriorityAsync(p as string));
        CloseCandidatesCommand = new AsyncCommand(CloseCandidatesAsync);
        RefreshCandidatesCommand = new RelayCommand(RefreshCandidates);
    }

    private ProcessItemViewModel? _selected;
    public ProcessItemViewModel? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) KillCommand.RaiseCanExecuteChanged(); }
    }

    private bool _showSystem;
    public bool ShowSystem { get => _showSystem; set { if (SetProperty(ref _showSystem, value)) ApplyFilter(); } }

    private string _search = "";
    public string SearchText { get => _search; set { if (SetProperty(ref _search, value ?? "")) ApplyFilter(); } }

    private string _summary = "";
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    private void ApplyFilter()
    {
        var q = _search.Trim();
        Visible.Clear();
        foreach (var p in _all.Where(p => (_showSystem || p.Info.Kind == ProcessKind.App) &&
                                          (q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                           p.Display.Contains(q, StringComparison.OrdinalIgnoreCase))))
            Visible.Add(p);
    }

    public async Task RefreshAsync()
    {
        var list = await Task.Run(() => _s.Processes.List());
        _all = list.Select(i => new ProcessItemViewModel { Info = i }).ToList();
        var mem = list.Sum(p => p.MemoryBytes);
        Summary = $"{list.Count} procesos · {list.Count(p => p.Kind == ProcessKind.App)} apps · {mem / 1073741824.0:0.0} GB en uso por procesos";
        ApplyFilter();
        RefreshCandidates();
        RefreshRules();
    }

    private void RefreshCandidates()
    {
        foreach (var c in Candidates) c.Running = ProcessService.CountRunning(c.Candidate.ExeNames);
    }

    private void RefreshRules()
    {
        PriorityRules.Clear();
        foreach (var r in ProcessService.GetPriorityRules(_s.Backend))
            PriorityRules.Add(new PriorityRuleViewModel { Rule = r, RemoveCommand = new AsyncCommand(() => RemoveRuleAsync(r)) });
    }

    private async Task KillAsync()
    {
        if (Selected is not { } p) return;
        if (!await _dialogs.ConfirmAsync("Cerrar proceso",
                $"¿Cerrar {p.Display} ({p.Name}.exe)?\n\nSi tiene trabajo sin guardar, se perderá.", "Cerrar")) return;
        var (ok, msg) = await Task.Run(() => _s.Processes.Kill(p.Pid));
        _shell.Toast(msg, !ok);
        await RefreshAsync();
    }

    private async Task SetPriorityAsync(string? level)
    {
        if (Selected is not { } p || level is null) return;
        if (!Enum.TryParse<ProcessPriorityClass>(level, out var prio)) return;
        var (ok, msg) = await Task.Run(() => _s.Processes.SetPriority(p.Pid, prio));
        _shell.Toast(msg + (ok ? " (hasta que se cierre)" : ""), !ok);
        await RefreshAsync();
    }

    private async Task RemoveRuleAsync(PriorityRule r)
    {
        await Task.Run(() => ProcessService.RemovePriorityRule(_s.Backend, r.ExeName));
        _shell.Toast($"Prioridad permanente de {r.ExeName} eliminada.");
        RefreshRules();
    }

    private async Task CloseCandidatesAsync()
    {
        var names = Candidates.Where(c => c.IsSelected && c.Running > 0).SelectMany(c => c.Candidate.ExeNames).ToList();
        if (names.Count == 0)
        {
            _shell.Toast("Ninguno de los seleccionados está abierto.");
            return;
        }
        var n = await Task.Run(() => _s.Processes.KillByNames(names));
        _shell.Toast($"Sesión de juego lista: {n} procesos cerrados.");
        await RefreshAsync();
    }
}
