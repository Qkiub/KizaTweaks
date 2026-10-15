using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Input;
using KizaTweaks.Core;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Hardware;
using KizaTweaks.Core.Platform;
using KizaTweaks.Core.Processes;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

/// <summary>Un ajuste dentro del Modo FPS, con interruptor de activar/desactivar.</summary>
public sealed class FpsTweakViewModel : ObservableObject
{
    public TweakItemViewModel Item { get; }
    public FpsTweakViewModel(TweakItemViewModel item, ICommand toggle, string? warning)
    {
        Item = item;
        ToggleCommand = toggle;
        Warning = warning;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TweakItemViewModel.State))
            {
                OnPropertyChanged(nameof(IsApplied));
                OnPropertyChanged(nameof(StatusText));
            }
        };
    }

    public string Name => Item.Name;
    public string Description => Item.Description;
    public string? Note => Item.Note;
    public bool HasNote => Item.HasNote;
    public string ImpactLabel => Item.ImpactLabel;
    public string ImpactColor => Item.ImpactColor;
    public string RiskLabel => Item.RiskLabel;
    public string RiskColor => Item.RiskColor;
    public bool RequiresReboot => Item.RequiresReboot;
    public bool IsApplied => Item.IsApplied;
    public string StatusText => Item.State switch
    {
        TweakState.Applied => "Activo",
        TweakState.Partial => "Parcial",
        _ => "Apagado",
    };
    public string? Warning { get; }
    public bool HasWarning => Warning is not null;
    public ICommand ToggleCommand { get; }
}

public sealed class FpsGroupViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Glyph { get; init; }
    public required string Color { get; init; }
    public List<FpsTweakViewModel> Items { get; } = new();
    public string ActiveText => $"{Items.Count(i => i.IsApplied)} de {Items.Count} activos";
    public void Refresh() => OnPropertyChanged(nameof(ActiveText));
}

public sealed class FpsLevelViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required ICommand ActivateCommand { get; init; }

    private int _pending, _total;
    private string _adjusted = "";
    public int Pending { get => _pending; private set => SetProperty(ref _pending, value); }
    public int Total { get => _total; private set => SetProperty(ref _total, value); }
    public string Adjusted { get => _adjusted; private set { SetProperty(ref _adjusted, value); OnPropertyChanged(nameof(HasAdjusted)); } }
    public bool HasAdjusted => Adjusted.Length > 0;
    public bool IsComplete => Total > 0 && Pending == 0;
    public string ProgressText => IsComplete ? $"Los {Total} ajustes están activos" : $"{Total - Pending} de {Total} activos, faltan {Pending}";
    public string ButtonText => IsComplete ? "Activado" : "Activar";

    public void Update(int pending, int total, IReadOnlyList<string> adjusted)
    {
        Pending = pending;
        Total = total;
        Adjusted = string.Join(" ", adjusted);
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ButtonText));
    }
}

public sealed class FpsCheckViewModel
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    /// <summary>ok | warn | info</summary>
    public required string Status { get; init; }
    public string Glyph => Status switch { "ok" => "", "warn" => "", _ => "" };
    public string Color => Status switch { "ok" => Labels.Green, "warn" => Labels.Amber, _ => Labels.Cyan };
    public string? ActionText { get; init; }
    public ICommand? ActionCommand { get; init; }
    public bool HasAction => ActionCommand is not null;
}

public sealed class GameItemViewModel
{
    public required GameEntry Game { get; init; }
    public string ExeName => Game.ExeName;
    public string Detail => string.Join(" · ", new[]
    {
        Game.HighPriority ? "Prioridad alta" : null,
        Game.DedicatedGpu ? "GPU de alto rendimiento" : null,
    }.Where(x => x is not null));
    public required ICommand RemoveCommand { get; init; }
}

/// <summary>
/// Modo FPS: solo lo que sube FPS o baja la latencia, agrupado por nivel, con la revisión del hardware
/// y herramientas para la partida (sesión de juego, configuración por juego).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FpsViewModel : ObservableObject
{
    private static readonly HashSet<string> FpsImpacts = new() { "fps", "latency" };

    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly IShellHost _shell;
    private readonly TweaksViewModel _tweaks;
    private HardwareReport? _hw;
    private bool _built;

    public ObservableCollection<FpsLevelViewModel> Levels { get; } = new();
    public ObservableCollection<FpsGroupViewModel> Groups { get; } = new();
    public ObservableCollection<FpsCheckViewModel> Checks { get; } = new();
    public ObservableCollection<GameItemViewModel> Games { get; } = new();

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand GameSessionCommand { get; }
    public AsyncCommand AddGameCommand { get; }
    public RelayCommand BenchmarkCommand { get; }

    public FpsViewModel(AppServices s, IDialogService dialogs, IShellHost shell, TweaksViewModel tweaks)
    {
        _s = s;
        _dialogs = dialogs;
        _shell = shell;
        _tweaks = tweaks;
        RefreshCommand = new AsyncCommand(LoadAsync);
        GameSessionCommand = new AsyncCommand(GameSessionAsync);
        AddGameCommand = new AsyncCommand(AddGameAsync);
        BenchmarkCommand = new RelayCommand(() => _shell.Navigate("benchmark"));
        // Se mantiene al día aunque los ajustes se cambien desde otra página.
        foreach (var t in tweaks.All)
            t.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TweakItemViewModel.State) && _built) UpdateProgress(); };

        Levels.Add(new FpsLevelViewModel
        {
            Id = Profiles.Performance, Name = "Máximo rendimiento", Glyph = "",
            Description = "Lo esencial para jugar + energía al máximo, USB y PCIe sin ahorro y prioridad multimedia. No quita nada básico de Windows.",
            ActivateCommand = new AsyncCommand(() => ActivateLevelAsync(Profiles.Performance)),
        });
        Levels.Add(new FpsLevelViewModel
        {
            Id = Profiles.Competitive, Name = "Competitivo", Glyph = "",
            Description = "Todo lo anterior + ajustes experimentales de latencia, temporizador y CPU, y menos efectos visuales. Para exprimir cada frame.",
            ActivateCommand = new AsyncCommand(() => ActivateLevelAsync(Profiles.Competitive)),
        });
    }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    private int _percent;
    public int Percent { get => _percent; private set => SetProperty(ref _percent, value); }
    private string _scoreText = "";
    public string ScoreText { get => _scoreText; private set => SetProperty(ref _scoreText, value); }
    private string _scoreLabel = "";
    public string ScoreLabel { get => _scoreLabel; private set => SetProperty(ref _scoreLabel, value); }
    public string ScoreColor => Percent >= 85 ? Labels.Green : Percent >= 50 ? Labels.Cyan : Labels.Amber;

    private string _hardwareText = "";
    public string HardwareText { get => _hardwareText; private set => SetProperty(ref _hardwareText, value); }

    public bool HasGames => Games.Count > 0;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            try { _hw = await _s.GetHardwareAsync(); }
            catch (Exception ex) { Log.Warn("Modo FPS sin detección de hardware: " + ex.Message); }
            if (!_tweaks.Loaded) await _tweaks.RefreshAsync();
            BuildGroups();
            BuildChecks();
            RefreshGames();
            _built = true;
            UpdateProgress();
        }
        finally { IsLoading = false; }
    }

    // ------------------------------------------------------------------
    // Ajustes
    // ------------------------------------------------------------------

    private void BuildGroups()
    {
        var byId = _tweaks.All.Where(t => t.IsCompatible).ToDictionary(t => t.Def.Id);
        var pro = ProfileResolver.Resolve(Profiles.Competitive, _s.Catalog, _hw);
        var proAnyHw = ProfileResolver.Resolve(Profiles.Competitive, _s.Catalog, null).Ids;
        var gamerFps = ProfileResolver.Resolve(Profiles.Gamer, _s.Catalog, null).Ids
            .Where(id => byId.TryGetValue(id, out var t) && FpsImpacts.Contains(t.Def.Impact)).ToHashSet();

        string? WarningFor(string id)
        {
            if (_hw is null || !proAnyHw.Contains(id) || pro.Ids.Contains(id)) return null;
            if (_hw.IsLaptop && ProfileResolver.NotOnLaptop.Contains(id)) return "No recomendado en portátil: gasta más batería y calienta.";
            if (id == "kiza.coreParking") return "No recomendado en tu CPU: el estacionamiento de núcleos ayuda a tu Ryzen X3D.";
            return "No recomendado en tu equipo.";
        }

        FpsGroupViewModel Group(string title, string subtitle, string glyph, string color, IEnumerable<string> ids)
        {
            var g = new FpsGroupViewModel { Title = title, Subtitle = subtitle, Glyph = glyph, Color = color };
            foreach (var id in ids.Where(byId.ContainsKey))
            {
                var item = byId[id];
                g.Items.Add(new FpsTweakViewModel(item, new AsyncCommand(() => ToggleAsync(item)), WarningFor(id)));
            }
            return g;
        }

        var catalogOrder = _s.Catalog.Tweaks.Select(t => t.Id).ToList();
        IEnumerable<string> Ordered(IEnumerable<string> ids) => ids.OrderBy(catalogOrder.IndexOf);

        Groups.Clear();
        Groups.Add(Group("Esenciales", "Lo que todo PC gamer debería tener activo. Riesgo bajo.", "", Labels.Green,
            Ordered(gamerFps)));
        Groups.Add(Group("Máximo rendimiento", "Energía y prioridades al máximo, sin tocar funciones básicas de Windows.", "", Labels.Cyan,
            Ordered(_s.Catalog.Tweaks.Where(t => t.Profiles.Contains(Profiles.Performance)).Select(t => t.Id).Except(gamerFps))));
        Groups.Add(Group("Competitivo", "Experimentales: populares en la escena competitiva, el resultado varía por equipo. Mide con Benchmark.", "", Labels.Violet,
            Ordered(_s.Catalog.Tweaks.Where(t => t.Profiles.Contains(Profiles.Competitive) && !t.Profiles.Contains(Profiles.Performance)).Select(t => t.Id))));
        Groups.Add(Group("Seguridad vs. FPS", "Pueden dar FPS en algunos equipos a cambio de protección. Solo si mides una mejora clara.", "", Labels.Red,
            Ordered(_s.Catalog.Tweaks.Where(t => t.Category == "security").Select(t => t.Id))));
        foreach (var g in Groups.Where(g => g.Items.Count == 0).ToList()) Groups.Remove(g);
    }

    private async Task ToggleAsync(TweakItemViewModel item)
    {
        if (item.IsApplied) await _tweaks.RevertAsync(new[] { item.Def });
        else await _tweaks.ApplyAsync(new[] { item.Def });
        UpdateProgress();
    }

    private async Task ActivateLevelAsync(string profileId)
    {
        var sel = ProfileResolver.Resolve(profileId, _s.Catalog, _hw);
        var targets = _tweaks.All.Where(t => t.IsCompatible && sel.Ids.Contains(t.Def.Id) && !t.IsApplied).Select(t => t.Def).ToList();
        if (targets.Count == 0)
        {
            _shell.Toast("Este nivel ya está completo.");
            return;
        }
        await _tweaks.ApplyAsync(targets);
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var compatible = _tweaks.All.Where(t => t.IsCompatible).ToDictionary(t => t.Def.Id);
        foreach (var level in Levels)
        {
            var sel = ProfileResolver.Resolve(level.Id, _s.Catalog, _hw);
            var items = sel.Ids.Where(compatible.ContainsKey).Select(id => compatible[id]).ToList();
            level.Update(items.Count(i => !i.IsApplied), items.Count, sel.AdjustedForHardware);
        }
        var target = ProfileResolver.Resolve(Profiles.Competitive, _s.Catalog, _hw).Ids
            .Where(compatible.ContainsKey).Select(id => compatible[id]).ToList();
        var applied = target.Count(t => t.IsApplied);
        Percent = target.Count == 0 ? 0 : (int)Math.Round(100.0 * applied / target.Count);
        ScoreText = $"{applied} de {target.Count} ajustes de rendimiento activos";
        ScoreLabel = Percent >= 85 ? "Listo para competir" : Percent >= 50 ? "Buen camino" : Percent > 0 ? "Hay FPS sobre la mesa" : "Sin optimizar";
        OnPropertyChanged(nameof(ScoreColor));
        foreach (var g in Groups) g.Refresh();
    }

    // ------------------------------------------------------------------
    // Revisión del hardware
    // ------------------------------------------------------------------

    private void BuildChecks()
    {
        Checks.Clear();
        if (_hw is not { } r)
        {
            HardwareText = "No se pudo detectar el hardware.";
            return;
        }
        HardwareText = $"{(r.IsLaptop ? "Portátil" : "Escritorio")} con {r.Cpu.Name} y {r.MainGpu?.Name ?? "GPU sin identificar"}";
        ICommand Open(string uri) => new RelayCommand(() => ProcessRunner.OpenShell(uri));

        foreach (var d in r.Displays.Take(2))
        {
            if (d.MaxHz > d.CurrentHz + 1 && d.CurrentHz > 0)
                Checks.Add(new FpsCheckViewModel
                {
                    Status = "warn", Title = $"Monitor a {d.CurrentHz} Hz, soporta {d.MaxHz} Hz",
                    Detail = $"{d.MonitorName}: súbelo a {d.MaxHz} Hz. Es lo que más se nota al jugar.",
                    ActionText = "Abrir pantalla", ActionCommand = Open("ms-settings:display-advanced"),
                });
            else if (d.CurrentHz > 0)
                Checks.Add(new FpsCheckViewModel { Status = "ok", Title = $"Monitor a su máximo: {d.CurrentHz} Hz", Detail = $"{d.MonitorName} · {d.Width}×{d.Height}" });
        }

        var ram = r.Ram.Where(m => m.ConfiguredMhz > 0).ToList();
        if (ram.Count > 0)
        {
            var conf = ram.Min(m => m.ConfiguredMhz);
            var rated = ram.Max(m => m.RatedMhz);
            Checks.Add(rated > conf + 100
                ? new FpsCheckViewModel
                {
                    Status = "warn", Title = $"RAM a {conf} MHz, soporta {rated} MHz",
                    Detail = "Activa XMP (Intel) o EXPO (AMD) en la BIOS. Son FPS gratis, sobre todo en juegos que exigen CPU.",
                }
                : new FpsCheckViewModel
                {
                    Status = ram.Count == 1 && !r.IsLaptop ? "info" : "ok",
                    Title = $"RAM a {conf} MHz · {Labels.Bytes(r.RamTotalBytes)}",
                    Detail = ram.Count == 1 && !r.IsLaptop
                        ? "Un solo módulo: con dos iguales (dual channel) suelen subir los FPS."
                        : $"{ram.Count} módulos {ram[0].Type} funcionando a su velocidad.",
                });
        }

        if (r.MainGpu is { } g && !g.IsVirtualOrBasic)
        {
            var old = g.DriverDate is { } dd && (DateTime.Now - dd).TotalDays > 240;
            Checks.Add(new FpsCheckViewModel
            {
                Status = old ? "warn" : "ok",
                Title = old ? $"Driver de GPU de {g.DriverDate:MMM yyyy}" : "Driver de GPU reciente",
                Detail = old ? $"{g.Name}: instala el último driver de NVIDIA, AMD o Intel." : $"{g.Name} · {g.DriverVersion}",
            });
        }

        if (r.VbsRunning == true)
            Checks.Add(new FpsCheckViewModel
            {
                Status = "info", Title = "Seguridad basada en virtualización activa",
                Detail = r.HvciRunning == true
                    ? "Con Integridad de memoria (HVCI) encendida. Puede costar FPS en algunos equipos: revisa «Seguridad vs. FPS» y mide antes de tocarla."
                    : "Puede costar algo de rendimiento en algunos equipos. Mide antes de cambiarla.",
            });

        if (!string.IsNullOrEmpty(r.PowerSchemeName))
            Checks.Add(new FpsCheckViewModel
            {
                Status = "info", Title = $"Plan de energía: {r.PowerSchemeName}",
                Detail = r.IsLaptop ? "En portátil, juega conectado a la corriente y con el modo de energía en «Mejor rendimiento»."
                                    : "El nivel «Máximo rendimiento» lo cambia por el plan de máximo rendimiento.",
                ActionText = r.IsLaptop ? "Modo de energía" : null,
                ActionCommand = r.IsLaptop ? Open("ms-settings:powersleep") : null,
            });

        if (r.SystemDisk is { } sd && sd.Kind.Contains("HDD", StringComparison.OrdinalIgnoreCase))
            Checks.Add(new FpsCheckViewModel
            {
                Status = "warn", Title = "Windows está en un disco mecánico",
                Detail = "Instala Windows y tus juegos en un SSD: cargas y tirones mejoran muchísimo.",
            });
    }

    // ------------------------------------------------------------------
    // Herramientas de partida
    // ------------------------------------------------------------------

    private async Task GameSessionAsync()
    {
        var running = ProcessService.BackgroundCandidates
            .Where(c => c.DefaultSelected && ProcessService.CountRunning(c.ExeNames) > 0).ToList();
        if (running.Count == 0)
        {
            _shell.Toast("No hay nada pesado de fondo para cerrar. Listo para jugar.");
            return;
        }
        var list = string.Join("\n", running.Select(c => $"• {c.FriendlyName}"));
        if (!await _dialogs.ConfirmAsync("Sesión de juego",
                $"Se cerrará lo que corre de fondo y consume recursos:\n\n{list}\n\nGuarda lo que tengas abierto en esos programas.",
                "Cerrar y jugar")) return;
        var n = await Task.Run(() => _s.Processes.KillByNames(running.SelectMany(c => c.ExeNames)));
        _shell.Toast($"Sesión de juego lista: {n} procesos cerrados.");
    }

    private async Task AddGameAsync()
    {
        var path = await _dialogs.PickFileAsync("Elige el .exe de tu juego", "Juegos (*.exe)|*.exe");
        if (path is null) return;
        var exe = GameService.ExeName(path);
        if (!await _dialogs.ConfirmAsync("Configurar juego",
                $"Cada vez que abras {exe}, Windows lo ejecutará con:\n\n• Prioridad de CPU Alta\n• La GPU de alto rendimiento (en portátiles con dos GPU evita que use la integrada)\n\n" +
                "Elige el .exe del juego, no el del launcher. Algunos anti-cheat ignoran la prioridad; no pasa nada.",
                "Aplicar")) return;
        try
        {
            await Task.Run(() => GameService.Add(_s.Backend, path));
            _shell.Toast($"{exe} configurado.");
        }
        catch (Exception ex) { _shell.Toast(ex.Message, true); }
        RefreshGames();
    }

    private void RefreshGames()
    {
        Games.Clear();
        foreach (var g in GameService.List(_s.Backend))
        {
            var game = g;
            Games.Add(new GameItemViewModel
            {
                Game = game,
                RemoveCommand = new AsyncCommand(async () =>
                {
                    await Task.Run(() => GameService.Remove(_s.Backend, game));
                    _shell.Toast($"{game.ExeName} vuelve a la configuración normal.");
                    RefreshGames();
                }),
            });
        }
        OnPropertyChanged(nameof(HasGames));
    }
}
