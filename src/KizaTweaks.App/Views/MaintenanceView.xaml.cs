using System.Windows.Controls;

namespace KizaTweaks.App.Views;

public partial class MaintenanceView : UserControl
{
    public MaintenanceView() => InitializeComponent();

    /// <summary>La consola baja sola al final cuando llega texto nuevo.</summary>
    private void Console_TextChanged(object sender, TextChangedEventArgs e) => ConsoleBox.ScrollToEnd();
}
