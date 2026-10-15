using KizaTweaks.Core.Apps;
using KizaTweaks.Core.Benchmark;
using KizaTweaks.Core.Catalog;
using KizaTweaks.Core.Engine;
using KizaTweaks.Core.Hardware;
using KizaTweaks.Core.Maintenance;
using KizaTweaks.Core.Processes;
using KizaTweaks.Presentation;
using KizaTweaks.Presentation.Mvvm;
using KizaTweaks.Presentation.ViewModels;

namespace KizaTweaks.Tests;

/// <summary>Diálogos falsos: responden con índices predefinidos y registran lo que se mostró.</summary>
public sealed class FakeDialogs : IDialogService
{
    public Queue<int> Answers { get; } = new();
    public List<string> Shown { get; } = new();

    public Task<int> ChooseAsync(string title, string message, params string[] buttons)
    {
        Shown.Add($"{title}: {message}");
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : -1);
    }

    public Task InfoAsync(string title, string message)
    {
        Shown.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    public Queue<string?> Files { get; } = new();
    public Task<string?> PickFileAsync(string title, string filter) => Task.FromResult(Files.Count > 0 ? Files.Dequeue() : null);
}

public static class ViewModelTests
{
    public static (MainViewModel vm, FakeDialogs dialogs, InMemoryBackend backend) Create()
    {
        var b = new InMemoryBackend();
        var startup = new StartupService(b);
        var services = new AppServices
        {
            Catalog = CatalogLoader.LoadTweaks(),
            AppsCatalog = CatalogLoader.LoadApps(),
            Backend = b,
            Engine = new TweakEngine(b, new BackupStore(null)),
            Hardware = new HardwareService(b),
            Apps = new AppService(),
            Processes = new ProcessService(),
            Startup = startup,
            Snapshots = new SnapshotService(startup),
            PresentMon = new PresentMonService(),
            FpsResults = new FpsResultStore(),
            Maintenance = new MaintenanceService(),
            Build = 26100,
            IsAdmin = true,
            RestorePointCreated = false,
        };
        var dialogs = new FakeDialogs();
        return (new MainViewModel(services, dialogs), dialogs, b);
    }

    /// <summary>Flujo de la interfaz: elegir perfil Gamer, aplicar sin punto de restauración, verificar y deshacer todo.</summary>
    public static void GamerProfileFlow()
    {
        var (vm, dialogs, b) = Create();
        vm.Tweaks.RefreshAsync().GetAwaiter().GetResult();
        // Algunos ajustes (los que solo borran claves) ya figuran aplicados en un sistema vacío.
        var initial = vm.Tweaks.AppliedCount;

        vm.Tweaks.SelectProfileAsync(Profiles.Gamer).GetAwaiter().GetResult();
        var selected = vm.Tweaks.SelectedCount;
        if (selected < 15) throw new Exception($"perfil gamer con pocos ajustes: {selected}");
        if (vm.Tweaks.All.Any(t => t.IsSelected && t.Def.Risk == "risky"))
            throw new Exception("el perfil gamer no debe incluir ajustes de riesgo");

        dialogs.Answers.Enqueue(1); // «Aplicar sin punto»
        vm.Tweaks.ApplySelectedAsync().GetAwaiter().GetResult();
        if (vm.Tweaks.AppliedCount != initial + selected)
            throw new Exception($"se esperaban {initial + selected} aplicados y hay {vm.Tweaks.AppliedCount}. Diálogos: {string.Join(" || ", dialogs.Shown)}");
        if (vm.Tweaks.SelectedCount != 0) throw new Exception("la selección debería limpiarse");
        if (vm.IsBusy) throw new Exception("quedó en estado ocupado");

        vm.Backups.Refresh();
        if (vm.Backups.Items.Count != selected) throw new Exception($"respaldos: {vm.Backups.Items.Count}");

        dialogs.Answers.Enqueue(0); // «Deshacer todo»
        vm.Backups.UndoAllCommand.Execute(null);
        // AsyncCommand es async void: se espera a que termine
        for (int i = 0; i < 100 && vm.Tweaks.AppliedCount != initial; i++) Thread.Sleep(50);
        if (vm.Tweaks.AppliedCount != initial) throw new Exception($"quedaron {vm.Tweaks.AppliedCount - initial} aplicados tras deshacer todo");
        if (vm.Backups.Items.Count != 0) throw new Exception("quedaron respaldos");
    }

    public static void CancelDoesNothing()
    {
        var (vm, dialogs, b) = Create();
        vm.Tweaks.RefreshAsync().GetAwaiter().GetResult();
        var before = b.Dump();
        vm.Tweaks.SelectProfileAsync(Profiles.Privacy).GetAwaiter().GetResult();
        dialogs.Answers.Enqueue(2); // «Cancelar»
        vm.Tweaks.ApplySelectedAsync().GetAwaiter().GetResult();
        if (b.Dump() != before) throw new Exception("cancelar modificó el sistema");
    }

    /// <summary>Modo FPS en un escritorio: activar «Máximo rendimiento», verificar progreso y deshacer con el interruptor.</summary>
    public static void FpsModeFlow()
    {
        var (vm, dialogs, b) = Create();
        vm.Services.LastHardware = new HardwareReport { IsLaptop = false, Cpu = new CpuInfo { Name = "AMD Ryzen 5 7600", Cores = 6, Threads = 12 } };
        vm.Fps.LoadAsync().GetAwaiter().GetResult();
        if (vm.Fps.Groups.Count < 3) throw new Exception($"grupos FPS: {vm.Fps.Groups.Count}");
        if (vm.Fps.Groups.SelectMany(g => g.Items).Any(i => i.Item.Def.Impact is not ("fps" or "latency" or "stability" or "appearance")))
            throw new Exception("el Modo FPS debe mostrar solo ajustes de rendimiento");
        var perf = vm.Fps.Levels.First(l => l.Id == Profiles.Performance);
        if (perf.Total < 15 || perf.Pending == 0) throw new Exception($"nivel máximo rendimiento: {perf.Pending}/{perf.Total}");

        dialogs.Answers.Enqueue(1); // «Aplicar sin punto»
        perf.ActivateCommand.Execute(null);
        for (int i = 0; i < 100 && !perf.IsComplete; i++) Thread.Sleep(50);
        if (!perf.IsComplete) throw new Exception($"quedaron {perf.Pending} pendientes. Diálogos: {string.Join(" || ", dialogs.Shown)}");
        if (b.ActiveScheme != b.ResolvePowerScheme("ultimate", false)) throw new Exception("no se activó el plan de máximo rendimiento");
        if (vm.Fps.Percent <= 0) throw new Exception("progreso FPS en 0");

        var item = vm.Fps.Groups.SelectMany(g => g.Items).First(i => i.IsApplied && i.Item.Def.Id == "kiza.gameMode");
        item.ToggleCommand.Execute(null);
        for (int i = 0; i < 100 && (item.IsApplied || perf.IsComplete); i++) Thread.Sleep(50);
        if (item.IsApplied) throw new Exception("el interruptor no deshizo el ajuste");
        if (perf.IsComplete) throw new Exception("el nivel debería quedar incompleto tras apagar un ajuste");
    }

    /// <summary>Configurar un juego: prioridad alta + GPU dedicada, y quitarlo.</summary>
    public static void GameConfigFlow()
    {
        var (vm, dialogs, b) = Create();
        vm.Services.LastHardware = new HardwareReport();
        vm.Fps.LoadAsync().GetAwaiter().GetResult();
        dialogs.Files.Enqueue(@"D:\Juegos\Valorant\VALORANT-Win64-Shipping.exe");
        dialogs.Answers.Enqueue(0);
        vm.Fps.AddGameCommand.Execute(null);
        for (int i = 0; i < 100 && vm.Fps.Games.Count == 0; i++) Thread.Sleep(50);
        var g = vm.Fps.Games.SingleOrDefault() ?? throw new Exception("el juego no aparece");
        if (!g.Game.HighPriority || !g.Game.DedicatedGpu) throw new Exception("configuración incompleta: " + g.Detail);
        g.RemoveCommand.Execute(null);
        for (int i = 0; i < 100 && vm.Fps.Games.Count > 0; i++) Thread.Sleep(50);
        if (vm.Fps.Games.Count != 0) throw new Exception("no se quitó el juego");
        if (b.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\VALORANT-Win64-Shipping.exe"))
            throw new Exception("quedó la clave IFEO");
    }

    public static void NavigationAndFilters()
    {
        var (vm, _, _) = Create();
        if (vm.Nav.Count != 10) throw new Exception("páginas");
        vm.Navigate("about");
        if (vm.CurrentPage is not AboutViewModel) throw new Exception("navegación");
        var t = vm.Tweaks;
        var gaming = t.Categories.First(c => c.Id == "gaming");
        t.SelectedCategory = gaming;
        if (t.Visible.Count != gaming.Count || t.Visible.Any(x => x.Def.Category != "gaming")) throw new Exception("filtro de categoría");
        t.SelectedCategory = t.Categories[0];
        t.SearchText = "mouse";
        if (t.Visible.Count == 0 || t.Visible.Any(x => !x.Name.Contains("mouse", StringComparison.OrdinalIgnoreCase)
                                                  && !x.Description.Contains("mouse", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("búsqueda");
        vm.Toast("hola");
        if (!vm.HasToast) throw new Exception("aviso");
    }
}
