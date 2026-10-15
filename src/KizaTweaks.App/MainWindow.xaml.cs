using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using KizaTweaks.Presentation.ViewModels;

namespace KizaTweaks.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is MainViewModel vm) await vm.InitializeAsync();
        };
    }

    /// <summary>Barra de título oscura (Windows 10 20H1+ y Windows 11).</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int)); // builds antiguos
            int caption = 0x00160F0F; // COLORREF (BGR) = #0F0F16
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int)); // solo Windows 11
        }
        catch
        {
            // Sin barra oscura en sistemas que no lo soportan.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
