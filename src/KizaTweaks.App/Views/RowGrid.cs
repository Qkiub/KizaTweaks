using System.Windows;
using System.Windows.Controls;

namespace KizaTweaks.App.Views;

/// <summary>
/// Cuadrícula de columnas iguales donde cada fila toma la altura de su elemento más alto
/// (a diferencia de UniformGrid, que iguala todas las filas a la más alta de toda la lista).
/// </summary>
public sealed class RowGrid : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(RowGrid), new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(RowGrid), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private double ColumnWidth(double total) => Math.Max(0, (total - Gap * (Columns - 1)) / Columns);

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 900 : available.Width;
        var colW = ColumnWidth(width);
        double height = 0, rowH = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            c.Measure(new Size(colW, double.PositiveInfinity));
            rowH = Math.Max(rowH, c.DesiredSize.Height);
            if (i % Columns == Columns - 1 || i == InternalChildren.Count - 1)
            {
                height += rowH + (i < InternalChildren.Count - 1 ? Gap : 0);
                rowH = 0;
            }
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var colW = ColumnWidth(final.Width);
        double y = 0;
        for (int start = 0; start < InternalChildren.Count; start += Columns)
        {
            var end = Math.Min(start + Columns, InternalChildren.Count);
            double rowH = 0;
            for (int i = start; i < end; i++) rowH = Math.Max(rowH, InternalChildren[i].DesiredSize.Height);
            for (int i = start; i < end; i++)
                InternalChildren[i].Arrange(new Rect((i - start) * (colW + Gap), y, colW, rowH));
            y += rowH + Gap;
        }
        return final;
    }
}
