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
            // Solo Windows 11: barra de título del mismo color que la barra lateral (COLORREF = BGR)
            int caption = 0x00180F0B;  // #0B0F18
            int text = 0x00F7F1EE;     // #EEF1F7
            int border = 0x00473027;   // #273047
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        }
        catch
        {
            // Sin barra oscura en sistemas que no lo soportan.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
