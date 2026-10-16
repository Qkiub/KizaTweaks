using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Versioning;
using KizaTweaks.Core.Platform;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

public sealed class NavItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public required object Page { get; init; }
    /// <summary>Se ejecuta la primera vez que se abre la página.</summary>
    public Func<Task>? OnFirstOpen { get; init; }
    internal bool Opened { get; set; }
}

public sealed class AboutViewModel
{
    public const string RepoUrl = "https://github.com/QkiubYT/KizaTweaks";

    public string Version { get; } = "v" + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");

    public IReadOnlyList<string> NotIncluded { get; } = new[]
    {
        "Desactivar Microsoft Defender",
        "Desactivar Windows Update",
        "Quitar Microsoft Edge (launchers y apps usan WebView2)",
        "Borrar el archivo de paginación",
        "Temporizadores con BCDEdit (HPET, useplatformclock)",
        "Desactivar servicios en masa",
        "Limpiadores de RAM y borrado de Prefetch",
        "Ajustes de red con netsh",
    };

    public RelayCommand OpenRepoCommand { get; } = new(() => ProcessRunner.OpenShell(RepoUrl));
    public RelayCommand OpenIssuesCommand { get; } = new(() => ProcessRunner.OpenShell(RepoUrl + "/issues"));
    public RelayCommand OpenDataCommand { get; } = new(() => ProcessRunner.OpenShell(Core.AppPaths.DataDir));
}

[SupportedOSPlatform("windows")]
public sealed class MainViewModel : ObservableObject, IShellHost
{
    public AppServices Services { get; }
    public ObservableCollection<NavItem> Nav { get; } = new();
    public DashboardViewModel Dashboard { get; }
    public TweaksViewModel Tweaks { get; }
    public AppsViewModel Apps { get; }
    public ProcessesViewModel Processes { get; }
    public StartupViewModel Startup { get; }
    public MaintenanceViewModel Maintenance { get; }
    public FpsViewModel Fps { get; }
    public BenchmarkViewModel Benchmark { get; }
    public BackupsViewModel Backups { get; }
    public AboutViewModel About { get; } = new();

    public RelayCommand RebootCommand { get; }
    public RelayCommand DismissToastCommand { get; }

    public MainViewModel(AppServices services, IDialogService dialogs)
    {
        Services = services;
        Tweaks = new TweaksViewModel(services, dialogs, this);
        Dashboard = new DashboardViewModel(services, dialogs, this, Tweaks);
        Fps = new FpsViewModel(services, dialogs, this, Tweaks);
        Apps = new AppsViewModel(services, dialogs, this);
        Processes = new ProcessesViewModel(services, dialogs, this);
        Startup = new StartupViewModel(services, this);
        Maintenance = new MaintenanceViewModel(services.Maintenance, dialogs, this);
        Benchmark = new BenchmarkViewModel(services, dialogs, this);
        Backups = new BackupsViewModel(services, dialogs, this, Tweaks);

        Nav.Add(new NavItem { Key = "home", Title = "Inicio", Glyph = "", Page = Dashboard, OnFirstOpen = Dashboard.LoadAsync });
        Nav.Add(new NavItem { Key = "fps", Title = "Modo FPS", Glyph = "\uEC4A", Page = Fps, OnFirstOpen = Fps.LoadAsync });
        Nav.Add(new NavItem { Key = "tweaks", Title = "Optimizar", Glyph = "", Page = Tweaks, OnFirstOpen = async () => { if (!Tweaks.Loaded) await Tweaks.RefreshAsync(); } });
        Nav.Add(new NavItem { Key = "apps", Title = "Apps", Glyph = "", Page = Apps, OnFirstOpen = Apps.ScanAsync });
        Nav.Add(new NavItem { Key = "processes", Title = "Procesos", Glyph = "", Page = Processes, OnFirstOpen = Processes.RefreshAsync });
        Nav.Add(new NavItem { Key = "startup", Title = "Arranque", Glyph = "", Page = Startup, OnFirstOpen = Startup.RefreshAsync });
        Nav.Add(new NavItem { Key = "maintenance", Title = "Mantenimiento", Glyph = "\uE90F", Page = Maintenance });
        Nav.Add(new NavItem { Key = "benchmark", Title = "Benchmark", Glyph = "", Page = Benchmark, OnFirstOpen = Benchmark.LoadAsync });
        Nav.Add(new NavItem { Key = "backups", Title = "Respaldos", Glyph = "", Page = Backups, OnFirstOpen = () => { Backups.Refresh(); return Task.CompletedTask; } });
        Nav.Add(new NavItem { Key = "about", Title = "Acerca de", Glyph = "", Page = About });

        RebootCommand = new RelayCommand(SystemActions.Reboot);
        DismissToastCommand = new RelayCommand(() => ToastText = null);
        _selected = Nav[0];
    }

    public bool IsAdmin => Services.IsAdmin;
    public string OsInfo => SystemActions.OsDisplayVersion();

    private NavItem _selected;
    public NavItem SelectedNav
    {
        get => _selected;
        set
        {
            if (value is null || !SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(CurrentPage));
            if (value.Key == "backups") Backups.Refresh();
            _ = OpenAsync(value);
        }
    }

    public object CurrentPage => _selected.Page;

    /// <summary>Llamar una vez cuando la ventana está lista.</summary>
    public Task InitializeAsync() => OpenAsync(_selected);

    private async Task OpenAsync(NavItem item)
    {
        if (item.Opened || item.OnFirstOpen is null) return;
        item.Opened = true;
        try { await item.OnFirstOpen(); }
        catch (Exception ex)
        {
            Core.Log.Error($"Error abriendo {item.Title}", ex);
            Toast($"Error cargando {item.Title}: {ex.Message}", true);
            item.Opened = false;
        }
    }

    public void Navigate(string pageKey)
    {
        var item = Nav.FirstOrDefault(n => n.Key == pageKey);
        if (item is not null) SelectedNav = item;
    }

    // ---- Ocupado ----
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    private string _busyText = "";
    public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }

    public async Task RunBusyAsync(string text, Func<IProgress<string>, Task> work)
    {
        var nested = IsBusy;
        BusyText = text;
        IsBusy = true;
        try
        {
            await work(new Progress<string>(t => BusyText = t));
        }
        finally
        {
            if (!nested) IsBusy = false;
        }
    }

    // ---- Avisos ----
    private string? _toast;
    public string? ToastText { get => _toast; private set { if (SetProperty(ref _toast, value)) OnPropertyChanged(nameof(HasToast)); } }
    public bool HasToast => !string.IsNullOrEmpty(_toast);
    private bool _toastError;
    public bool ToastIsError { get => _toastError; private set => SetProperty(ref _toastError, value); }
    private int _toastId;

    public void Toast(string message, bool error = false)
    {
        ToastIsError = error;
        ToastText = message;
        var id = ++_toastId;
        _ = HideToastLater(id, error ? 9000 : 5000);
    }

    private async Task HideToastLater(int id, int ms)
    {
        await Task.Delay(ms);
        if (id == _toastId) ToastText = null;
    }

    // ---- Reinicio ----
    private bool _reboot;
    public bool RebootRequired { get => _reboot; private set => SetProperty(ref _reboot, value); }
    public void MarkRebootRequired() => RebootRequired = true;
}
