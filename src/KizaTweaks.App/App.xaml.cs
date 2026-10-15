using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using KizaTweaks.App.Dialogs;
using KizaTweaks.Core;
using KizaTweaks.Presentation;
using KizaTweaks.Presentation.Mvvm;
using KizaTweaks.Presentation.ViewModels;

namespace KizaTweaks.App;

public partial class App : Application
{
    private MainViewModel? _vm;
    private readonly BindingErrorListener _bindingErrors = new();
    private bool _smoke;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _smoke = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Tarea sin observar", args.Exception);
            args.SetObserved();
        };

        if (_smoke)
        {
            // Modo de prueba automática (CI): registra errores de enlace de datos de WPF.
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(_bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        }

        // Si se abrió sin permisos de administrador, se vuelve a abrir pidiéndolos (UAC).
        if (!_smoke && !Core.Platform.SystemActions.IsAdministrator() && Environment.ProcessPath is { } self)
        {
            try
            {
                Process.Start(new ProcessStartInfo(self)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(" ", e.Args.Select(a => $"\"{a}\"")),
                });
                Shutdown(0);
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // El usuario rechazó el aviso de UAC: se continúa con permisos limitados y se avisa.
            }
        }

        var dialogs = new WpfDialogService { Unattended = _smoke };
        AppServices services;
        try
        {
            services = AppServices.CreateForWindows();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo iniciar", ex);
            if (!_smoke)
                MessageBox.Show($"KizaTweaks no pudo iniciar:\n\n{ex.Message}", "KizaTweaks", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var vm = new MainViewModel(services, dialogs);
        _vm = vm;
        AsyncCommand.ErrorHandler = ex => vm.Toast($"Error: {ex.Message}", error: true);

        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();

        if (_smoke)
        {
            _ = RunSmokeTestAsync(vm);
            return;
        }

        if (!services.IsAdmin)
        {
            _ = dialogs.InfoAsync("Se necesitan permisos de administrador",
                "KizaTweaks se abrió sin permisos de administrador, así que no podrá aplicar la mayoría de los ajustes.\n\n" +
                "Ciérralo y ábrelo con clic derecho > «Ejecutar como administrador».");
        }
    }

    /// <summary>Abre cada página, espera a que cargue y termina con código 0 si no hubo errores.</summary>
    private async Task RunSmokeTestAsync(MainViewModel vm)
    {
        var report = new StringBuilder();
        var failures = 0;
        try
        {
            await Task.Delay(1500);
            foreach (var item in vm.Nav)
            {
                vm.SelectedNav = item;
                // Espera a que la página cargue sus datos (algunas consultan WMI o PowerShell).
                for (int i = 0; i < 60 && vm.IsBusy; i++) await Task.Delay(500);
                await Task.Delay(3000);
                for (int i = 0; i < 120 && vm.IsBusy; i++) await Task.Delay(500);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                report.AppendLine($"OK página: {item.Title}");
            }
            report.AppendLine($"Tweaks cargados: {vm.Tweaks.All.Count}, aplicados: {vm.Tweaks.AppliedCount}");
            report.AppendLine($"Recomendaciones: {vm.Dashboard.Recommendations.Count}, puntaje {vm.Dashboard.Score}");
            report.AppendLine($"CPU: {vm.Dashboard.CpuText} | GPU: {vm.Dashboard.GpuText} | RAM: {vm.Dashboard.RamText}");
        }
        catch (Exception ex)
        {
            failures++;
            report.AppendLine($"EXCEPCIÓN: {ex}");
        }

        foreach (var err in _bindingErrors.Errors)
        {
            failures++;
            report.AppendLine($"ERROR DE BINDING: {err}");
        }
        foreach (var err in _unhandled)
        {
            failures++;
            report.AppendLine($"ERROR NO CONTROLADO: {err}");
        }
        report.AppendLine(failures == 0 ? "RESULTADO: OK" : $"RESULTADO: {failures} FALLOS");

        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            File.WriteAllText(Path.Combine(AppPaths.LogsDir, "smoke-test.txt"), report.ToString());
        }
        catch { }
        Shutdown(failures == 0 ? 0 : 1);
    }

    private readonly List<string> _unhandled = new();

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Error no controlado", e.Exception);
        _unhandled.Add(e.Exception.ToString());
        _vm?.Toast($"Ocurrió un error: {e.Exception.Message}", error: true);
        e.Handled = true;
    }
}

/// <summary>Recoge los errores de enlace de datos que WPF normalmente solo escribe en la salida de depuración.</summary>
internal sealed class BindingErrorListener : TraceListener
{
    private readonly StringBuilder _line = new();
    public List<string> Errors { get; } = new();

    public override void Write(string? message) => _line.Append(message);

    public override void WriteLine(string? message)
    {
        _line.Append(message);
        var text = _line.ToString();
        _line.Clear();
        lock (Errors) Errors.Add(text);
        Log.Warn("Binding: " + text);
    }
}
