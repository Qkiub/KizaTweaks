using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Text;
using KizaTweaks.Core.Apps;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class AppItemViewModel : ObservableObject
{
    public AppDefinition Def { get; }
    public AppItemViewModel(AppDefinition def)
    {
        Def = def;
        (RecommendationLabel, RecommendationColor) = Labels.AppRecommendation(def.Recommendation);
    }
    public string Name => Def.Name;
    public string Description => Def.Description;
    public string Id => Def.PrimaryId;
    public string RecommendationLabel { get; }
    public string RecommendationColor { get; }

    private bool _installed;
    public bool IsInstalled { get => _installed; set { if (SetProperty(ref _installed, value)) OnPropertyChanged(nameof(InstalledLabel)); } }
    public string InstalledLabel => IsInstalled ? "Instalada" : "No instalada";

    private bool _selected;
    public bool IsSelected { get => _selected; set { if (SetProperty(ref _selected, value)) SelectionChanged?.Invoke(); } }
    internal Action? SelectionChanged { get; set; }
}

[SupportedOSPlatform("windows")]
public sealed class AppsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private readonly List<AppItemViewModel> _all;

    public ObservableCollection<AppItemViewModel> Visible { get; } = new();
    public AsyncCommand ScanCommand { get; }
    public AsyncCommand RemoveSelectedCommand { get; }
    public RelayCommand SelectRecommendedCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }

    public AppsViewModel(AppServices s, IDialogService dialogs, IShellHost shell)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        _all = s.AppsCatalog.Apps.Select(a => new AppItemViewModel(a)).ToList();
        foreach (var a in _all) a.SelectionChanged = () => OnPropertyChanged(nameof(SelectedCount));
        ScanCommand = new AsyncCommand(ScanAsync);
        RemoveSelectedCommand = new AsyncCommand(RemoveSelectedAsync);
        SelectRecommendedCommand = new RelayCommand(() =>
        {
            foreach (var a in _all) a.IsSelected = a.IsInstalled && a.Def.SelectedByDefault;
        });
        ClearSelectionCommand = new RelayCommand(() => { foreach (var a in _all) a.IsSelected = false; });
    }

    private bool _scanned;
    public bool Scanned { get => _scanned; private set => SetProperty(ref _scanned, value); }

    private bool _onlyInstalled = true;
    public bool OnlyInstalled { get => _onlyInstalled; set { if (SetProperty(ref _onlyInstalled, value)) ApplyFilter(); } }

    private string _search = "";
    public string SearchText { get => _search; set { if (SetProperty(ref _search, value ?? "")) ApplyFilter(); } }

    public int SelectedCount => _all.Count(a => a.IsSelected);
    public bool ShowEmpty => Scanned && Visible.Count == 0;
    public int InstalledCount => _all.Count(a => a.IsInstalled);

    private void ApplyFilter()
    {
        var q = _search.Trim();
        Visible.Clear();
        foreach (var a in _all
                     .Where(a => (!_onlyInstalled || a.IsInstalled) &&
                                 (q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                  a.Description.Contains(q, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(a => a.Def.Recommendation switch { "safe" => 0, "optional" => 1, _ => 2 })
                     .ThenBy(a => a.Name))
            Visible.Add(a);
        OnPropertyChanged(nameof(ShowEmpty));
    }

    public async Task ScanAsync()
    {
        await _shell.RunBusyAsync("Buscando apps instaladas…", async progress =>
        {
            var appx = await Task.Run(() => _s.Apps.GetInstalledAppxNames());
            progress.Report("Revisando apps de WinGet…");
            var installed = await Task.Run(() => _all.ToDictionary(a => a, a => _s.Apps.IsInstalled(a.Def, appx)));
            foreach (var (a, inst) in installed)
            {
                a.IsInstalled = inst;
                if (!inst) a.IsSelected = false;
            }
        });
        Scanned = true;
        OnPropertyChanged(nameof(InstalledCount));
        ApplyFilter();
    }

    private async Task RemoveSelectedAsync()
    {
        var targets = _all.Where(a => a.IsSelected && a.IsInstalled).ToList();
        if (targets.Count == 0)
        {
            await _dialogs.InfoAsync("Nada seleccionado", "Marca las apps instaladas que quieres quitar.");
            return;
        }
        var sb = new StringBuilder($"Se desinstalarán {targets.Count} apps:\n");
        foreach (var a in targets.Take(25)) sb.Append($"\n• {a.Name}");
        if (targets.Count > 25) sb.Append($"\n• … y {targets.Count - 25} más");
        var unsafeOnes = targets.Where(a => a.Def.Recommendation != "safe").ToList();
        if (unsafeOnes.Count > 0)
        {
            sb.Append("\n\nAtención, estas no son de quita segura:\n");
            foreach (var a in unsafeOnes) sb.Append($"\n• {a.Name}: {a.Description}");
        }
        sb.Append("\n\nEsto no se deshace desde «Respaldos»: para recuperarlas, reinstálalas desde la Microsoft Store.");
        if (!await _dialogs.ConfirmAsync("Quitar apps", sb.ToString(), "Quitar")) return;

        var results = new List<AppRemovalResult>();
        await _shell.RunBusyAsync("Quitando apps…", async progress =>
        {
            int i = 0;
            foreach (var a in targets)
            {
                progress.Report($"Quitando {++i}/{targets.Count}: {a.Name}");
                results.Add(await Task.Run(() => _s.Apps.Remove(a.Def)));
            }
        });
        foreach (var a in _all) a.IsSelected = false;
        await ScanAsync();

        var failed = results.Where(r => !r.Ok).ToList();
        if (failed.Count == 0) _shell.Toast($"{results.Count} apps eliminadas.");
        else
        {
            var msg = new StringBuilder($"{results.Count - failed.Count} eliminadas, {failed.Count} con problemas:\n");
            foreach (var f in failed) msg.Append($"\n• {f.App.Name}: {f.Message}");
            await _dialogs.InfoAsync("Quitar apps", msg.ToString());
        }
    }
}
