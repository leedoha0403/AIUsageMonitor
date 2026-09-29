using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UsageMonitorWpf.Controls;

public sealed class ChartSeries
{
    public string Name { get; init; } = "";
    public System.Windows.Media.Brush Brush { get; init; } = System.Windows.Media.Brushes.Gray;
    public List<(DateTimeOffset At, double Value)> Points { get; init; } = new();
    // Used in the combined view to tell a provider's weekly line apart from its 5H line without a second chart.
    public bool Dashed { get; init; }
}

// Lightweight line chart (0-100%) drawn directly with DrawingContext; no charting dependency.
public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(
        nameof(Range), typeof(TimeSpan), typeof(HistoryChart),
        new FrameworkPropertyMetadata(TimeSpan.FromDays(1), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(System.Windows.Media.Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(System.Windows.Media.Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public TimeSpan Range
    {
        get => (TimeSpan)GetValue(RangeProperty);
        set => SetValue(RangeProperty, value);
    }

    public System.Windows.Media.Brush Foreground
    {
        get => (System.Windows.Media.Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public System.Windows.Media.Brush GridBrush
    {
        get => (System.Windows.Media.Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double left = 36, bottom = 22, top = 8, right = 8;
        var width = ActualWidth - left - right;
        var height = ActualHeight - top - bottom;
        if (width <= 10 || height <= 10) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        var gridPen = new System.Windows.Media.Pen(GridBrush, 1);
        gridPen.Freeze();
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        foreach (var level in new[] { 0, 25, 50, 75, 100 })
        {
            var y = top + height * (1 - level / 100.0);
            dc.DrawLine(gridPen, new System.Windows.Point(left, y), new System.Windows.Point(left + width, y));
            var label = new FormattedText($"{level}", CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, typeface, 10, Foreground, dpi);
            dc.DrawText(label, new System.Windows.Point(left - 6 - label.Width, y - label.Height / 2));
        }

        var end = DateTimeOffset.Now;
        var start = end - Range;
        double X(DateTimeOffset t) => left + width * Math.Clamp((t - start).TotalSeconds / Range.TotalSeconds, 0, 1);
        double Y(double v) => top + height * (1 - Math.Clamp(v, 0, 100) / 100.0);

        for (var i = 0; i <= 4; i++)
        {
            var t = start + TimeSpan.FromTicks(Range.Ticks * i / 4);
            var format = Range <= TimeSpan.FromDays(1) ? "HH:mm" : "MM-dd";
            var label = new FormattedText(t.ToLocalTime().ToString(format), CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, typeface, 10, Foreground, dpi);
            var x = Math.Clamp(X(t) - label.Width / 2, left, left + width - label.Width);
            dc.DrawText(label, new System.Windows.Point(x, top + height + 5));
        }

        if (Series == null) return;
        foreach (var series in Series)
        {
            var points = series.Points.Where(p => p.At >= start).OrderBy(p => p.At).ToList();
            // Carry the last value from before the range so lines start at the left edge.
            var before = series.Points.Where(p => p.At < start).OrderBy(p => p.At).LastOrDefault();
            if (before != default) points.Insert(0, (start, before.Value));
            if (points.Count == 0) continue;

            var pen = new System.Windows.Media.Pen(series.Brush, 2) { LineJoin = PenLineJoin.Round };
            if (series.Dashed) pen.DashStyle = new DashStyle(new double[] { 3, 2 }, 0);
            pen.Freeze();
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new System.Windows.Point(X(points[0].At), Y(points[0].Value)), false, false);
                for (var i = 1; i < points.Count; i++)
                {
                    // Step shape: usage holds its value until the next sample.
                    ctx.LineTo(new System.Windows.Point(X(points[i].At), Y(points[i - 1].Value)), true, true);
                    ctx.LineTo(new System.Windows.Point(X(points[i].At), Y(points[i].Value)), true, true);
                }
                ctx.LineTo(new System.Windows.Point(X(end), Y(points[^1].Value)), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
            dc.DrawEllipse(series.Brush, null, new System.Windows.Point(X(end), Y(points[^1].Value)), 3, 3);
        }
    }
}
