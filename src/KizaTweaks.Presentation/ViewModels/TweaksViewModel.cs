using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Text;
using KizaTweaks.Core;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Hardware;
using KizaTweaks.Core.Platform;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class TweakItemViewModel : ObservableObject
{
    public TweakDefinition Def { get; }
    public TweakItemViewModel(TweakDefinition def, string categoryName, bool compatible)
    {
        Def = def;
        CategoryName = categoryName;
        IsCompatible = compatible;
        (ImpactLabel, ImpactColor) = Labels.Impact(def.Impact);
        (RiskLabel, RiskColor) = Labels.Risk(def.Risk);
    }

    public string Name => Def.Name;
    public string Description => Def.Description;
    public string? Note => Def.Note;
    public bool HasNote => !string.IsNullOrWhiteSpace(Def.Note);
    public string CategoryName { get; }
    public string ImpactLabel { get; }
    public string ImpactColor { get; }
    public string RiskLabel { get; }
    public string RiskColor { get; }
    public bool RequiresReboot => Def.RequiresReboot;
    public bool IsExperimental => Def.IsExperimental;
    public bool IsCompatible { get; }
    public string Details => string.Join("\n", Def.Apply.Select(o => "• " + o));

    private TweakState _state = TweakState.Unknown;
    public TweakState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(StateColor));
                OnPropertyChanged(nameof(IsApplied));
            }
        }
    }
    public string StateLabel => IsCompatible ? Labels.State(State).text : "No compatible";
    public string StateColor => IsCompatible ? Labels.State(State).color : Labels.Slate;
    public bool IsApplied => State == TweakState.Applied;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value && IsCompatible)) SelectionChanged?.Invoke(); }
    }
    internal Action? SelectionChanged { get; set; }

    private bool _showDetails;
    public bool ShowDetails { get => _showDetails; set => SetProperty(ref _showDetails, value); }
}

public sealed class CategoryFilterViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Glyph { get; init; } = "";
    private int _count;
    public int Count { get => _count; set => SetProperty(ref _count, value); }
}

public sealed record ProfileOption(string Id, string Name, string Description, string Glyph);

[SupportedOSPlatform("windows")]
public sealed class TweaksViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private readonly List<TweakItemViewModel> _all;

    public ObservableCollection<TweakItemViewModel> Visible { get; } = new();
    public ObservableCollection<CategoryFilterViewModel> Categories { get; } = new();
    public IReadOnlyList<ProfileOption> ProfilesList { get; } =
        ProfileResolver.All.Select(p => new ProfileOption(p.Id, p.Name, p.Description, p.Glyph))
            .Append(new ProfileOption(ProfileResolver.None, "Limpiar selección", "", "\uE894")).ToList();

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ApplySelectedCommand { get; }
    public AsyncCommand RevertSelectedCommand { get; }
    public RelayCommand SelectProfileCommand { get; }
    public RelayCommand SelectVisibleCommand { get; }
    public RelayCommand ToggleDetailsCommand { get; }

    public TweaksViewModel(AppServices s, IDialogService dialogs, IShellHost shell)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        var catNames = s.Catalog.Categories.ToDictionary(c => c.Id, c => c.Name);
        var order = s.Catalog.Categories.Select(c => c.Id).ToList();
        _all = s.Catalog.Tweaks.Where(t => !t.Retired)
            .OrderBy(t => order.IndexOf(t.Category)).ThenBy(t => t.Risk == "safe" ? 0 : t.Risk == "moderate" ? 1 : 2).ThenBy(t => t.Name)
            .Select(t => new TweakItemViewModel(t, catNames.GetValueOrDefault(t.Category, t.Category), t.IsCompatible(s.Build)))
            .ToList();
        foreach (var t in _all) t.SelectionChanged = () => OnPropertyChanged(nameof(SelectedCount));

        Categories.Add(new CategoryFilterViewModel { Id = "all", Name = "Todos", Glyph = "", Count = _all.Count });
        foreach (var c in s.Catalog.Categories)
        {
            var n = _all.Count(t => t.Def.Category == c.Id);
            if (n > 0) Categories.Add(new CategoryFilterViewModel { Id = c.Id, Name = c.Name, Glyph = c.Icon, Count = n });
        }
        _selectedCategory = Categories[0];

        RefreshCommand = new AsyncCommand(RefreshAsync);
        ApplySelectedCommand = new AsyncCommand(ApplySelectedAsync);
        RevertSelectedCommand = new AsyncCommand(RevertSelectedAsync);
        SelectProfileCommand = new RelayCommand(p => _ = SelectProfileAsync(p as string ?? ProfileResolver.None));
        SelectVisibleCommand = new RelayCommand(() =>
        {
            var allSelected = Visible.Where(v => v.IsCompatible).All(v => v.IsSelected);
            foreach (var v in Visible) v.IsSelected = !allSelected;
        });
        ToggleDetailsCommand = new RelayCommand(p => { if (p is TweakItemViewModel t) t.ShowDetails = !t.ShowDetails; });
        ApplyFilter();
    }

    public IReadOnlyList<TweakItemViewModel> All => _all;

    private CategoryFilterViewModel _selectedCategory;
    public CategoryFilterViewModel SelectedCategory
    {
        get => _selectedCategory;
        set { if (value is not null && SetProperty(ref _selectedCategory, value)) ApplyFilter(); }
    }

    private string _search = "";
    public string SearchText
    {
        get => _search;
        set { if (SetProperty(ref _search, value ?? "")) ApplyFilter(); }
    }

    private bool _hideApplied;
    public bool HideApplied
    {
        get => _hideApplied;
        set { if (SetProperty(ref _hideApplied, value)) ApplyFilter(); }
    }

    public int SelectedCount => _all.Count(t => t.IsSelected);
    public int AppliedCount => _all.Count(t => t.IsApplied);

    private bool _loaded;
    public bool Loaded { get => _loaded; private set => SetProperty(ref _loaded, value); }

    private void ApplyFilter()
    {
        var q = _search.Trim();
        var items = _all.Where(t =>
            (_selectedCategory.Id == "all" || t.Def.Category == _selectedCategory.Id) &&
            (!_hideApplied || !t.IsApplied) &&
            (q.Length == 0 || t.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                           || t.Description.Contains(q, StringComparison.OrdinalIgnoreCase)));
        Visible.Clear();
        foreach (var t in items) Visible.Add(t);
    }

    public async Task RefreshAsync()
    {
        var states = await Task.Run(() => _all.ToDictionary(t => t, t => t.IsCompatible ? _s.Engine.GetState(t.Def) : TweakState.Unknown));
        foreach (var (t, st) in states) t.State = st;
        Loaded = true;
        OnPropertyChanged(nameof(AppliedCount));
        if (_hideApplied) ApplyFilter();
    }

    /// <summary>Marca los ajustes del perfil (adaptado al hardware) que aún no están aplicados.</summary>
    public async Task<ProfileSelection> SelectProfileAsync(string id, bool quiet = false)
    {
        HardwareReport? hw = null;
        if (id == Profiles.Performance)
        {
            try { hw = await _s.GetHardwareAsync(); }
            catch (Exception ex) { Log.Warn("No se pudo detectar el hardware para el perfil: " + ex.Message); }
        }
        var sel = ProfileResolver.Resolve(id, _s.Catalog, hw);
        foreach (var t in _all) t.IsSelected = sel.Ids.Contains(t.Def.Id) && !t.IsApplied;
        SelectedCategory = Categories[0];
        OnPropertyChanged(nameof(SelectedCount));
        if (id != ProfileResolver.None && !quiet)
        {
            var extra = sel.AdjustedForHardware.Count > 0 ? " " + string.Join(" ", sel.AdjustedForHardware) : "";
            _shell.Toast(SelectedCount == 0
                ? "Todo lo de este perfil ya está aplicado." + extra
                : $"{SelectedCount} ajustes seleccionados. Revisa la lista y pulsa «Aplicar seleccionados».{extra}");
        }
        return sel;
    }

    /// <summary>Selecciona solo los tweaks indicados (usado desde el inicio y las recomendaciones).</summary>
    public void SelectOnly(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        foreach (var t in _all) t.IsSelected = set.Contains(t.Def.Id);
        OnPropertyChanged(nameof(SelectedCount));
    }

    public async Task ApplySelectedAsync()
    {
        var targets = _all.Where(t => t.IsSelected && t.IsCompatible).Select(t => t.Def).ToList();
        if (targets.Count == 0)
        {
            await _dialogs.InfoAsync("Nada seleccionado", "Marca los ajustes que quieres aplicar o elige un perfil.");
            return;
        }
        await ApplyAsync(targets);
    }

    /// <summary>Flujo completo: advertencias, punto de restauración, aplicar, reiniciar Explorador.</summary>
    public async Task ApplyAsync(IReadOnlyList<TweakDefinition> targets, bool askConfirm = true)
    {
        targets = ProfileResolver.ApplyOrder(targets);
        var risky = targets.Where(t => t.Risk != "safe" || t.IsExperimental).ToList();
        var sb = new StringBuilder($"Se aplicarán {targets.Count} ajustes.");
        if (risky.Count > 0)
        {
            sb.Append("\n\nRevisa estos, tienen efectos secundarios:\n");
            foreach (var t in risky)
                sb.Append($"\n• {t.Name} ({Labels.Risk(t.Risk).text}{(t.IsExperimental ? ", experimental" : "")})" +
                          (string.IsNullOrWhiteSpace(t.Note) ? "" : $"\n   {t.Note}"));
        }
        sb.Append("\n\nTodo se puede deshacer desde «Respaldos».");

        if (!_s.RestorePointCreated)
            sb.Append("\n\nAntes se creará un punto de restauración de Windows, para que puedas volver atrás aunque algo falle (tarda menos de un minuto).");
        if (askConfirm && await _dialogs.ChooseAsync("Aplicar ajustes", sb.ToString(), "Aplicar", "Cancelar") != 0) return;

        // Punto de restauración obligatorio (una vez por sesión). Si falla, se pregunta antes de seguir.
        if (!_s.RestorePointCreated)
        {
            (bool ok, string msg) rp = default;
            await _shell.RunBusyAsync("Creando punto de restauración…", async _ =>
            {
                try { rp = await Task.Run(() => _s.CreateRestorePoint("KizaTweaks - antes de optimizar")); }
                catch (Exception ex) { rp = (false, ex.Message); }
            });
            if (rp.ok) _s.RestorePointCreated = true;
            else if (await _dialogs.ChooseAsync("Sin punto de restauración",
                         $"No se pudo crear el punto de restauración:\n{rp.msg}\n\nLos ajustes igual se pueden deshacer desde «Respaldos». ¿Aplicar de todas formas?",
                         "Aplicar igual", "Cancelar") != 0) return;
        }

        var results = new List<TweakRunResult>();
        await _shell.RunBusyAsync("Preparando…", async progress =>
        {
            int i = 0;
            foreach (var t in targets)
            {
                progress.Report($"Aplicando {++i}/{targets.Count}: {t.Name}");
                results.Add(await Task.Run(() => _s.Engine.Apply(t)));
            }
            if (results.Any(r => r.RestartExplorer))
            {
                progress.Report("Reiniciando el Explorador para aplicar cambios visuales…");
                await Task.Run(SystemActions.RestartExplorer);
            }
            progress.Report("Verificando…");
            await RefreshAsync();
        });

        await ReportAsync("Ajustes aplicados", results);
    }

    public async Task RevertSelectedAsync()
    {
        var targets = _all.Where(t => t.IsSelected).Select(t => t.Def).ToList();
        if (targets.Count == 0)
        {
            await _dialogs.InfoAsync("Nada seleccionado", "Marca los ajustes que quieres deshacer.");
            return;
        }
        if (!await _dialogs.ConfirmAsync("Deshacer ajustes",
                $"Se restaurará el estado anterior de {targets.Count} ajustes.", "Deshacer")) return;
        await RevertAsync(targets);
    }

    public async Task RevertAsync(IReadOnlyList<TweakDefinition> targets)
    {
        var results = new List<TweakRunResult>();
        await _shell.RunBusyAsync("Deshaciendo…", async progress =>
        {
            int i = 0;
            foreach (var t in targets)
            {
                progress.Report($"Deshaciendo {++i}/{targets.Count}: {t.Name}");
                results.Add(await Task.Run(() => _s.Engine.Revert(t)));
            }
            if (results.Any(r => r.RestartExplorer)) await Task.Run(SystemActions.RestartExplorer);
            await RefreshAsync();
        });
        await ReportAsync("Ajustes deshechos", results);
    }

    private async Task ReportAsync(string title, List<TweakRunResult> results)
    {
        foreach (var t in _all) t.IsSelected = false;
        OnPropertyChanged(nameof(SelectedCount));
        if (results.Any(r => r.RequiresReboot)) _shell.MarkRebootRequired();
        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Count == 0)
        {
            _shell.Toast($"{title}: {results.Count} correctos." + (results.Any(r => r.RequiresReboot) ? " Algunos necesitan reiniciar." : ""));
            return;
        }
        var sb = new StringBuilder($"{results.Count - failed.Count} correctos, {failed.Count} con errores:\n");
        foreach (var f in failed.Take(10))
            sb.Append($"\n• {f.TweakName}: {f.Errors.FirstOrDefault()}");
        sb.Append($"\n\nDetalles en el registro: {AppPaths.LogsDir}");
        await _dialogs.InfoAsync(title, sb.ToString());
    }
}
