using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using KizaTweaks.Core;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Platform;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class BackupItemViewModel
{
    public required TweakBackup Backup { get; init; }
    public string Name => Backup.TweakName;
    public string When => $"Aplicado el {Backup.AppliedAt:dd/MM/yyyy HH:mm} · {Backup.Entries.Count} cambios respaldados";
    public required System.Windows.Input.ICommand UndoCommand { get; init; }
}

public sealed class HistoryItemViewModel
{
    public required HistoryEntry Entry { get; init; }
    public string Text => $"{Entry.Time:dd/MM HH:mm}  {(Entry.Action == "apply" ? "Aplicado" : "Deshecho")}: {Entry.Target}";
    public string Color => Entry.Ok ? Labels.Green : Labels.Red;
    public string? Detail => Entry.Detail;
}

[SupportedOSPlatform("windows")]
public sealed class BackupsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private readonly TweaksViewModel _tweaks;

    public ObservableCollection<BackupItemViewModel> Items { get; } = new();
    public ObservableCollection<HistoryItemViewModel> History { get; } = new();
    public RelayCommand RefreshCommand { get; }
    public AsyncCommand UndoAllCommand { get; }
    public AsyncCommand RestorePointCommand { get; }
    public RelayCommand OpenSystemRestoreCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }

    public BackupsViewModel(AppServices s, IDialogService dialogs, IShellHost shell, TweaksViewModel tweaks)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        _tweaks = tweaks;
        RefreshCommand = new RelayCommand(Refresh);
        UndoAllCommand = new AsyncCommand(UndoAllAsync);
        RestorePointCommand = new AsyncCommand(RestorePointAsync);
        OpenSystemRestoreCommand = new RelayCommand(() => ProcessRunner.OpenShell(ProcessRunner.System32("rstrui.exe")));
        OpenDataFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            ProcessRunner.OpenShell(AppPaths.DataDir);
        });
    }

    public bool IsEmpty => Items.Count == 0;

    public void Refresh()
    {
        Items.Clear();
        foreach (var b in _s.Engine.Store.All())
            Items.Add(new BackupItemViewModel { Backup = b, UndoCommand = new AsyncCommand(() => UndoAsync(b)) });
        History.Clear();
        foreach (var h in _s.Engine.ReadHistory(100)) History.Add(new HistoryItemViewModel { Entry = h });
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task UndoAsync(TweakBackup b)
    {
        var def = _s.Catalog.Find(b.TweakId);
        if (def is null)
        {
            await _dialogs.InfoAsync("No disponible", "Este ajuste ya no existe en el catálogo de esta versión.");
            return;
        }
        await _tweaks.RevertAsync(new[] { def });
        Refresh();
    }

    private async Task UndoAllAsync()
    {
        var defs = _s.Engine.Store.All().Select(b => _s.Catalog.Find(b.TweakId)).Where(d => d is not null).Cast<Core.Catalog.TweakDefinition>().ToList();
        if (defs.Count == 0)
        {
            _shell.Toast("No hay ajustes aplicados por KizaTweaks.");
            return;
        }
        if (!await _dialogs.ConfirmAsync("Deshacer todo",
                $"Se restaurará el estado original de {defs.Count} ajustes aplicados con KizaTweaks.", "Deshacer todo")) return;
        await _tweaks.RevertAsync(defs);
        Refresh();
    }

    private async Task RestorePointAsync()
    {
        (bool ok, string msg) r = default;
        await _shell.RunBusyAsync("Creando punto de restauración…", async _ =>
            r = await Task.Run(() => SystemActions.CreateRestorePoint("KizaTweaks - manual")));
        if (r.ok) _s.RestorePointCreated = true;
        _shell.Toast(r.ok ? "Punto de restauración creado." : $"No se pudo crear: {r.msg}", !r.ok);
    }
}
