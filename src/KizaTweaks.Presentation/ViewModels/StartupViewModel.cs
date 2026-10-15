using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using KizaTweaks.Core;
using KizaTweaks.Core.Platform;
using KizaTweaks.Core.Processes;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.Presentation.ViewModels;

[SupportedOSPlatform("windows")]
public sealed class StartupItemViewModel : ObservableObject
{
    private readonly StartupService _svc;
    private readonly IShellHost _shell;
    public StartupItem Item { get; }

    public StartupItemViewModel(StartupItem item, StartupService svc, IShellHost shell)
    {
        Item = item;
        _svc = svc;
        _shell = shell;
        _enabled = item.Enabled;
    }

    public string Name => Item.Name;
    public string Command => Item.Command;
    public string Location => Item.Location;

    private bool _enabled;
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            try
            {
                _svc.SetEnabled(Item, value);
                SetProperty(ref _enabled, value);
                _shell.Toast($"{Name}: {(value ? "arrancará con Windows" : "ya no arrancará con Windows")}.");
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo cambiar {Name}", ex);
                _shell.Toast($"No se pudo cambiar {Name}: {ex.Message}", true);
                OnPropertyChanged();
            }
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class StartupViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly IShellHost _shell;
    public ObservableCollection<StartupItemViewModel> Items { get; } = new();
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand OpenTaskManagerCommand { get; }
    public RelayCommand OpenStartupSettingsCommand { get; }

    public StartupViewModel(AppServices s, IShellHost shell)
    {
        _s = s;
        _shell = shell;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        OpenTaskManagerCommand = new RelayCommand(() => ProcessRunner.OpenShell("taskmgr.exe"));
        OpenStartupSettingsCommand = new RelayCommand(() => ProcessRunner.OpenShell("ms-settings:startupapps"));
    }

    private string _summary = "";
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public async Task RefreshAsync()
    {
        var list = await Task.Run(() => _s.Startup.List());
        Items.Clear();
        foreach (var i in list) Items.Add(new StartupItemViewModel(i, _s.Startup, _shell));
        Summary = $"{list.Count(i => i.Enabled)} de {list.Count} activas al iniciar Windows";
    }
}
