using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
        // 재생 커서와 격자 맞춤 번쩍임은 여기 넣지 않는다. 겹 컨트롤(OverlayLayer)만 다시 그린다.
        AffectsRender<TimelineControlBase>(
            RowHeightProperty, VerticalZoomProperty, HorizontalZoomProperty, MeasureCountProperty,
            BeatSplitProperty, GridMeasureProperty, BpmProperty, DurationSecondsProperty,
            IsHorizontalViewProperty, TimelineProperty, AudioOffsetSecondsProperty);

        // HorizontalZoom 이 여기 빠져 있어서, 가로 줌을 바꾸면 다시 그려지기만 하고
        // 컨트롤 크기는 그대로였다. 레인이 잘리거나 오른쪽에 빈 자리가 남았다.
        AffectsMeasure<TimelineControlBase>(
            RowHeightProperty, VerticalZoomProperty, HorizontalZoomProperty, MeasureCountProperty,
            BeatSplitProperty, GridMeasureProperty, DurationSecondsProperty, IsHorizontalViewProperty);
    }

    // 재생 커서·번쩍임·드래그 사각형만 그리는 겹 컨트롤.
    //
    // 예전에는 커서가 격자와 같은 컨트롤에 있어서, 커서가 1px 움직일 때마다(초당 30번)
    // 격자선·노트·파형을 전부 다시 기록했다. 곡 하나에서 그리기 명령이 수만 개라
    // 재생 중 프레임이 그대로 떨어졌다. 자주 바뀌는 것만 떼어 내면 격자 쪽은 다시 그릴 일이 없다.
    private sealed class OverlayLayer : Control
    {
        private readonly TimelineControlBase _owner;

        public OverlayLayer(TimelineControlBase owner)
        {
            _owner = owner;

            // 그림만 얹는다. 클릭은 아래 격자가 받아야 한다.
            IsHitTestVisible = false;
        }

        public override void Render(DrawingContext context) => _owner.RenderOverlay(context);
    }

    private readonly OverlayLayer _overlay;

    protected TimelineControlBase()
    {
        _overlay = new OverlayLayer(this);
        VisualChildren.Add(_overlay);
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        var property = change.Property;
        if (property == PlaybackPositionSecondsProperty
            || property == IsPlaybackCursorVisibleProperty
            || property == IsGridSyncFlashVisibleProperty
            || property == AudioOffsetSecondsProperty
            || property == DurationSecondsProperty
            || property == IsHorizontalViewProperty)
        {
            _overlay.InvalidateVisual();
        }
    }

    protected void InvalidateOverlay() => _overlay.InvalidateVisual();

    // 커서처럼 자주 바뀌는 그림. 격자 위에 얹힌다.
    protected virtual void RenderOverlay(DrawingContext context)
    {
    }

    // 타임라인 축에서 이번 렌더에 그릴 구간(컨트롤 좌표). 양쪽이 무한대면 전부 그린다.
    protected readonly record struct TimelineRange(double Start, double End)
    {
        public static TimelineRange All { get; } = new(double.NegativeInfinity, double.PositiveInfinity);

        public bool IsAll => double.IsInfinity(Start) || double.IsInfinity(End);

        public bool Contains(double position, double margin = 0.0) =>
            position + margin >= Start && position - margin <= End;

        public bool Overlaps(double from, double to) => to >= Start && from <= End;
    }

    // 뷰포트 앞뒤로 더 그려 두는 길이. 스크롤로 컨트롤이 움직이면 Avalonia 가 어차피 다시 그리므로
    // 길게 잡을수록 스크롤 한 번의 비용만 는다. 렌더 전에 한 프레임 밀리는 정도만 덮으면 된다.
    private const double Overscan = 256.0;

    private Rect? _effectiveViewport;
    private TimelineRange _renderedRange = TimelineRange.All;

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var viewport = e.EffectiveViewport;

        // 잠깐 화면에서 사라진 경우(창 최소화 등). 마지막으로 본 자리를 그대로 둔다.
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return;

        _effectiveViewport = viewport;

        // 처음 뷰포트를 알게 됐거나, 그려 둔 구간을 벗어나면 다시 그린다.
        //
        // 잘라 그린 뒤로는 그린 구간 밖이 비어 있다. 스크롤에 Avalonia 가 다시 그려 주는 것은
        // 컨트롤의 Bounds 가 바뀌는 구조일 때뿐이라, 거기에 기대지 않고 여기서 직접 확인한다.
        var (start, end) = ViewportAxisRange(viewport);
        if (_renderedRange.IsAll || start < _renderedRange.Start || end > _renderedRange.End)
            InvalidateVisual();
    }

    private (double Start, double End) ViewportAxisRange(Rect viewport) =>
        IsHorizontalView ? (viewport.Left, viewport.Right) : (viewport.Top, viewport.Bottom);

    // Render 첫머리에서 부른다. 뷰포트를 아직 모르면(레이아웃 전, 비트맵으로 뜨는 중) 전부 그린다.
    protected TimelineRange BeginTimelineRender()
    {
        if ((_effectiveViewport ?? EstimateViewport()) is not { } viewport)
        {
            _renderedRange = TimelineRange.All;
            return _renderedRange;
        }

        var (start, end) = ViewportAxisRange(viewport);
        _renderedRange = new TimelineRange(start - Overscan, end + Overscan);
        return _renderedRange;
    }

    // EffectiveViewportChanged 는 값이 **바뀔 때만** 온다. 첫 레이아웃의 뷰포트가 등록 시점과 같으면
    // 첫 스크롤 전까지 아무 소식이 없어서, 그동안 곡 전체를 그리게 된다. 그래서 첫 렌더는 직접 구한다.
    private Rect? EstimateViewport()
    {
        var scroller = this.FindAncestorOfType<ScrollViewer>();
        if (scroller is null || scroller.Viewport.Width <= 0 || scroller.Viewport.Height <= 0)
            return null;

        return scroller.TranslatePoint(new Point(0, 0), this) is { } origin
            ? new Rect(origin, scroller.Viewport)
            : null;
    }

    // 화면 구간을 시간 비율(0~1) 구간으로. 세로 뷰는 아래가 0초라 뒤집힌다.
    protected (double From, double To) ToRatioRange(TimelineRange range, double timelineLength)
    {
        if (range.IsAll || timelineLength <= 0)
            return (0.0, 1.0);

        var from = Math.Clamp(range.Start / timelineLength, 0.0, 1.0);
        var to = Math.Clamp(range.End / timelineLength, 0.0, 1.0);
        return IsHorizontalView ? (from, to) : (1.0 - to, 1.0 - from);
    }

    protected double GetTimelineHeight() =>
        ComputeTimelineLength(DurationSeconds, MeasureCount, RowHeight, VerticalZoom, BeatSplit, GridMeasure);

    protected double GetGridSpacingScale() => GridSpacingScale(BeatSplit, GridMeasure);

    // 타임라인 길이 규칙은 여기 한 곳에만 둔다. 메인 창(스크롤 따라가기·가운데 버튼 스크럽)도 이 식을 부른다.
    // 예전에는 MainWindow 가 같은 식을 한 벌 따로 들고 있어서, 한쪽만 고치면 커서와 격자가 어긋났다.
    public static double ComputeTimelineLength(
        double durationSeconds, int measureCount, double rowHeight, double verticalZoom, int beatSplit, int gridMeasure)
    {
        var spacingScale = GridSpacingScale(beatSplit, gridMeasure);
        if (durationSeconds > 0)
            return Math.Max(1.0, durationSeconds * rowHeight * verticalZoom * spacingScale / 2.0);

        return measureCount * rowHeight * verticalZoom * spacingScale;
    }

    public static double GridSpacingScale(int beatSplit, int gridMeasure) =>
        Math.Max(1.0, beatSplit / (double)Math.Max(1, gridMeasure));

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
    protected IEnumerable<GridLine> EnumerateGridLines(double timelineLength) =>
        EnumerateGridLines(timelineLength, TimelineRange.All);

    // range 안의 격자선만 내놓는다. 범위 밖을 건너뛸 뿐 선의 위치·종류는 전체를 셀 때와 같다.
    protected IEnumerable<GridLine> EnumerateGridLines(double timelineLength, TimelineRange range)
    {
        var split = Math.Max(1, BeatSplit);

        // 배경 음원이 있으면 화면 전체가 곡 길이를 뜻하므로 초 단위로 훑는다.
        if (DurationSeconds > 0 && Bpm > 0)
        {
            // 격자 칸의 시각은 Timeline 이 정한다. 예전에는 여기서 240/(BPM*split) 을
            // 직접 써서, BPM 이 바뀌거나 4/4가 아닌 마디가 있으면 그 뒤로 전부 어긋났다.
            var timeline = EffectiveTimeline;
            var (ratioFrom, ratioTo) = ToRatioRange(range, timelineLength);

            // 앞쪽을 건너뛴다. 한 칸 여유를 둬서 경계의 선이 빠지지 않게 한다.
            var firstIndex = ratioFrom > 0
                ? Math.Max(0, (int)Math.Floor(timeline.MeasurePositionAt(ratioFrom * DurationSeconds) * split) - 1)
                : 0;
            var lastSeconds = ratioTo * DurationSeconds;

            for (var index = firstIndex; ; index++)
            {
                var seconds = timeline.SecondsAt((double)index / split);
                if (seconds > DurationSeconds)
                    yield break;

                var position = ToTimelinePosition(seconds / DurationSeconds, timelineLength);
                if (position >= -0.5 && position <= timelineLength + 0.5 && range.Contains(position))
                {
                    yield return new GridLine(
                        position,
                        ClassifyGridLine(index, split),
                        index / split,
                        seconds);
                }

                if (seconds > lastSeconds)
                    yield break;
            }
        }
        else
        {
            // 음원이 없으면 마디 높이가 곧 화면 높이다.
            var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
            var measureTimeline = EffectiveTimeline;

            var firstMeasure = 0;
            var lastMeasure = MeasureCount;
            if (!range.IsAll && rowHeight > 0)
            {
                var from = IsHorizontalView ? range.Start / rowHeight : (timelineLength - range.End) / rowHeight;
                var to = IsHorizontalView ? range.End / rowHeight : (timelineLength - range.Start) / rowHeight;
                firstMeasure = Math.Clamp((int)Math.Floor(from), 0, MeasureCount);
                lastMeasure = Math.Clamp((int)Math.Ceiling(to), 0, MeasureCount);
            }

            // 마지막 마디의 닫는 선까지 그리려고 MeasureCount 까지 돈다.
            // 범위를 벗어나는 보조선은 아래 검사에서 걸러진다.
            for (var measure = firstMeasure; measure <= lastMeasure; measure++)
            {
                for (var beat = 0; beat < split; beat++)
                {
                    var measurePosition = measure + (beat / (double)split);
                    var offset = measurePosition * rowHeight;
                    var position = IsHorizontalView ? offset : timelineLength - offset;

                    // 곱셈 순서 차이로 끝 선이 반 픽셀쯤 넘칠 수 있어 여유를 둔다.
                    if (position < -0.5 || position > timelineLength + 0.5 || !range.Contains(position))
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

    private static readonly IPen PlaybackCursorGlowPen =
        new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(90, 50, 255, 120)), 5);
    private static readonly IPen PlaybackCursorPen =
        new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(40, 255, 90)), 2);
    private static readonly IBrush GridSyncFlashFillBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(36, 80, 255, 140));
    private static readonly IPen GridSyncFlashOutlinePen =
        new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(190, 90, 255, 150)), 2);

    protected void DrawPlaybackCursor(DrawingContext context, double width, double height)
    {
        if (!IsPlaybackCursorVisible || DurationSeconds <= 0)
            return;

        var ratio = Math.Clamp(AudioRatio(PlaybackPositionSeconds), 0, 1);

        if (IsHorizontalView)
        {
            var x = ratio * width;
            context.DrawLine(PlaybackCursorGlowPen, new Point(x, 0), new Point(x, height));
            context.DrawLine(PlaybackCursorPen, new Point(x, 0), new Point(x, height));
        }
        else
        {
            var y = (1.0 - ratio) * height;
            context.DrawLine(PlaybackCursorGlowPen, new Point(0, y), new Point(width, y));
            context.DrawLine(PlaybackCursorPen, new Point(0, y), new Point(width, y));
        }
    }

    protected void DrawGridSyncFlash(DrawingContext context, double width, double height)
    {
        if (!IsGridSyncFlashVisible)
            return;

        context.FillRectangle(GridSyncFlashFillBrush, new Rect(0, 0, width, height));
        context.DrawRectangle(null, GridSyncFlashOutlinePen, new Rect(1, 1, width - 2, height - 2));
    }
}
