using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class AdvancedGroupViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Glyph { get; init; }
    public List<FpsTweakViewModel> Items { get; } = new();
    public string ActiveText => $"{Items.Count(i => i.IsApplied)} de {Items.Count} activos";
    public void Refresh() => OnPropertyChanged(nameof(ActiveText));
}

/// <summary>
/// Ajustes con más efecto pero con riesgo: en algunos PCs hacen fallar apps (Photoshop, Illustrator) o juegos.
/// Nunca se activan solos; cada uno pide confirmación diciendo qué puede causar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AdvancedViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly TweaksViewModel _tweaks;

    public ObservableCollection<AdvancedGroupViewModel> Groups { get; } = new();
    public AsyncCommand RevertAllCommand { get; }

    private static readonly (string Title, string Subtitle, string Glyph, string[] Ids)[] Layout =
    {
        ("GPU y pantalla", "Pueden bajar la latencia o subir FPS, pero son los que más chocan con apps que usan la GPU como Photoshop e Illustrator.", "",
            new[] { "kiza.hags", "kiza.windowedOpt", "kiza.vrr", "kiza.disableMpo", "kiza.fseGlobal", "q.DisableGameBarIntegration" }),
        ("CPU y energía", "El procesador nunca descansa y el juego recibe más prioridad. Más consumo y temperatura.", "",
            new[] { "kiza.powerUltimate", "kiza.coreParking", "kiza.powerThrottling", "kiza.win32Priority", "kiza.timerResolution", "kiza.mmcssGames" }),
        ("Red y apps en segundo plano", "Ganancias pequeñas que pueden afectar notificaciones o el uso de CPU.", "",
            new[] { "kiza.networkThrottling", "kiza.bgApps" }),
        ("Seguridad vs. rendimiento", "Más FPS a cambio de menos protección. Solo si sabes lo que haces.", "",
            new[] { "kiza.memoryIntegrity", "kiza.vmp" }),
    };

    public AdvancedViewModel(IDialogService dialogs, TweaksViewModel tweaks)
    {
        _dialogs = dialogs;
        _tweaks = tweaks;
        RevertAllCommand = new AsyncCommand(RevertAllAsync);
        foreach (var t in tweaks.All)
            t.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(TweakItemViewModel.State)) return;
                foreach (var g in Groups) g.Refresh();
                OnPropertyChanged(nameof(ActiveCount));
                OnPropertyChanged(nameof(HasActive));
            };
    }

    public int ActiveCount => Groups.SelectMany(g => g.Items).Count(i => i.IsApplied);
    public bool HasActive => ActiveCount > 0;

    public async Task LoadAsync()
    {
        if (!_tweaks.Loaded) await _tweaks.RefreshAsync();
        Groups.Clear();
        var byId = _tweaks.All.ToDictionary(t => t.Def.Id);
        foreach (var (title, subtitle, glyph, ids) in Layout)
        {
            var g = new AdvancedGroupViewModel { Title = title, Subtitle = subtitle, Glyph = glyph };
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var item) || !item.IsCompatible) continue;
                var caution = item.Caution ?? item.Note;
                g.Items.Add(new FpsTweakViewModel(item, new AsyncCommand(() => ToggleAsync(item)),
                    string.IsNullOrWhiteSpace(caution) ? null : "Riesgo: " + caution));
            }
            if (g.Items.Count > 0) Groups.Add(g);
        }
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasActive));
    }

    private async Task ToggleAsync(TweakItemViewModel item)
    {
        if (item.IsApplied)
        {
            await _tweaks.RevertAsync(new[] { item.Def });
            return;
        }
        var caution = item.Caution ?? item.Note ?? "";
        if (await _dialogs.ChooseAsync("Activar ajuste avanzado",
                $"{item.Name}\n\n{item.Description}\n\nRiesgo: {caution}\n\n" +
                "Si después algo se cierra o falla, vuelve aquí y apágalo (o usa «Apagar todos los avanzados»).",
                "Activar", "Cancelar") != 0) return;
        await _tweaks.ApplyAsync(new[] { item.Def }, askConfirm: false);
    }

    private async Task RevertAllAsync()
    {
        var active = Groups.SelectMany(g => g.Items).Where(i => i.IsApplied).Select(i => i.Item.Def).ToList();
        if (active.Count == 0) return;
        if (!await _dialogs.ConfirmAsync("Apagar todos los avanzados",
                $"Se deshacen {active.Count} ajustes avanzados y todo vuelve a como estaba antes de activarlos.", "Apagar todos")) return;
        await _tweaks.RevertAsync(active);
    }
}
