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

        vm.Tweaks.SelectProfile(Profiles.Gamer);
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
        vm.Tweaks.SelectProfile(Profiles.Privacy);
        dialogs.Answers.Enqueue(2); // «Cancelar»
        vm.Tweaks.ApplySelectedAsync().GetAwaiter().GetResult();
        if (b.Dump() != before) throw new Exception("cancelar modificó el sistema");
    }

    public static void NavigationAndFilters()
    {
        var (vm, _, _) = Create();
        if (vm.Nav.Count != 9) throw new Exception("páginas");
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
