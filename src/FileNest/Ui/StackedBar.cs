using System.Windows;
using System.Windows.Media;

namespace FileNest.Ui;

public sealed record BarSegment(double Value, Brush Brush);

/// <summary>The thin "what was sorted today" bar: one coloured segment per nest, 2px gaps, rounded ends.</summary>
public sealed class StackedBar : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<BarSegment>), typeof(StackedBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(StackedBar),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<BarSegment>? Segments
    {
        get => (IReadOnlyList<BarSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 8);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var radius = height / 2;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), radius, radius));

        var segments = Segments?.Where(s => s.Value > 0).ToList() ?? [];
        var total = segments.Sum(s => s.Value);
        if (total <= 0)
        {
            dc.DrawRectangle(Track, null, new Rect(0, 0, width, height));
            dc.Pop();
            return;
        }

        const double gap = 2;
        var usable = width - gap * (segments.Count - 1);
        var x = 0.0;
        foreach (var segment in segments)
        {
            var w = Math.Max(2, usable * segment.Value / total);
            dc.DrawRectangle(segment.Brush, null, new Rect(x, 0, Math.Min(w, width - x), height));
            x += w + gap;
        }

        dc.Pop();
    }
}
