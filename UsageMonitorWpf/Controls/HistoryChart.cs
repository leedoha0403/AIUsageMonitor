using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UsageMonitorWpf.Controls;

public sealed class ChartSeries
{
    public string Name { get; init; } = "";
    public System.Windows.Media.Brush Brush { get; init; } = System.Windows.Media.Brushes.Gray;
    public List<(DateTimeOffset At, double Value)> Points { get; init; } = new();
}

// A filled, step-shaped region (e.g. cumulative usage) drawn behind the line series, with thin divider
// lines at each Boundaries timestamp splitting it into segments (e.g. one per 5H session).
public sealed class ChartArea
{
    public string Name { get; init; } = "";
    public System.Windows.Media.Brush Fill { get; init; } = System.Windows.Media.Brushes.Transparent;
    public List<(DateTimeOffset At, double Value)> Points { get; init; } = new();
    public List<DateTimeOffset> Boundaries { get; init; } = new();
}

// Lightweight line chart (0-100%) drawn directly with DrawingContext; no charting dependency.
public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AreasProperty = DependencyProperty.Register(
        nameof(Areas), typeof(IReadOnlyList<ChartArea>), typeof(HistoryChart),
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

    public IReadOnlyList<ChartArea>? Areas
    {
        get => (IReadOnlyList<ChartArea>?)GetValue(AreasProperty);
        set => SetValue(AreasProperty, value);
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

        if (Areas != null)
        {
            var dividerPen = new System.Windows.Media.Pen(Foreground, 1) { DashStyle = DashStyles.Dot };
            dividerPen.Freeze();
            foreach (var area in Areas)
            {
                var points = area.Points.Where(p => p.At >= start).OrderBy(p => p.At).ToList();
                var before = area.Points.Where(p => p.At < start).OrderBy(p => p.At).LastOrDefault();
                if (before != default) points.Insert(0, (start, before.Value));
                if (points.Count == 0) continue;

                var fillGeometry = new StreamGeometry();
                using (var ctx = fillGeometry.Open())
                {
                    ctx.BeginFigure(new System.Windows.Point(X(points[0].At), top + height), true, true);
                    ctx.LineTo(new System.Windows.Point(X(points[0].At), Y(points[0].Value)), true, true);
                    for (var i = 1; i < points.Count; i++)
                    {
                        ctx.LineTo(new System.Windows.Point(X(points[i].At), Y(points[i - 1].Value)), true, true);
                        ctx.LineTo(new System.Windows.Point(X(points[i].At), Y(points[i].Value)), true, true);
                    }
                    var endX = X(end);
                    ctx.LineTo(new System.Windows.Point(endX, Y(points[^1].Value)), true, true);
                    ctx.LineTo(new System.Windows.Point(endX, top + height), true, true);
                }
                fillGeometry.Freeze();
                dc.DrawGeometry(area.Fill, null, fillGeometry);

                foreach (var boundary in area.Boundaries.Where(b => b >= start && b <= end))
                {
                    var valueAt = points.LastOrDefault(p => p.At <= boundary).Value;
                    var x = X(boundary);
                    dc.DrawLine(dividerPen, new System.Windows.Point(x, Y(valueAt)), new System.Windows.Point(x, top + height));
                }
            }
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
