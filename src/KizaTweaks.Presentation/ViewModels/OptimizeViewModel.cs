using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Input;
using KizaTweaks.Core;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Hardware;
using KizaTweaks.Core.Maintenance;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

/// <summary>Un elemento con casilla: una app para quitar o un tipo de archivos para limpiar.</summary>
public sealed class QuickItem : ObservableObject
{
    public required string Text { get; init; }
    public string Detail { get; init; } = "";
    public bool IsCheckable { get; init; }
    public AppDefinition? App { get; init; }
    public JunkCategory? Junk { get; init; }
    private string _size = "";
    public string Size { get => _size; set => SetProperty(ref _size, value); }
    public long Bytes { get; set; } = -1;
    private bool _checked = true;
    public bool IsChecked { get => _checked; set => SetProperty(ref _checked, value); }
    public ICommand ToggleCommand => new RelayCommand(() => IsChecked = !IsChecked);
}

/// <summary>Tarjeta con casillas (apps o limpieza).</summary>
public sealed class QuickActionViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required string ButtonText { get; init; }
    public required ICommand Command { get; init; }
    public ObservableCollection<QuickItem> Items { get; } = new();
    /// <summary>Solo apps: otras apps de la Store que no están en la lista (desmarcadas).</summary>
    public ObservableCollection<QuickItem> Extra { get; } = new();

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    private bool _done;
    public bool IsDone { get => _done; set => SetProperty(ref _done, value); }
}

/// <summary>Una sección de la optimización segura, con un interruptor por ajuste.</summary>
public sealed class SafeSectionViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required ICommand ActivateCommand { get; init; }
    public List<FpsTweakViewModel> Items { get; } = new();
    public int Active => Items.Count(i => i.IsApplied);
    public string ActiveText => $"{Active} de {Items.Count} activos";
    public bool IsComplete => Items.Count > 0 && Active == Items.Count;
    public void Refresh()
    {
        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(ActiveText));
        OnPropertyChanged(nameof(IsComplete));
    }
}

/// <summary>
/// Página principal: optimización segura de un clic. Quita lo inservible, baja el input lag, sube el
/// rendimiento sin tocar cómo funcionan las apps, quita anuncios y limpia. Lo que tiene riesgo va en «Avanzado».
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OptimizeViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private readonly TweaksViewModel _tweaks;
    private HardwareReport? _hw;

    public ObservableCollection<SafeSectionViewModel> Sections { get; } = new();
    public QuickActionViewModel Apps { get; }
    public QuickActionViewModel Clean { get; }
    public AsyncCommand DoAllCommand { get; }
    public AsyncCommand RescanJunkCommand { get; }
    public RelayCommand CheckAllAppsCommand { get; }
    public RelayCommand UncheckAllAppsCommand { get; }
    public RelayCommand GoAdvancedCommand { get; }

    public OptimizeViewModel(AppServices s, IDialogService dialogs, IShellHost shell, TweaksViewModel tweaks)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        _tweaks = tweaks;

        Apps = new QuickActionViewModel
        {
            Title = "Apps que no instalaste", Glyph = "\uE71D", ButtonText = "Quitar marcadas",
            Description = "Lo que Windows instala o sugiere solo al formatear. Desmarca lo que sí uses. Nunca toca Store, Xbox, Fotos, Paint, Calculadora, Bloc de notas ni apps de drivers.",
            Command = new AsyncCommand(() => RemoveAppsAsync(confirm: true)),
        };
        Clean = new QuickActionViewModel
        {
            Title = "Limpiar el PC", Glyph = "\uE74D", ButtonText = "Limpiar",
            Description = "Revisa las carpetas de Windows y muestra cuánto ocupa lo que sobra. Nunca toca tus documentos, descargas, escritorio, papelera ni juegos.",
            Command = new AsyncCommand(() => CleanAsync(confirm: true)),
        };
        foreach (var c in JunkScanner.Categories())
        {
            var item = new QuickItem { Text = c.Name, Detail = c.Description, Junk = c, IsCheckable = true, IsChecked = c.DefaultChecked };
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(QuickItem.IsChecked) && !Clean.IsDone) UpdateCleanStatus(); };
            Clean.Items.Add(item);
        }

        DoAllCommand = new AsyncCommand(DoAllAsync);
        RescanJunkCommand = new AsyncCommand(ScanJunkAsync);
        CheckAllAppsCommand = new RelayCommand(() => { foreach (var i in Apps.Items) i.IsChecked = true; });
        UncheckAllAppsCommand = new RelayCommand(() => { foreach (var i in Apps.Items) i.IsChecked = false; });
        GoAdvancedCommand = new RelayCommand(() => _shell.Navigate("advanced"));

        foreach (var t in tweaks.All)
            t.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TweakItemViewModel.State)) UpdateScore(); };
    }

    // ------------------------------------------------------------------
    // Estado y puntaje
    // ------------------------------------------------------------------

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    private int _percent;
    public int Percent { get => _percent; private set => SetProperty(ref _percent, value); }
    private string _scoreText = "";
    public string ScoreText { get => _scoreText; private set => SetProperty(ref _scoreText, value); }
    private string _hardwareText = "";
    public string HardwareText { get => _hardwareText; private set => SetProperty(ref _hardwareText, value); }
    private int _pending;
    public int Pending { get => _pending; private set { SetProperty(ref _pending, value); OnPropertyChanged(nameof(IsComplete)); } }
    public bool IsComplete => Pending == 0 && Sections.Count > 0;

    private IEnumerable<FpsTweakViewModel> AllItems => Sections.SelectMany(s => s.Items);

    private void UpdateScore()
    {
        foreach (var sec in Sections) sec.Refresh();
        var items = AllItems.ToList();
        var applied = items.Count(i => i.IsApplied);
        Percent = items.Count == 0 ? 0 : (int)Math.Round(100.0 * applied / items.Count);
        Pending = items.Count - applied;
        ScoreText = Pending == 0 && items.Count > 0
            ? "Todo lo seguro está activo."
            : $"{applied} de {items.Count} ajustes seguros activos.";
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            if (!_tweaks.Loaded) await _tweaks.RefreshAsync();
            try { _hw = await _s.GetHardwareAsync(); }
            catch (Exception ex) { Log.Warn("Hardware: " + ex.Message); }
            HardwareText = _hw is { } r
                ? $"{(r.IsLaptop ? "Portátil" : "Escritorio")} · {r.Cpu.Name} · {r.MainGpu?.Name ?? "GPU sin identificar"}" +
                  (r.RamTotalBytes > 0 ? $" · {Math.Round(r.RamTotalBytes / (double)(1L << 30))} GB de RAM" : "")
                : "";
            BuildSections();
            UpdateScore();
            await Task.WhenAll(ScanAppsAsync(), ScanJunkAsync());
        }
        finally { IsLoading = false; }
    }

    private void BuildSections()
    {
        Sections.Clear();
        var byId = _tweaks.All.ToDictionary(t => t.Def.Id);
        foreach (var sec in SafePlan.Sections(_s.Catalog, _hw))
        {
            SafeSectionViewModel? vm = null;
            vm = new SafeSectionViewModel
            {
                Id = sec.Id, Title = sec.Title, Description = sec.Description, Glyph = sec.Glyph,
                ActivateCommand = new AsyncCommand(() => ActivateSectionAsync(vm!)),
            };
            foreach (var id in sec.TweakIds)
            {
                if (!byId.TryGetValue(id, out var item) || !item.IsCompatible) continue;
                vm.Items.Add(new FpsTweakViewModel(item, new AsyncCommand(() => ToggleAsync(item)), null));
            }
            if (vm.Items.Count > 0) Sections.Add(vm);
        }
    }

    // ------------------------------------------------------------------
    // Ajustes
    // ------------------------------------------------------------------

    private async Task ToggleAsync(TweakItemViewModel item)
    {
        if (item.IsApplied) await _tweaks.RevertAsync(new[] { item.Def });
        else await _tweaks.ApplyAsync(new[] { item.Def }, askConfirm: false);
        UpdateScore();
    }

    private async Task ActivateSectionAsync(SafeSectionViewModel sec)
    {
        var targets = sec.Items.Where(i => !i.IsApplied).Select(i => i.Item.Def).ToList();
        if (targets.Count == 0)
        {
            _shell.Toast("Esta sección ya está toda activa.");
            return;
        }
        await _tweaks.ApplyAsync(targets);
        UpdateScore();
    }

    // ------------------------------------------------------------------
    // Apps
    // ------------------------------------------------------------------

    public bool HasExtraApps => Apps.Extra.Count > 0;

    private List<QuickItem> CheckedApps() => Apps.Items.Concat(Apps.Extra).Where(i => i.IsChecked && i.App is not null).ToList();

    private async Task ScanAppsAsync()
    {
        HashSet<string> appx;
        try { appx = await Task.Run(() => _s.Apps.GetInstalledAppxNames()); }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron listar las apps: " + ex.Message);
            appx = new();
        }
        var previous = Apps.Items.Concat(Apps.Extra).Where(i => i.App is not null)
                           .ToDictionary(i => i.App!.PrimaryId, i => i.IsChecked, StringComparer.OrdinalIgnoreCase);
        var junk = SafePlan.Apps(_s.AppsCatalog).Where(a => _s.Apps.IsInstalled(a, appx)).ToList();
        Apps.Items.Clear();
        foreach (var a in junk.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var check = previous.TryGetValue(a.PrimaryId, out var was) ? was : !a.Ids.Any(SafePlan.AppsUncheckedByDefault.Contains);
            Apps.Items.Add(new QuickItem { Text = a.Name, App = a, IsCheckable = true, IsChecked = check });
        }

        // Otras apps de la Store que no están en el catálogo: se muestran desmarcadas.
        List<(string Id, string Name)> others;
        try { others = await Task.Run(() => _s.Apps.GetOtherStoreApps()); }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron listar otras apps: " + ex.Message);
            others = new();
        }
        var catalogIds = _s.AppsCatalog.Apps.SelectMany(a => a.Ids).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Apps.Extra.Clear();
        foreach (var (id, name) in others.Where(o => !catalogIds.Contains(o.Id)).OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var def = new AppDefinition { Ids = new() { id }, Name = name, Method = "Appx", Recommendation = "optional" };
            Apps.Extra.Add(new QuickItem { Text = name, Detail = id, App = def, IsCheckable = true, IsChecked = previous.GetValueOrDefault(id) });
        }
        OnPropertyChanged(nameof(HasExtraApps));

        Apps.IsDone = Apps.Items.Count == 0;
        Apps.Status = Apps.IsDone ? "Listo: no hay apps basura instaladas." : $"{Apps.Items.Count} apps que Windows instaló solo.";
    }

    private async Task<int> RemoveAppsAsync(bool confirm)
    {
        var targets = CheckedApps().Select(i => i.App!).ToList();
        if (targets.Count == 0)
        {
            if (confirm) _shell.Toast(Apps.Items.Count == 0 ? "No hay apps basura instaladas." : "No marcaste ninguna app.");
            return 0;
        }
        if (confirm && !await _dialogs.ConfirmAsync("Quitar apps",
                "Se quitarán:\n\n" + string.Join("\n", targets.Select(a => "• " + a.Name)) +
                "\n\nSi alguna te hace falta, se reinstala gratis desde Microsoft Store.", "Quitar")) return 0;
        int ok = 0;
        await _shell.RunBusyAsync("Quitando apps…", async progress =>
        {
            int i = 0;
            foreach (var a in targets)
            {
                progress.Report($"Quitando {++i}/{targets.Count}: {a.Name}");
                if ((await Task.Run(() => _s.Apps.Remove(a))).Ok) ok++;
            }
            await ScanAppsAsync();
        });
        if (confirm) _shell.Toast($"{ok} apps quitadas.");
        return ok;
    }

    // ------------------------------------------------------------------
    // Limpieza
    // ------------------------------------------------------------------

    private async Task ScanJunkAsync()
    {
        Clean.Status = "Revisando carpetas…";
        foreach (var i in Clean.Items) i.Size = i.Junk!.Measurable ? "…" : "";
        foreach (var i in Clean.Items.Where(i => i.Junk!.Measurable))
        {
            var b = await Task.Run(() => JunkScanner.Measure(i.Junk!));
            i.Bytes = b;
            i.Size = b <= 0 ? "vacío" : MaintenanceService.FormatBytes(b);
        }
        UpdateCleanStatus();
    }

    private void UpdateCleanStatus()
    {
        var sel = Clean.Items.Where(i => i.IsChecked && i.Bytes > 0).Sum(i => i.Bytes);
        var all = Clean.Items.Where(i => i.Bytes > 0).Sum(i => i.Bytes);
        Clean.Status = $"Se pueden liberar {MaintenanceService.FormatBytes(all)} (marcado: {MaintenanceService.FormatBytes(sel)}).";
    }

    private async Task<long> CleanAsync(bool confirm)
    {
        var targets = Clean.Items.Where(i => i.IsChecked && i.Junk is not null).ToList();
        if (targets.Count == 0)
        {
            if (confirm) _shell.Toast("No marcaste nada para limpiar.");
            return 0;
        }
        if (confirm && targets.Any(t => t.Junk!.Id == "windows-old") && !await _dialogs.ConfirmAsync("Borrar Windows.old",
                "Windows.old es la copia del Windows de antes de formatear. Si dejaste ahí archivos tuyos (fotos, documentos), sácalos antes.\n\nDespués de borrarla no se puede volver al Windows anterior.", "Borrar")) return 0;
        long bytes = 0;
        await _shell.RunBusyAsync("Limpiando…", async progress =>
        {
            foreach (var item in targets)
            {
                var c = item.Junk!;
                progress.Report(c.Name + "…");
                try
                {
                    var before = c.Measurable ? await Task.Run(() => JunkScanner.Measure(c)) : 0;
                    var task = c.TaskId is null ? null : _s.Maintenance.Tasks.FirstOrDefault(t => t.Id == c.TaskId);
                    if (task is not null) await task.Run(new MaintenanceContext { Output = _ => { } });
                    else await Task.Run(() => JunkScanner.CleanFiles(c, _ => { }));
                    if (c.Measurable)
                    {
                        var after = await Task.Run(() => JunkScanner.Measure(c));
                        bytes += Math.Max(0, before - after);
                        item.Bytes = after;
                        item.Size = after <= 0 ? "vacío" : MaintenanceService.FormatBytes(after);
                    }
                }
                catch (Exception ex) { Log.Warn($"Limpieza {c.Id}: {ex.Message}"); }
            }
        });
        UpdateCleanStatus();
        Clean.Status = $"Última limpieza: {MaintenanceService.FormatBytes(bytes)} liberados.";
        Clean.IsDone = true;
        if (confirm) _shell.Toast($"Limpieza lista: {MaintenanceService.FormatBytes(bytes)} liberados.");
        return bytes;
    }

    // ------------------------------------------------------------------
    // Todo de una vez
    // ------------------------------------------------------------------

    private async Task DoAllAsync()
    {
        var pending = AllItems.Where(i => !i.IsApplied).Select(i => i.Item.Def).ToList();
        var apps = CheckedApps().Count;
        var clean = Clean.Items.Count(i => i.IsChecked);
        var withOld = Clean.Items.Any(i => i.IsChecked && i.Junk?.Id == "windows-old");
        if (await _dialogs.ChooseAsync("Optimizar todo",
                "Se hará todo esto, en orden:\n\n" +
                "• Punto de restauración de Windows (para volver atrás si quieres)\n" +
                $"• {pending.Count} ajustes seguros: inservibles, input lag, rendimiento y privacidad\n" +
                $"• Quitar {apps} apps marcadas\n" +
                $"• Limpiar {clean} tipos de archivos que sobran" + (withOld ? " (incluye Windows.old: saca antes tus archivos de ahí)" : "") + "\n\n" +
                "Nada de esto cambia cómo funcionan Photoshop, tus apps o tus juegos. Lo de «Avanzado» no se toca.",
                "Optimizar todo", "Cancelar") != 0) return;

        if (pending.Count > 0) await _tweaks.ApplyAsync(pending, askConfirm: false);
        var removed = await RemoveAppsAsync(confirm: false);
        var bytes = await CleanAsync(confirm: false);
        UpdateScore();
        _shell.Toast($"Listo: {pending.Count} ajustes, {removed} apps quitadas y {MaintenanceService.FormatBytes(bytes)} liberados. Reinicia cuando puedas.");
    }
}
