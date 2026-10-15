using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace KizaTweaks.Presentation.Mvvm;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Comando asíncrono que no se puede ejecutar dos veces a la vez y captura errores.</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _running;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>Se invoca si el comando lanza una excepción no controlada.</summary>
    public static Action<Exception>? ErrorHandler { get; set; }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (_running) return;
        _running = true;
        RaiseCanExecuteChanged();
        try { await _execute(parameter); }
        catch (Exception ex)
        {
            Core.Log.Error("Error no controlado en un comando", ex);
            ErrorHandler?.Invoke(ex);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Diálogos implementados por la capa visual (WPF).</summary>
public interface IDialogService
{
    /// <summary>Muestra un mensaje con varios botones; devuelve el índice elegido o -1 si se cierra.</summary>
    Task<int> ChooseAsync(string title, string message, params string[] buttons);
    Task InfoAsync(string title, string message);
    /// <summary>Selector de archivo; null si se cancela. filter con el formato de Windows: "Juegos (*.exe)|*.exe".</summary>
    Task<string?> PickFileAsync(string title, string filter);
}

public static class DialogExtensions
{
    public static async Task<bool> ConfirmAsync(this IDialogService d, string title, string message, string ok = "Continuar", string cancel = "Cancelar") =>
        await d.ChooseAsync(title, message, ok, cancel) == 0;
}

/// <summary>Lo que las páginas necesitan de la ventana principal (barra de progreso, avisos, reinicio).</summary>
public interface IShellHost
{
    Task RunBusyAsync(string text, Func<IProgress<string>, Task> work);
    void Toast(string message, bool error = false);
    void MarkRebootRequired();
    void Navigate(string pageKey);
}
