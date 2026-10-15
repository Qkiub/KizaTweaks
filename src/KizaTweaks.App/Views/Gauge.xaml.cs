using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KizaTweaks.App.Views;

/// <summary>Medidor de 270° (estilo tacómetro) para el progreso del Modo FPS.</summary>
public partial class Gauge : UserControl
{
    private const double StartAngle = 135, Sweep = 270, Radius = 82, Center = 98;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Gauge), new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(Gauge), new PropertyMetadata(""));

    // Valor que se dibuja (se anima hacia Value)
    private static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
        "Display", typeof(double), typeof(Gauge), new PropertyMetadata(0.0, (d, _) => ((Gauge)d).Redraw()));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    public Gauge()
    {
        InitializeComponent();
        Track.Data = Arc(Sweep);
        Number.Text = "0";
        Redraw();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (Gauge)d;
        var to = Math.Clamp((double)e.NewValue, 0, 100);
        g.Number.Text = ((int)Math.Round(to)).ToString();
        if (!SystemParameters.ClientAreaAnimation)
        {
            g.SetValue(DisplayProperty, to);
            return;
        }
        // Único movimiento de la página: la aguja "sube" hasta el valor.
        g.BeginAnimation(DisplayProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void Redraw()
    {
        var v = (double)GetValue(DisplayProperty);
        Bar.Data = v <= 0.5 ? null : Arc(Sweep * v / 100.0);
    }

    private static Geometry Arc(double sweep)
    {
        static Point At(double deg)
        {
            var r = deg * Math.PI / 180;
            return new Point(Center + Radius * Math.Cos(r), Center + Radius * Math.Sin(r));
        }
        var fig = new PathFigure { StartPoint = At(StartAngle), IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment(At(StartAngle + Math.Min(sweep, 359.9)), new Size(Radius, Radius), 0,
            sweep > 180, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }
}
