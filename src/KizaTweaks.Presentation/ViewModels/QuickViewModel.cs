using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Input;
using KizaTweaks.Core;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Maintenance;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

/// <summary>Un elemento de una acción rápida. Las apps se pueden marcar o desmarcar; lo demás es solo informativo.</summary>
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
    public System.Windows.Input.ICommand ToggleCommand => new RelayCommand(() => IsChecked = !IsChecked);
}

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

/// <summary>
/// Optimización rápida para un Windows recién instalado: quitar apps basura, limpiar, menos input delay y sin
/// anuncios ni rastreo. Nada que cambie cómo funcionan tus programas o juegos.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class QuickViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private readonly TweaksViewModel _tweaks;


    public QuickActionViewModel Apps { get; }
    public QuickActionViewModel Clean { get; }
    public QuickActionViewModel Delay { get; }
    public QuickActionViewModel Privacy { get; }
    public IReadOnlyList<QuickActionViewModel> Actions { get; }
    public IReadOnlyList<QuickActionViewModel> OtherActions { get; }
    public AsyncCommand DoAllCommand { get; }
    public AsyncCommand RescanJunkCommand { get; }
    public RelayCommand CheckAllAppsCommand { get; }
    public RelayCommand UncheckAllAppsCommand { get; }

    private void SetAllApps(bool value)
    {
        foreach (var i in Apps.Items) i.IsChecked = value;
    }

    private List<QuickItem> CheckedApps() => Apps.Items.Concat(Apps.Extra).Where(i => i.IsChecked && i.App is not null).ToList();

    public QuickViewModel(AppServices s, IDialogService dialogs, IShellHost shell, TweaksViewModel tweaks)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        _tweaks = tweaks;

        Apps = new QuickActionViewModel
        {
            Title = "Quitar apps basura", Glyph = "\uE71D", ButtonText = "Quitar",
            Description = "Lo que Windows instala o sugiere solo al formatear: iTunes, TikTok, Instagram, Netflix, juegos patrocinados y relleno de Microsoft. Desmarca lo que sí uses. Nunca toca Store, Xbox, Fotos, Paint, Calculadora, Bloc de notas ni drivers.",
            Command = new AsyncCommand(() => RemoveAppsAsync(confirm: true)),
        };
        Clean = new QuickActionViewModel
        {
            Title = "Limpiar el PC", Glyph = "\uE74D", ButtonText = "Limpiar",
            Description = "Revisa las carpetas de Windows y muestra cuánto ocupa lo que sobra: temporales, actualizaciones ya instaladas, registros viejos, restos de instalación y de drivers. Nunca toca tus documentos, descargas, escritorio, papelera ni juegos.",
            Command = new AsyncCommand(() => CleanAsync(confirm: true)),
        };
        Delay = new QuickActionViewModel
        {
            Title = "Menos input delay", Glyph = "\uE962", ButtonText = "Aplicar",
            Description = "El mouse y el teclado responden antes. No cambia nada de tus juegos ni apps.",
            Command = new AsyncCommand(() => ApplyTweaksAsync(DelayIds(), confirm: true)),
        };
        Privacy = new QuickActionViewModel
        {
            Title = "Sin anuncios ni rastreo", Glyph = "\uE72E", ButtonText = "Aplicar",
            Description = "Quita anuncios, sugerencias, telemetría, Copilot y Recall de Windows. Ninguna función que uses deja de servir.",
            Command = new AsyncCommand(() => ApplyTweaksAsync(QuickPlan.PrivacyTweaks, confirm: true)),
        };
        Actions = new[] { Apps, Clean, Delay, Privacy };
        OtherActions = new[] { Delay, Privacy };
        DoAllCommand = new AsyncCommand(DoAllAsync);

        foreach (var c in JunkScanner.Categories())
        {
            var item = new QuickItem { Text = c.Name, Detail = c.Description, Junk = c, IsCheckable = true, IsChecked = c.DefaultChecked };
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(QuickItem.IsChecked) && !Clean.IsDone) UpdateCleanStatus(); };
            Clean.Items.Add(item);
        }
        RescanJunkCommand = new AsyncCommand(ScanJunkAsync);
        CheckAllAppsCommand = new RelayCommand(() => SetAllApps(true));
        UncheckAllAppsCommand = new RelayCommand(() => SetAllApps(false));
        foreach (var t in tweaks.All)
            t.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TweakItemViewModel.State)) UpdateTweakStatus(); };
    }

    private HashSet<string> DelayIds() => QuickPlan.DelayTweaks(_s.Catalog);

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            if (!_tweaks.Loaded) await _tweaks.RefreshAsync();
            FillTweakItems(Delay, DelayIds());
            FillTweakItems(Privacy, QuickPlan.PrivacyTweaks);
            UpdateTweakStatus();
            await Task.WhenAll(ScanAppsAsync(), ScanJunkAsync());
        }
        finally { IsLoading = false; }
    }

    private void FillTweakItems(QuickActionViewModel action, IEnumerable<string> ids)
    {
        action.Items.Clear();
        foreach (var t in _tweaks.All.Where(t => t.IsCompatible && ids.Contains(t.Def.Id)))
            action.Items.Add(new QuickItem { Text = t.Name });
    }

    private void UpdateTweakStatus()
    {
        void Set(QuickActionViewModel a, IEnumerable<string> ids)
        {
            var items = _tweaks.All.Where(t => t.IsCompatible && ids.Contains(t.Def.Id)).ToList();
            var applied = items.Count(t => t.IsApplied);
            a.IsDone = items.Count > 0 && applied == items.Count;
            a.Status = a.IsDone ? "Listo: todo aplicado." : $"{applied} de {items.Count} aplicados.";
        }
        Set(Delay, DelayIds());
        Set(Privacy, QuickPlan.PrivacyTweaks);
    }

    private async Task ScanAppsAsync()
    {
        HashSet<string> appx;
        try { appx = await Task.Run(() => _s.Apps.GetInstalledAppxNames()); }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron listar las apps: " + ex.Message);
            appx = new();
        }
        var skipped = Apps.Items.Concat(Apps.Extra).Where(i => !i.IsChecked && i.App is not null).Select(i => i.App!.PrimaryId).ToHashSet();
        var junk = QuickPlan.Apps(_s.AppsCatalog).Where(a => _s.Apps.IsInstalled(a, appx)).ToList();
        Apps.Items.Clear();
        foreach (var a in junk.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            Apps.Items.Add(new QuickItem { Text = a.Name, App = a, IsCheckable = true, IsChecked = !skipped.Contains(a.PrimaryId) });

        // Otras apps de la Store que no están en el catálogo: se muestran desmarcadas.
        List<(string Id, string Name)> others;
        try { others = await Task.Run(() => _s.Apps.GetOtherStoreApps()); }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron listar otras apps: " + ex.Message);
            others = new();
        }
        var catalogIds = _s.AppsCatalog.Apps.SelectMany(a => a.Ids).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkedExtra = Apps.Extra.Where(i => i.IsChecked && i.App is not null).Select(i => i.App!.PrimaryId).ToHashSet();
        Apps.Extra.Clear();
        foreach (var (id, name) in others.Where(o => !catalogIds.Contains(o.Id)).OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var def = new AppDefinition { Ids = new() { id }, Name = name, Method = "Appx", Recommendation = "optional" };
            Apps.Extra.Add(new QuickItem { Text = name, Detail = id, App = def, IsCheckable = true, IsChecked = checkedExtra.Contains(id) });
        }
        OnPropertyChanged(nameof(HasExtraApps));

        Apps.IsDone = Apps.Items.Count == 0;
        Apps.Status = Apps.IsDone ? "Listo: no hay apps basura instaladas." : $"{Apps.Items.Count} apps basura instaladas.";
    }

    public bool HasExtraApps => Apps.Extra.Count > 0;

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

    // ------------------------------------------------------------------

    private async Task ApplyTweaksAsync(IEnumerable<string> ids, bool confirm)
    {
        var targets = _tweaks.All.Where(t => t.IsCompatible && !t.IsApplied && ids.Contains(t.Def.Id)).Select(t => t.Def).ToList();
        if (targets.Count == 0)
        {
            if (confirm) _shell.Toast("Ya está todo aplicado.");
            return;
        }
        await _tweaks.ApplyAsync(targets, askConfirm: confirm);
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

    private async Task DoAllAsync()
    {
        var tweakIds = DelayIds().Concat(QuickPlan.PrivacyTweaks).ToHashSet();
        var pendingTweaks = _tweaks.All.Count(t => t.IsCompatible && !t.IsApplied && tweakIds.Contains(t.Def.Id));
        if (!await _dialogs.ConfirmAsync("Optimizar todo",
                "Se hará todo esto, en orden:\n\n" +
                "• Punto de restauración de Windows (para volver atrás si quieres)\n" +
                $"• {pendingTweaks} ajustes de input delay, anuncios y rastreo\n" +
                $"• Quitar {CheckedApps().Count} apps marcadas\n" +
                $"• Limpiar {Clean.Items.Count(i => i.IsChecked)} tipos de archivos que sobran" +
                (Clean.Items.Any(i => i.IsChecked && i.Junk?.Id == "windows-old") ? " (incluye Windows.old: saca antes tus archivos de ahí)" : "") + "\n\n" +
                "Nada de esto cambia cómo funcionan tus programas o juegos.", "Optimizar todo")) return;

        await ApplyTweaksAsync(tweakIds, confirm: false);
        var apps = await RemoveAppsAsync(confirm: false);
        var bytes = await CleanAsync(confirm: false);
        UpdateTweakStatus();
        _shell.Toast($"Listo: ajustes aplicados, {apps} apps quitadas y {MaintenanceService.FormatBytes(bytes)} liberados. Reinicia cuando puedas.");
    }
}
