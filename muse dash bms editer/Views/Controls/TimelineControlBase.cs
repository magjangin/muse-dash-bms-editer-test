using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using bms_editer.Services;

namespace bms_editer.Views.Controls;

public abstract class TimelineControlBase : Control
{
    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(RowHeight), 16.0);

    public static readonly StyledProperty<double> VerticalZoomProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(VerticalZoom), 1.0);

    public static readonly StyledProperty<double> HorizontalZoomProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(HorizontalZoom), 1.0);

    public static readonly StyledProperty<int> MeasureCountProperty =
        AvaloniaProperty.Register<TimelineControlBase, int>(nameof(MeasureCount), 32);

    public static readonly StyledProperty<int> BeatSplitProperty =
        AvaloniaProperty.Register<TimelineControlBase, int>(nameof(BeatSplit), 16);

    public static readonly StyledProperty<int> GridMeasureProperty =
        AvaloniaProperty.Register<TimelineControlBase, int>(nameof(GridMeasure), 4);

    public static readonly StyledProperty<double> BpmProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(Bpm), 120.0);

    public static readonly StyledProperty<double> DurationSecondsProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(DurationSeconds));

    public static readonly StyledProperty<double> PlaybackPositionSecondsProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(PlaybackPositionSeconds));

    public static readonly StyledProperty<double> AudioOffsetSecondsProperty =
        AvaloniaProperty.Register<TimelineControlBase, double>(nameof(AudioOffsetSeconds));

    public static readonly StyledProperty<bool> IsPlaybackCursorVisibleProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsPlaybackCursorVisible));

    public static readonly StyledProperty<bool> IsGridSyncFlashVisibleProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsGridSyncFlashVisible));

    public static readonly StyledProperty<bool> IsHorizontalViewProperty =
        AvaloniaProperty.Register<TimelineControlBase, bool>(nameof(IsHorizontalView));

    public static readonly StyledProperty<ChartTimeline?> TimelineProperty =
        AvaloniaProperty.Register<TimelineControlBase, ChartTimeline?>(nameof(Timeline));

    // 마디 위치 <-> 시각 변환. 뷰모델이 넣어준다.
    // 비어 있으면 Bpm 하나만 쓰는 균일 시간축으로 대신한다(디자이너 미리보기 등).
    public ChartTimeline? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    // 실제로 쓸 시간축. BPM 이 바뀌면 균일 시간축도 따라 바뀌어야 해서 캐시해 둔다.
    private ChartTimeline? _fallbackTimeline;
    private double _fallbackTimelineBpm;

    protected ChartTimeline EffectiveTimeline
    {
        get
        {
            if (Timeline is { } timeline)
                return timeline;

            if (_fallbackTimeline is null || _fallbackTimelineBpm != Bpm)
            {
                _fallbackTimeline = ChartTimeline.Uniform(Bpm);
                _fallbackTimelineBpm = Bpm;
            }

            return _fallbackTimeline;
        }
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double VerticalZoom
    {
        get => GetValue(VerticalZoomProperty);
        set => SetValue(VerticalZoomProperty, value);
    }

    public double HorizontalZoom
    {
        get => GetValue(HorizontalZoomProperty);
        set => SetValue(HorizontalZoomProperty, value);
    }

    public int MeasureCount
    {
        get => GetValue(MeasureCountProperty);
        set => SetValue(MeasureCountProperty, value);
    }

    public int BeatSplit
    {
        get => GetValue(BeatSplitProperty);
        set => SetValue(BeatSplitProperty, value);
    }

    public int GridMeasure
    {
        get => GetValue(GridMeasureProperty);
        set => SetValue(GridMeasureProperty, value);
    }

    public double Bpm
    {
        get => GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    public double DurationSeconds
    {
        get => GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public double PlaybackPositionSeconds
    {
        get => GetValue(PlaybackPositionSecondsProperty);
        set => SetValue(PlaybackPositionSecondsProperty, value);
    }

    // 음원을 타임라인에서 뒤로 미는 양(초).
    // 격자와 노트는 차트 시간축 위에 그대로 두고, 음원에서 나온 것만(파형, 온셋, 재생 커서) 이 값만큼 민다.
    public double AudioOffsetSeconds
    {
        get => GetValue(AudioOffsetSecondsProperty);
        set => SetValue(AudioOffsetSecondsProperty, value);
    }

    public double AudioRatio(double audioSeconds) =>
        DurationSeconds <= 0 ? 0.0 : (audioSeconds + AudioOffsetSeconds) / DurationSeconds;

    public double AudioSecondsAtRatio(double ratio) =>
        (ratio * DurationSeconds) - AudioOffsetSeconds;

    public bool IsPlaybackCursorVisible
    {
        get => GetValue(IsPlaybackCursorVisibleProperty);
        set => SetValue(IsPlaybackCursorVisibleProperty, value);
    }

    public bool IsGridSyncFlashVisible
    {
        get => GetValue(IsGridSyncFlashVisibleProperty);
        set => SetValue(IsGridSyncFlashVisibleProperty, value);
    }

    public bool IsHorizontalView
    {
        get => GetValue(IsHorizontalViewProperty);
        set => SetValue(IsHorizontalViewProperty, value);
    }

    static TimelineControlBase()
    {
        AffectsRender<TimelineControlBase>(
            RowHeightProperty, VerticalZoomProperty, HorizontalZoomProperty, MeasureCountProperty,
            BeatSplitProperty, GridMeasureProperty, BpmProperty, DurationSecondsProperty,
            PlaybackPositionSecondsProperty, IsPlaybackCursorVisibleProperty, IsGridSyncFlashVisibleProperty,
            IsHorizontalViewProperty, TimelineProperty, AudioOffsetSecondsProperty);

        // HorizontalZoom 이 여기 빠져 있어서, 가로 줌을 바꾸면 다시 그려지기만 하고
        // 컨트롤 크기는 그대로였다. 레인이 잘리거나 오른쪽에 빈 자리가 남았다.
        AffectsMeasure<TimelineControlBase>(
            RowHeightProperty, VerticalZoomProperty, HorizontalZoomProperty, MeasureCountProperty,
            BeatSplitProperty, GridMeasureProperty, DurationSecondsProperty, IsHorizontalViewProperty);
    }

    protected double GetTimelineHeight()
    {
        var spacingScale = GetGridSpacingScale();
        if (DurationSeconds > 0)
            return Math.Max(1.0, DurationSeconds * RowHeight * VerticalZoom * spacingScale / 2.0);

        return MeasureCount * RowHeight * VerticalZoom * spacingScale;
    }

    protected double GetGridSpacingScale() => Math.Max(1.0, BeatSplit / (double)Math.Max(1, GridMeasure));

    protected static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;

    protected static bool IsMeasureBeatLine(int index, int split, int gridMeasure)
    {
        if (gridMeasure <= 0 || split < gridMeasure || split % gridMeasure != 0)
            return false;

        return Mod(index, split / gridMeasure) == 0;
    }

    protected enum GridLineKind
    {
        SubBeat,
        Beat,
        Measure,
    }

    protected readonly record struct GridLine(double Position, GridLineKind Kind, int Measure, double Seconds);

    // 타임라인에 그릴 격자선을 순서대로 내놓는다.
    protected IEnumerable<GridLine> EnumerateGridLines(double timelineLength)
    {
        var split = Math.Max(1, BeatSplit);

        // 배경 음원이 있으면 화면 전체가 곡 길이를 뜻하므로 초 단위로 훑는다.
        if (DurationSeconds > 0 && Bpm > 0)
        {
            // 격자 칸의 시각은 Timeline 이 정한다. 예전에는 여기서 240/(BPM*split) 을
            // 직접 써서, BPM 이 바뀌거나 4/4가 아닌 마디가 있으면 그 뒤로 전부 어긋났다.
            var timeline = EffectiveTimeline;

            for (var index = 0; ; index++)
            {
                var seconds = timeline.SecondsAt((double)index / split);
                if (seconds > DurationSeconds)
                    yield break;

                var position = ToTimelinePosition(seconds / DurationSeconds, timelineLength);
                if (position < -0.5 || position > timelineLength + 0.5)
                    continue;

                yield return new GridLine(
                    position,
                    ClassifyGridLine(index, split),
                    index / split,
                    seconds);
            }
        }
        else
        {
            // 음원이 없으면 마디 높이가 곧 화면 높이다.
            var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
            var measureTimeline = EffectiveTimeline;

            // 마지막 마디의 닫는 선까지 그리려고 MeasureCount 까지 돈다.
            // 범위를 벗어나는 보조선은 아래 검사에서 걸러진다.
            for (var measure = 0; measure <= MeasureCount; measure++)
            {
                for (var beat = 0; beat < split; beat++)
                {
                    var measurePosition = measure + (beat / (double)split);
                    var offset = measurePosition * rowHeight;
                    var position = IsHorizontalView ? offset : timelineLength - offset;

                    // 곱셈 순서 차이로 끝 선이 반 픽셀쯤 넘칠 수 있어 여유를 둔다.
                    if (position < -0.5 || position > timelineLength + 0.5)
                        continue;

                    yield return new GridLine(
                        position,
                        ClassifyGridLine(beat, split),
                        measure,
                        measureTimeline.SecondsAt(measurePosition));
                }
            }
        }
    }

    private GridLineKind ClassifyGridLine(int index, int split) =>
        Mod(index, split) == 0
            ? GridLineKind.Measure
            : IsMeasureBeatLine(index, split, GridMeasure)
                ? GridLineKind.Beat
                : GridLineKind.SubBeat;

    // 세로 뷰는 아래가 0초라 비율을 뒤집는다. 이 뒤집기가 여기저기 흩어져 있었다.
    protected double ToTimelinePosition(double ratio, double timelineLength) =>
        IsHorizontalView ? ratio * timelineLength : (1.0 - ratio) * timelineLength;

    protected void DrawPlaybackCursor(DrawingContext context, double width, double height)
    {
        if (!IsPlaybackCursorVisible || DurationSeconds <= 0)
            return;

        var ratio = Math.Clamp(AudioRatio(PlaybackPositionSeconds), 0, 1);
        var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 50, 255, 120)), 5);
        var cursorPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 255, 90)), 2);

        if (IsHorizontalView)
        {
            var x = ratio * width;
            context.DrawLine(glowPen, new Point(x, 0), new Point(x, height));
            context.DrawLine(cursorPen, new Point(x, 0), new Point(x, height));
        }
        else
        {
            var y = (1.0 - ratio) * height;
            context.DrawLine(glowPen, new Point(0, y), new Point(width, y));
            context.DrawLine(cursorPen, new Point(0, y), new Point(width, y));
        }
    }

    protected void DrawGridSyncFlash(DrawingContext context, double width, double height)
    {
        if (!IsGridSyncFlashVisible)
            return;

        context.FillRectangle(new SolidColorBrush(Color.FromArgb(36, 80, 255, 140)), new Rect(0, 0, width, height));
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(190, 90, 255, 150)), 2), new Rect(1, 1, width - 2, height - 2));
    }
}
