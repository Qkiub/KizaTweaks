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
    public static int RestorePointsCreated;

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
            CreateRestorePoint = _ => { RestorePointsCreated++; return (true, "ok"); },
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

        dialogs.Answers.Enqueue(0); // «Aplicar» (crea el punto de restauración solo)
        vm.Tweaks.ApplySelectedAsync().GetAwaiter().GetResult();
        if (vm.Tweaks.AppliedCount != initial + selected)
            throw new Exception($"se esperaban {initial + selected} aplicados y hay {vm.Tweaks.AppliedCount}. Diálogos: {string.Join(" || ", dialogs.Shown)}");
        if (vm.Tweaks.SelectedCount != 0) throw new Exception("la selección debería limpiarse");
        if (!vm.Services.RestorePointCreated) throw new Exception("debe crear el punto de restauración antes de aplicar");
        if (vm.IsBusy) throw new Exception("quedó en estado ocupado");

        vm.Backups.Refresh();
        if (vm.Backups.Items.Count != selected) throw new Exception($"respaldos: {vm.Backups.Items.Count}");

        dialogs.Answers.Enqueue(0); // «Deshacer todo»
        vm.Backups.UndoAllCommand.Execute(null);
        // AsyncCommand es async void: se espera a que termine
        for (int i = 0; i < 400 && (vm.Tweaks.AppliedCount != initial || vm.Backups.Items.Count != 0); i++) Thread.Sleep(50);
        if (vm.Tweaks.AppliedCount != initial) throw new Exception($"quedaron {vm.Tweaks.AppliedCount - initial} aplicados tras deshacer todo");
        if (vm.Backups.Items.Count != 0) throw new Exception("quedaron respaldos");
    }

    public static void CancelDoesNothing()
    {
        var (vm, dialogs, b) = Create();
        vm.Tweaks.RefreshAsync().GetAwaiter().GetResult();
        var before = b.Dump();
        vm.Tweaks.SelectProfileAsync(Profiles.Privacy).GetAwaiter().GetResult();
        dialogs.Answers.Enqueue(1); // «Cancelar»
        vm.Tweaks.ApplySelectedAsync().GetAwaiter().GetResult();
        if (b.Dump() != before) throw new Exception("cancelar modificó el sistema");
    }

    /// <summary>Modo FPS en un escritorio: activar «Máximo rendimiento», verificar progreso y deshacer con el interruptor.</summary>
    public static void FpsModeFlow()
    {
        var (vm, dialogs, b) = Create();
        vm.Services.LastHardware = new HardwareReport { IsLaptop = false, Cpu = new CpuInfo { Name = "AMD Ryzen 5 7600", Cores = 6, Threads = 12 } };
        vm.Fps.LoadAsync().GetAwaiter().GetResult();
        if (vm.Fps.Groups.Count < 2) throw new Exception($"grupos FPS: {vm.Fps.Groups.Count}");
        if (vm.Fps.Groups.SelectMany(g => g.Items).Any(i => i.Item.Def.Impact is not ("fps" or "latency" or "stability" or "appearance")))
            throw new Exception("el Modo FPS debe mostrar solo ajustes de rendimiento");
        var perf = vm.Fps.Levels.First(l => l.Id == Profiles.Performance);
        if (perf.Total < 12 || perf.Pending == 0) throw new Exception($"nivel máximo rendimiento: {perf.Pending}/{perf.Total}");

        dialogs.Answers.Enqueue(0); // «Aplicar» (crea el punto de restauración solo)
        perf.ActivateCommand.Execute(null);
        for (int i = 0; i < 400 && !perf.IsComplete; i++) Thread.Sleep(50);
        if (!perf.IsComplete) throw new Exception($"quedaron {perf.Pending} pendientes. Diálogos: {string.Join(" || ", dialogs.Shown)}");
        if (b.ActiveScheme != b.ResolvePowerScheme("ultimate", false)) throw new Exception("no se activó el plan de máximo rendimiento");
        if (vm.Fps.Percent <= 0) throw new Exception("progreso FPS en 0");

        var item = vm.Fps.Groups.SelectMany(g => g.Items).First(i => i.IsApplied && i.Item.Def.Id == "kiza.gameMode");
        item.ToggleCommand.Execute(null);
        for (int i = 0; i < 400 && (item.IsApplied || perf.IsComplete); i++) Thread.Sleep(50);
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
        for (int i = 0; i < 400 && vm.Fps.Games.Count == 0; i++) Thread.Sleep(50);
        var g = vm.Fps.Games.SingleOrDefault() ?? throw new Exception("el juego no aparece");
        if (g.Game.HighPriority || !g.Game.DedicatedGpu) throw new Exception("solo debe forzar la GPU, no la prioridad: " + g.Detail);
        g.RemoveCommand.Execute(null);
        for (int i = 0; i < 400 && vm.Fps.Games.Count > 0; i++) Thread.Sleep(50);
        if (vm.Fps.Games.Count != 0) throw new Exception("no se quitó el juego");
        if (b.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\VALORANT-Win64-Shipping.exe"))
            throw new Exception("quedó la clave IFEO");
    }

    /// <summary>Optimizar: el plan seguro no trae nada avanzado y cada sección se activa con punto de restauración.</summary>
    public static void OptimizeFlow()
    {
        var (vm, dialogs, b) = Create();
        vm.Optimize.LoadAsync().GetAwaiter().GetResult();
        var c = vm.Services.Catalog;
        var ids = SafePlan.AllTweakIds(c);
        if (ids.Count < 25) throw new Exception($"plan seguro corto: {ids.Count}");
        foreach (var id in ids)
        {
            var t = c.Find(id) ?? throw new Exception($"{id} no existe");
            if (t.Advanced || t.Risk != "safe") throw new Exception($"{id} no es seguro");
        }
        if (SafePlan.Sections(c, new KizaTweaks.Core.Hardware.HardwareReport { IsLaptop = true }).SelectMany(s => s.TweakIds).Any(SafePlan.DesktopOnly.Contains))
            throw new Exception("portátil con plan de escritorio");
        if (SafePlan.Apps(vm.Services.AppsCatalog).Count() < 25) throw new Exception("lista de apps basura incompleta");
        if (SafePlan.Apps(vm.Services.AppsCatalog).Any(a => a.Recommendation != "safe")) throw new Exception("app no segura en la lista");
        if (vm.Optimize.Clean.Items.Count < 10 || vm.Optimize.Clean.Items.Any(i => i.Junk!.Id == "windows-old" && i.IsChecked))
            throw new Exception("limpieza");
        if (vm.Optimize.Sections.Count != 4) throw new Exception($"secciones: {vm.Optimize.Sections.Count}");
        var input = vm.Optimize.Sections.First(s => s.Id == "input");
        dialogs.Answers.Enqueue(0);
        input.ActivateCommand.Execute(null);
        for (int i = 0; i < 400 && !input.IsComplete; i++) Thread.Sleep(50);
        if (!input.IsComplete) throw new Exception("no quedó aplicado: " + input.ActiveText + " | " + string.Join(" || ", dialogs.Shown));
        if (!vm.Services.RestorePointCreated) throw new Exception("sin punto de restauración");
        if (vm.Optimize.Percent <= 0 || vm.Optimize.Percent >= 100) throw new Exception($"puntaje {vm.Optimize.Percent}");

        // Avanzado: solo ajustes avanzados (o el plan máximo), y activar uno pide confirmación
        vm.Advanced.LoadAsync().GetAwaiter().GetResult();
        var adv = vm.Advanced.Groups.SelectMany(g => g.Items).ToList();
        if (adv.Count < 12 || adv.Any(a => !a.Item.IsAdvanced && a.Item.Def.Id != "kiza.powerUltimate") || adv.Any(a => !a.HasWarning))
            throw new Exception("avanzado");
        var hags = adv.First(a => a.Item.Def.Id == "kiza.hags");
        dialogs.Answers.Enqueue(1); // cancelar
        hags.ToggleCommand.Execute(null);
        Thread.Sleep(300);
        if (hags.IsApplied) throw new Exception("se activó sin confirmar");
        dialogs.Answers.Enqueue(0);
        hags.ToggleCommand.Execute(null);
        for (int i = 0; i < 400 && !hags.IsApplied; i++) Thread.Sleep(50);
        if (!hags.IsApplied || !vm.Advanced.HasActive) throw new Exception("no se activó HAGS");
        if (vm.Tweaks.Visible.Any(t => t.IsAdvanced)) throw new Exception("avanzados en «Todos los ajustes»");
    }

    public static void NavigationAndFilters()
    {
        var (vm, _, _) = Create();
        if (vm.Nav.Count != 11 || vm.Nav[0].Key != "optimize" || vm.Nav.Any(n => n.Group.Length == 0)) throw new Exception("páginas");
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
