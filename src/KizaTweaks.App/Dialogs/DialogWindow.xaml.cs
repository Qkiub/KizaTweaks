using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KizaTweaks.Presentation.Mvvm;

namespace KizaTweaks.App.Dialogs;

public partial class DialogWindow : Window
{
    public int Result { get; private set; } = -1;

    public DialogWindow(string title, string message, string[] buttons)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        for (int i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var b = new Button
            {
                Content = buttons[i],
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 96,
                Style = (Style)FindResource(i == 0 ? "PrimaryButton" : "BaseButton"),
                IsDefault = i == 0,
                IsCancel = buttons.Length > 1 && i == buttons.Length - 1,
            };
            b.Click += (_, _) =>
            {
                Result = index;
                DialogResult = true;
            };
            Buttons.Children.Add(b);
        }
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }
}

public sealed class WpfDialogService : IDialogService
{
    /// <summary>En modo de prueba automática no se muestran diálogos: se registran y se cancelan.</summary>
    public bool Unattended { get; init; }

    public Task<int> ChooseAsync(string title, string message, params string[] buttons)
    {
        if (Unattended)
        {
            Core.Log.Info($"[diálogo omitido] {title}: {message.Replace('\n', ' ')}");
            return Task.FromResult(-1);
        }
        var owner = Application.Current?.MainWindow;
        var w = new DialogWindow(title, message, buttons.Length == 0 ? new[] { "Aceptar" } : buttons);
        if (owner is not null && owner.IsVisible) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        return Task.FromResult(w.Result);
    }

    public Task InfoAsync(string title, string message) => ChooseAsync(title, message, "Entendido");

    public Task<string?> PickFileAsync(string title, string filter)
    {
        if (Unattended) return Task.FromResult<string?>(null);
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        var owner = Application.Current?.MainWindow;
        var ok = owner is not null && owner.IsVisible ? dlg.ShowDialog(owner) : dlg.ShowDialog();
        return Task.FromResult(ok == true ? dlg.FileName : null);
    }
}
