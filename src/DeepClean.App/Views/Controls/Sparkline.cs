using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DeepClean.App.Views.Controls;

/// <summary>
/// Draws a compact line chart for a sequence of values normalised against
/// <see cref="Maximum"/>. Used by the dashboard to show recent usage history
/// without pulling in a charting dependency.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IReadOnlyList<double>),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Values in chronological order, oldest first.</summary>
    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>Value that maps to the top edge of the control.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Optional area fill drawn below the line.</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var values = Values;
        var width = ActualWidth;
        var height = ActualHeight;

        if (values is null || values.Count < 2 || width < 2 || height < 2)
        {
            return;
        }

        var maximum = Maximum > 0 ? Maximum : 100.0;
        var inset = StrokeThickness / 2 + 0.5;
        var usableWidth = width - inset * 2;
        var usableHeight = height - inset * 2;

        if (usableWidth <= 0 || usableHeight <= 0)
        {
            return;
        }

        var fill = Fill;
        var closesArea = fill is not null;
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(0, values[0]), closesArea, closesArea);

            for (var i = 1; i < values.Count; i++)
            {
                context.LineTo(ToPoint(i, values[i]), isStroked: true, isSmoothJoin: false);
            }

            if (closesArea)
            {
                context.LineTo(new Point(width - inset, height - inset), isStroked: false, isSmoothJoin: false);
                context.LineTo(new Point(inset, height - inset), isStroked: false, isSmoothJoin: false);
            }
        }

        geometry.Freeze();

        Pen? pen = null;

        if (Stroke is not null)
        {
            pen = new Pen(Stroke, StrokeThickness) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
        }

        drawingContext.DrawGeometry(fill, pen, geometry);

        Point ToPoint(int index, double value)
        {
            var x = inset + usableWidth * index / (values.Count - 1);
            var ratio = Math.Clamp(value / maximum, 0.0, 1.0);
            var y = inset + usableHeight * (1.0 - ratio);
            return new Point(x, y);
        }
    }
}
