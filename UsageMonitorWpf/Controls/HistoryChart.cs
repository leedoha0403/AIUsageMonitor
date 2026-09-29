using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

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
        new FrameworkPropertyMetadata(TimeSpan.FromDays(1), FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged));

    // The actually-drawn time window, eased toward Range whenever it changes so picking a different
    // date range zooms/pans smoothly instead of jumping straight to the new window.
    private static readonly DependencyProperty AnimatedRangeSecondsProperty = DependencyProperty.Register(
        "AnimatedRangeSeconds", typeof(double), typeof(HistoryChart),
        new FrameworkPropertyMetadata(TimeSpan.FromDays(1).TotalSeconds, FrameworkPropertyMetadataOptions.AffectsRender));

    private double AnimatedRangeSeconds => (double)GetValue(AnimatedRangeSecondsProperty);

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (HistoryChart)d;
        var from = chart.AnimatedRangeSeconds > 0 ? chart.AnimatedRangeSeconds : ((TimeSpan)e.OldValue).TotalSeconds;
        var to = ((TimeSpan)e.NewValue).TotalSeconds;
        var animation = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(350)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        chart.BeginAnimation(AnimatedRangeSecondsProperty, animation);
    }

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(System.Windows.Media.Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(System.Windows.Media.Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    // When set to a Series/Area Name, that one is drawn last (on top) and at full strength while every
    // other one is faded, so hovering a legend entry calls out just that provider.
    public static readonly DependencyProperty HighlightedNameProperty = DependencyProperty.Register(
        nameof(HighlightedName), typeof(string), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string? HighlightedName
    {
        get => (string?)GetValue(HighlightedNameProperty);
        set => SetValue(HighlightedNameProperty, value);
    }

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

        // Animate toward Range instead of jumping straight to it, so switching date ranges pans/zooms smoothly.
        var animatedRange = TimeSpan.FromSeconds(AnimatedRangeSeconds);
        var end = DateTimeOffset.Now;
        var start = end - animatedRange;
        double X(DateTimeOffset t) => left + width * Math.Clamp((t - start).TotalSeconds / animatedRange.TotalSeconds, 0, 1);
        double Y(double v) => top + height * (1 - Math.Clamp(v, 0, 100) / 100.0);

        for (var i = 0; i <= 4; i++)
        {
            var t = start + TimeSpan.FromTicks(animatedRange.Ticks * i / 4);
            var format = Range <= TimeSpan.FromDays(1) ? "HH:mm" : "MM-dd";
            var label = new FormattedText(t.ToLocalTime().ToString(format), CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, typeface, 10, Foreground, dpi);
            var x = Math.Clamp(X(t) - label.Width / 2, left, left + width - label.Width);
            dc.DrawText(label, new System.Windows.Point(x, top + height + 5));
        }

        if (Areas != null)
        {
            const double gap = 3; // px of visible separation between adjacent session blocks
            // The highlighted one (if any) is drawn last, on top of the rest, which are faded.
            foreach (var area in Areas.OrderBy(a => a.Name == HighlightedName ? 1 : 0))
            {
                var points = area.Points.Where(p => p.At >= start).OrderBy(p => p.At).ToList();
                var before = area.Points.Where(p => p.At < start).OrderBy(p => p.At).LastOrDefault();
                if (before != default) points.Insert(0, (start, before.Value));
                if (points.Count == 0) continue;

                double ValueAt(DateTimeOffset t)
                {
                    var last = default((DateTimeOffset At, double Value));
                    var found = false;
                    foreach (var p in points)
                    {
                        if (p.At > t) break;
                        last = p;
                        found = true;
                    }
                    return found ? last.Value : points[0].Value;
                }

                var faded = HighlightedName != null && area.Name != HighlightedName;
                var fill = faded ? Fade(area.Fill, 0.25) : area.Fill;

                var strokeColor = area.Fill is System.Windows.Media.SolidColorBrush scb
                    ? System.Windows.Media.Color.FromArgb(255, scb.Color.R, scb.Color.G, scb.Color.B)
                    : Colors.Gray;
                var stroke = new System.Windows.Media.SolidColorBrush(strokeColor);
                stroke.Freeze();
                var strokePen = new System.Windows.Media.Pen(faded ? Fade(stroke, 0.35) : stroke, 1.25);
                strokePen.Freeze();

                // Each session becomes its own trapezoid block (flat base, rising/falling top edge from its
                // start value to its end value), with a visible gap and outline so blocks read as separate
                // pieces instead of one continuous fill.
                var cuts = new List<DateTimeOffset> { start };
                cuts.AddRange(area.Boundaries.Where(b => b > start && b < end).OrderBy(b => b));
                cuts.Add(end);

                for (var i = 0; i < cuts.Count - 1; i++)
                {
                    var x0 = X(cuts[i]) + (i == 0 ? 0 : gap / 2);
                    var x1 = X(cuts[i + 1]) - (i == cuts.Count - 2 ? 0 : gap / 2);
                    if (x1 <= x0) continue;

                    var y0 = Y(ValueAt(cuts[i]));
                    var y1 = Y(ValueAt(cuts[i + 1]));

                    var blockGeometry = new StreamGeometry();
                    using (var ctx = blockGeometry.Open())
                    {
                        ctx.BeginFigure(new System.Windows.Point(x0, top + height), true, true);
                        ctx.LineTo(new System.Windows.Point(x0, y0), true, true);
                        ctx.LineTo(new System.Windows.Point(x1, y1), true, true);
                        ctx.LineTo(new System.Windows.Point(x1, top + height), true, true);
                    }
                    blockGeometry.Freeze();
                    dc.DrawGeometry(fill, strokePen, blockGeometry);
                }
            }
        }

        if (Series == null) return;
        // The highlighted one (if any) is drawn last, on top of the rest, which are faded.
        foreach (var series in Series.OrderBy(s => s.Name == HighlightedName ? 1 : 0))
        {
            var points = series.Points.Where(p => p.At >= start).OrderBy(p => p.At).ToList();
            // Carry the last value from before the range so lines start at the left edge.
            var before = series.Points.Where(p => p.At < start).OrderBy(p => p.At).LastOrDefault();
            if (before != default) points.Insert(0, (start, before.Value));
            if (points.Count == 0) continue;

            var faded = HighlightedName != null && series.Name != HighlightedName;
            var brush = faded ? Fade(series.Brush, 0.25) : series.Brush;
            var pen = new System.Windows.Media.Pen(brush, faded ? 1.5 : 2.5) { LineJoin = PenLineJoin.Round };
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
            dc.DrawEllipse(brush, null, new System.Windows.Point(X(end), Y(points[^1].Value)), faded ? 2.5 : 3.5, faded ? 2.5 : 3.5);
        }
    }

    private static System.Windows.Media.Brush Fade(System.Windows.Media.Brush brush, double opacity)
    {
        var clone = brush.Clone();
        clone.Opacity = opacity;
        clone.Freeze();
        return clone;
    }
}
