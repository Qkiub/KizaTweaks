using System.Windows;
using System.Windows.Controls;

namespace KizaTweaks.App.Views;

/// <summary>Etiqueta de color (impacto, riesgo, estado…).</summary>
public partial class Pill : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(Pill), new PropertyMetadata(""));

    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(string), typeof(Pill), new PropertyMetadata("#94A3B8"));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public Pill() => InitializeComponent();
}
