using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Text;
using KizaTweaks.Core;
using KizaTweaks.Core.Maintenance;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class MaintenanceItemViewModel : ObservableObject
{
    public required MaintenanceTask Task { get; init; }
    public required System.Windows.Input.ICommand RunCommand { get; init; }
    public string Name => Task.Name;
    public string Description => Task.Description;
    public string Command => Task.Command;
    public string Duration => Task.Duration;
    public string? Note => Task.Note;
    public bool HasNote => !string.IsNullOrWhiteSpace(Task.Note);
    public bool RequiresReboot => Task.RequiresReboot;
    public bool InQuick => Task.InQuick;
    public string RiskLabel => Labels.Risk(Task.Risk).text;
    public string RiskColor => Labels.Risk(Task.Risk).color;

    private string _status = "";
    public string Status { get => _status; set { if (SetProperty(ref _status, value)) OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => !string.IsNullOrEmpty(_status);
    private string _statusColor = Labels.Slate;
    public string StatusColor { get => _statusColor; set => SetProperty(ref _statusColor, value); }
}

public sealed class MaintenanceGroupViewModel
{
    public required string Name { get; init; }
    public required IReadOnlyList<MaintenanceItemViewModel> Items { get; init; }
}

[SupportedOSPlatform("windows")]
public sealed class MaintenanceViewModel : ObservableObject
{
    private const int MaxOutput = 200_000;
    private readonly MaintenanceService _svc;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private readonly List<MaintenanceItemViewModel> _items = new();
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<MaintenanceGroupViewModel> Groups { get; } = new();
    public AsyncCommand QuickCommand { get; }
    public AsyncCommand FullCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearOutputCommand { get; }

    public MaintenanceViewModel(MaintenanceService svc, IDialogService dialogs, IShellHost shell)
    {
        _svc = svc;
        _dialogs = dialogs;
        _shell = shell;
        foreach (var (id, name) in MaintenanceService.Groups)
        {
            var items = svc.Tasks.Where(t => t.Group == id).Select(t =>
            {
                MaintenanceItemViewModel? vm = null;
                vm = new MaintenanceItemViewModel { Task = t, RunCommand = new AsyncCommand(() => RunSingleAsync(vm!), () => !IsRunning) };
                return vm;
            }).ToList();
            _items.AddRange(items);
            if (items.Count > 0) Groups.Add(new MaintenanceGroupViewModel { Name = name, Items = items });
        }
        QuickCommand = new AsyncCommand(() => RunSetAsync(_items.Where(i => i.Task.InQuick).ToList(), "Mantenimiento rápido"), () => !IsRunning);
        FullCommand = new AsyncCommand(() => RunSetAsync(_items.Where(i => i.Task.InFull).ToList(), "Mantenimiento completo"), () => !IsRunning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        ClearOutputCommand = new RelayCommand(() => { _output.Clear(); OnPropertyChanged(nameof(Output)); });
    }

    public string QuickList => string.Join(", ", _items.Where(i => i.Task.InQuick).Select(i => i.Name));
    public string FullList => string.Join(", ", _items.Where(i => i.Task.InFull).Select(i => i.Name));

    public string Output => _output.ToString();

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            QuickCommand.RaiseCanExecuteChanged();
            FullCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            foreach (var i in _items) (i.RunCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    private string _current = "";
    public string CurrentTask { get => _current; private set => SetProperty(ref _current, value); }

    private void Append(string line)
    {
        _output.AppendLine(line);
        if (_output.Length > MaxOutput) _output.Remove(0, _output.Length - MaxOutput);
        OnPropertyChanged(nameof(Output));
    }

    private async Task RunSingleAsync(MaintenanceItemViewModel item)
    {
        if (item.Task.Risk != "safe" || item.RequiresReboot)
        {
            var msg = item.Description + (item.HasNote ? $"\n\n{item.Note}" : "") +
                      (item.RequiresReboot ? "\n\nNecesitará reiniciar Windows." : "") + $"\n\nComando: {item.Command}";
            if (!await _dialogs.ConfirmAsync(item.Name, msg, "Ejecutar")) return;
        }
        await RunAsync(new[] { item }, item.Name);
    }

    private async Task RunSetAsync(IReadOnlyList<MaintenanceItemViewModel> items, string title)
    {
        var msg = $"Se ejecutará:\n\n• {string.Join("\n• ", items.Select(i => $"{i.Name}  ({i.Duration})"))}\n\nPuedes seguir usando el PC, pero evita juegos pesados mientras termina.";
        if (!await _dialogs.ConfirmAsync(title, msg, "Empezar")) return;
        await RunAsync(items, title);
    }

    private async Task RunAsync(IReadOnlyList<MaintenanceItemViewModel> items, string title)
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(Append);
        long freed = 0;
        int failed = 0;
        bool reboot = false;
        Append($"══════ {title} · {DateTime.Now:HH:mm} ══════");
        try
        {
            foreach (var i in items) { i.Status = "En cola"; i.StatusColor = Labels.Slate; }
            foreach (var i in items)
            {
                if (_cts.IsCancellationRequested)
                {
                    i.Status = "Cancelado";
                    continue;
                }
                CurrentTask = i.Name;
                i.Status = "Ejecutando…";
                i.StatusColor = Labels.Cyan;
                Append($"── {i.Name}");
                MaintenanceResult r;
                try
                {
                    var ctx = new MaintenanceContext { Output = ((IProgress<string>)progress).Report, Cancel = _cts.Token };
                    r = await i.Task.Run(ctx);
                }
                catch (Exception ex)
                {
                    Log.Error($"Mantenimiento {i.Task.Id}", ex);
                    r = new MaintenanceResult { Ok = false, Summary = ex.Message };
                }
                freed += r.BytesFreed;
                if (!r.Ok) failed++;
                if (i.RequiresReboot && r.Ok) reboot = true;
                i.Status = r.Ok ? "✓ " + r.Summary : "⚠ " + r.Summary;
                i.StatusColor = r.Ok ? Labels.Green : Labels.Amber;
                // Deja que la consola reciba las últimas líneas antes del resumen
                await System.Threading.Tasks.Task.Delay(50);
                Append(r.Summary);
            }
        }
        finally
        {
            IsRunning = false;
            CurrentTask = "";
            _cts.Dispose();
            _cts = null;
        }
        var summary = $"{title} terminado" + (freed > 0 ? $": {MaintenanceService.FormatBytes(freed)} liberados" : "") +
                      (failed > 0 ? $" · {failed} con avisos (revisa la consola)" : "");
        Append($"══════ {summary} ══════");
        if (reboot) _shell.MarkRebootRequired();
        _shell.Toast(summary, failed > 0);
    }
}
