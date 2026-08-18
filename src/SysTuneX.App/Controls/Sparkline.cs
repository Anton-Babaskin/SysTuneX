using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysTuneX.App.Controls;

/// <summary>
/// A tiny filled line chart for the live dashboard counters.
///
/// Drawn directly in <see cref="OnRender"/> rather than built from shapes: the series is
/// replaced once a second and rebuilding a Polyline's point collection that often is visibly
/// more expensive than just painting two geometries.
/// </summary>
public sealed class Sparkline : Control
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Number of samples the horizontal axis is scaled to, so a partly filled series does not stretch.</summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity),
        typeof(int),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _observed;

    public Sparkline()
    {
        // No template and no interaction: the control exists purely to paint in OnRender.
        Focusable = false;
        IsHitTestVisible = false;
    }

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

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

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0 || Values is null)
        {
            return;
        }

        List<double> samples = [];
        foreach (object? item in Values)
        {
            if (item is null)
            {
                continue;
            }

            try
            {
                samples.Add(System.Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                // A non-numeric item is simply not part of the series.
            }
        }

        if (samples.Count < 2)
        {
            return;
        }

        double maximum = Maximum > 0 ? Maximum : samples.Max();
        if (maximum <= 0)
        {
            return;
        }

        int capacity = Math.Max(Capacity, samples.Count);
        double step = width / Math.Max(1, capacity - 1);
        double inset = StrokeThickness;
        double usableHeight = Math.Max(1, height - inset * 2);

        // The newest sample sits at the right edge, so a series that has not filled up yet grows
        // in from the left instead of stretching to fit.
        double startX = width - (samples.Count - 1) * step;

        var line = new StreamGeometry();
        var area = new StreamGeometry();

        using (StreamGeometryContext lineContext = line.Open())
        using (StreamGeometryContext areaContext = area.Open())
        {
            Point First() => new(startX, PointFor(samples[0]));
            double PointFor(double value) => inset + usableHeight * (1 - Math.Clamp(value / maximum, 0, 1));

            lineContext.BeginFigure(First(), isFilled: false, isClosed: false);
            areaContext.BeginFigure(new Point(startX, height), isFilled: true, isClosed: true);
            areaContext.LineTo(First(), isStroked: false, isSmoothJoin: false);

            for (int i = 1; i < samples.Count; i++)
            {
                var point = new Point(startX + i * step, PointFor(samples[i]));
                lineContext.LineTo(point, isStroked: true, isSmoothJoin: true);
                areaContext.LineTo(point, isStroked: false, isSmoothJoin: true);
            }

            areaContext.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
        }

        line.Freeze();
        area.Freeze();

        if (Fill is not null)
        {
            drawingContext.DrawGeometry(Fill, null, area);
        }

        drawingContext.DrawGeometry(null, new Pen(Stroke, StrokeThickness) { LineJoin = PenLineJoin.Round }, line);
    }

    private static void OnValuesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Sparkline sparkline)
        {
            sparkline.Observe(args.NewValue as INotifyCollectionChanged);
        }
    }

    private void Observe(INotifyCollectionChanged? collection)
    {
        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnCollectionChanged;
        }

        _observed = collection;

        if (_observed is not null)
        {
            _observed.CollectionChanged += OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => InvalidateVisual();
}
