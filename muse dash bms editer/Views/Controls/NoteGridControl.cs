using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using bms_editer.Models;

namespace bms_editer.Views.Controls;

// 마디/레인 그리드를 직접 그리는 커스텀 컨트롤.
// 노트 배치·선택·드래그 편집은 다음 단계에서 확장.
public sealed class NoteGridControl : TimelineControlBase
{
    public static readonly StyledProperty<IReadOnlyList<LaneDefinition>?> LanesProperty =
        AvaloniaProperty.Register<NoteGridControl, IReadOnlyList<LaneDefinition>?>(nameof(Lanes));

    public static readonly StyledProperty<double> LaneWidthProperty =
        AvaloniaProperty.Register<NoteGridControl, double>(nameof(LaneWidth), 40.0);

    public static readonly StyledProperty<IReadOnlyList<BmsNote>?> NotesProperty =
        AvaloniaProperty.Register<NoteGridControl, IReadOnlyList<BmsNote>?>(nameof(Notes));

    public static readonly StyledProperty<bool> IsCircleNoteShapeProperty =
        AvaloniaProperty.Register<NoteGridControl, bool>(nameof(IsCircleNoteShape));

    public static readonly StyledProperty<bool> IsEditModeProperty =
        AvaloniaProperty.Register<NoteGridControl, bool>(nameof(IsEditMode));

    public static readonly StyledProperty<bool> SnapToGridProperty =
        AvaloniaProperty.Register<NoteGridControl, bool>(nameof(SnapToGrid), true);

    // 끄면 클릭한 자리에 그대로 찍는다. 잇단음처럼 격자로 표현할 수 없는 자리를
    // 손으로 잡을 때 쓴다. 예전에는 이 값이 바인딩만 되어 있고 아무도 읽지 않아서,
    // 체크를 꺼도 언제나 격자에 반올림됐다.
    public bool SnapToGrid
    {
        get => GetValue(SnapToGridProperty);
        set => SetValue(SnapToGridProperty, value);
    }

    public static readonly StyledProperty<IReadOnlyList<BmsNote>?> SelectedNotesProperty =
        AvaloniaProperty.Register<NoteGridControl, IReadOnlyList<BmsNote>?>(nameof(SelectedNotes));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> PlaceNoteCommandProperty =
        AvaloniaProperty.Register<NoteGridControl, System.Windows.Input.ICommand?>(nameof(PlaceNoteCommand));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> RemoveNoteCommandProperty =
        AvaloniaProperty.Register<NoteGridControl, System.Windows.Input.ICommand?>(nameof(RemoveNoteCommand));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> SelectNotesCommandProperty =
        AvaloniaProperty.Register<NoteGridControl, System.Windows.Input.ICommand?>(nameof(SelectNotesCommand));

    // 짝이 맞은 홀드. 머리와 꼬리를 몸통으로 이어 그린다.
    public static readonly StyledProperty<IReadOnlyList<HoldLink>?> HoldLinksProperty =
        AvaloniaProperty.Register<NoteGridControl, IReadOnlyList<HoldLink>?>(nameof(HoldLinks));

    // 짝이 어긋난 노트. 몸통 없이 경고 테두리로 표시한다.
    public static readonly StyledProperty<IReadOnlyList<BmsNote>?> HoldProblemNotesProperty =
        AvaloniaProperty.Register<NoteGridControl, IReadOnlyList<BmsNote>?>(nameof(HoldProblemNotes));

    public IReadOnlyList<HoldLink>? HoldLinks
    {
        get => GetValue(HoldLinksProperty);
        set => SetValue(HoldLinksProperty, value);
    }

    public IReadOnlyList<BmsNote>? HoldProblemNotes
    {
        get => GetValue(HoldProblemNotesProperty);
        set => SetValue(HoldProblemNotesProperty, value);
    }

    public IReadOnlyList<LaneDefinition>? Lanes
    {
        get => GetValue(LanesProperty);
        set => SetValue(LanesProperty, value);
    }

    public double LaneWidth
    {
        get => GetValue(LaneWidthProperty);
        set => SetValue(LaneWidthProperty, value);
    }

    public IReadOnlyList<BmsNote>? Notes
    {
        get => GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    public bool IsCircleNoteShape
    {
        get => GetValue(IsCircleNoteShapeProperty);
        set => SetValue(IsCircleNoteShapeProperty, value);
    }

    public bool IsEditMode
    {
        get => GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

    public IReadOnlyList<BmsNote>? SelectedNotes
    {
        get => GetValue(SelectedNotesProperty);
        set => SetValue(SelectedNotesProperty, value);
    }

    public System.Windows.Input.ICommand? PlaceNoteCommand
    {
        get => GetValue(PlaceNoteCommandProperty);
        set => SetValue(PlaceNoteCommandProperty, value);
    }

    public System.Windows.Input.ICommand? RemoveNoteCommand
    {
        get => GetValue(RemoveNoteCommandProperty);
        set => SetValue(RemoveNoteCommandProperty, value);
    }

    public System.Windows.Input.ICommand? SelectNotesCommand
    {
        get => GetValue(SelectNotesCommandProperty);
        set => SetValue(SelectNotesCommandProperty, value);
    }

    // 노트 한 개마다 새로 만들면 프레임당 수천 개가 할당된다. 색이 고정이라 나눠 쓴다.
    private static readonly Pen NoteOutlinePen = new(Brushes.Black, 1);

    private static readonly IBrush ScratchNoteBrush = new SolidColorBrush(Color.FromRgb(230, 40, 40));
    private static readonly IBrush BlackKeyNoteBrush = new SolidColorBrush(Color.FromRgb(40, 140, 230));
    private static readonly IBrush WhiteKeyNoteBrush = new SolidColorBrush(Color.FromRgb(240, 240, 240));

    // 노트 채움색도 세 가지뿐이라 나눠 쓴다. 예전에는 노트마다 새로 만들었다.
    private static IBrush GetNoteBrush(string laneId) => laneId switch
    {
        "16" => ScratchNoteBrush,                          // 스크래치는 빨강
        "12" or "14" or "18" => BlackKeyNoteBrush,         // 흑건은 파랑
        _ => WhiteKeyNoteBrush,                            // 백건은 백색
    };

    // 격자 펜과 배경도 매 프레임 새로 만들 이유가 없다.
    private static readonly IBrush GridBackgroundBrush = new SolidColorBrush(Color.FromArgb(40, 30, 60, 120));
    private static readonly IPen LanePen = new Pen(Brushes.DimGray, 1);
    private static readonly IPen SubBeatPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 150, 160, 170)), 1);
    private static readonly IPen BeatPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 190, 200, 210)), 1);
    private static readonly IPen MeasurePen = new Pen(Brushes.White, 1.5);
    private static readonly IBrush LaneHeaderBrush = new SolidColorBrush(Color.FromArgb(140, 200, 200, 200));
    private static readonly IBrush DragFillBrush = new SolidColorBrush(Color.FromArgb(60, 255, 220, 60));
    private static readonly IPen DragOutlinePen = new Pen(Brushes.Yellow, 1);
    private static readonly Typeface LaneHeaderTypeface = new("Inter, Arial, sans-serif");

    // 선택한 노트를 둘러 그리는 펜.
    //
    // 노트 자체보다 넓은 사각형이라 테두리는 노트 위가 아니라 **검은 바탕 위에** 그려진다.
    // 그래서 스크래치 레인(16)의 붉은 노트(230,40,40)와 색이 가까워도 테두리는 살아 있다.
    private static readonly Pen SelectionPen = new(Brushes.Red, 2);

    // 홀드 몸통 폭. 레인 폭 전체를 채우면 몸통 위에 겹친 다른 노트가 묻힌다.
    public const double HoldBodyWidthRatio = 0.44;

    // 짝이 어긋난 노트의 테두리. 선택 테두리(빨강 실선)와 헷갈리지 않게 주황 점선으로 둔다.
    private static readonly Pen HoldProblemPen =
        new(new ImmutableSolidColorBrush(Color.FromRgb(255, 120, 0)), 2, DashStyle.Dash);

    // 불변 브러시로 둔다. 보통 브러시는 만든 스레드만 쓸 수 있어서, 캐시가 UI 스레드 밖에서
    // 한 번이라도 채워지면 그 뒤의 렌더가 통째로 터진다(테스트에서 실제로 걸렸다).
    private static readonly Dictionary<string, IImmutableSolidColorBrush> HoldBodyBrushes =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, IPen> HoldLinkPens = new(StringComparer.OrdinalIgnoreCase);

    // 종류마다 몸통 색이 달라야 샌드백이 홀드와 섞여 보이지 않는다.
    public static Color GetHoldColor(string kind) => kind.ToUpperInvariant() switch
    {
        "SANDBAG" => Color.FromArgb(220, 255, 105, 180),
        _ => Color.FromArgb(220, 255, 190, 40),
    };

    private static IImmutableSolidColorBrush GetHoldBrush(string kind)
    {
        if (!HoldBodyBrushes.TryGetValue(kind, out var brush))
        {
            brush = new ImmutableSolidColorBrush(GetHoldColor(kind));
            HoldBodyBrushes[kind] = brush;
        }

        return brush;
    }

    private static IPen GetHoldLinkPen(string kind)
    {
        if (!HoldLinkPens.TryGetValue(kind, out var pen))
        {
            pen = new Pen(GetHoldBrush(kind), 4);
            HoldLinkPens[kind] = pen;
        }

        return pen;
    }

    static NoteGridControl()
    {
        AffectsRender<NoteGridControl>(LanesProperty, LaneWidthProperty, NotesProperty, IsCircleNoteShapeProperty,
            SelectedNotesProperty, HoldLinksProperty, HoldProblemNotesProperty);
        AffectsMeasure<NoteGridControl>(LanesProperty, LaneWidthProperty);
    }

    private Point? _dragStartPoint;
    private Point? _dragCurrentPoint;

    // 이번 드래그가 기존 선택에 더하는 것인지(Ctrl/Shift), 갈아끼우는 것인지.
    private bool _isAdditiveDrag;

    private readonly Dictionary<string, FormattedText> _laneHeaderFormattedTextCache = new();
    private HashSet<BmsNote>? _cachedSelectedSet;
    private IReadOnlyList<BmsNote>? _lastObservedSelectedNotes;

    private FormattedText GetOrCreateLaneHeaderFormattedText(string header)
    {
        if (!_laneHeaderFormattedTextCache.TryGetValue(header, out var text))
        {
            text = new FormattedText(
                header,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LaneHeaderTypeface,
                12.0,
                LaneHeaderBrush);
            _laneHeaderFormattedTextCache[header] = text;
        }
        return text;
    }

    public NoteGridControl()
    {
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMovedForSelection;
        PointerReleased += OnPointerReleased;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var laneCount = Lanes?.Count ?? 0;
        var timelineLength = GetTimelineHeight();
        var lanesTotalThickness = laneCount * LaneWidth * HorizontalZoom;

        if (IsHorizontalView)
        {
            return new Size(timelineLength, lanesTotalThickness);
        }
        else
        {
            return new Size(lanesTotalThickness, timelineLength);
        }
    }

    public override void Render(DrawingContext context)
    {
        var lanes = Lanes;
        if (lanes is null || lanes.Count == 0)
            return;

        var laneThickness = LaneWidth * HorizontalZoom;
        var totalLanesThickness = lanes.Count * laneThickness;
        var timelineLength = GetTimelineHeight();

        var totalWidth = IsHorizontalView ? timelineLength : totalLanesThickness;
        var totalHeight = IsHorizontalView ? totalLanesThickness : timelineLength;

        context.FillRectangle(Brushes.Black, new Rect(0, 0, totalWidth, totalHeight));
        context.FillRectangle(GridBackgroundBrush, new Rect(0, 0, totalWidth, totalHeight));


        var thicknessOffset = 0.0;
        for (var i = 0; i <= lanes.Count; i++)
        {
            if (IsHorizontalView)
            {
                context.DrawLine(LanePen, new Point(0, thicknessOffset), new Point(totalWidth, thicknessOffset));
            }
            else
            {
                context.DrawLine(LanePen, new Point(thicknessOffset, 0), new Point(thicknessOffset, totalHeight));
            }

            if (i < lanes.Count)
                thicknessOffset += laneThickness;
        }

        foreach (var line in EnumerateGridLines(timelineLength))
        {
            var pen = line.Kind switch
            {
                GridLineKind.Measure => MeasurePen,
                GridLineKind.Beat => BeatPen,
                _ => SubBeatPen,
            };

            if (IsHorizontalView)
            {
                context.DrawLine(pen, new Point(line.Position, 0), new Point(line.Position, totalHeight));
            }
            else
            {
                context.DrawLine(pen, new Point(0, line.Position), new Point(totalWidth, line.Position));
            }
        }

        // 래인 번호(채널 번호) 텍스트 그리기
        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            var formattedText = GetOrCreateLaneHeaderFormattedText(lane.Header);

            if (IsHorizontalView)
            {
                var laneY = (i * laneThickness) + (laneThickness - formattedText.Height) / 2;
                context.DrawText(formattedText, new Point(8, laneY));
            }
            else
            {
                var laneX = (i * laneThickness) + (laneThickness - formattedText.Width) / 2;
                context.DrawText(formattedText, new Point(laneX, totalHeight - formattedText.Height - 8));
            }
        }

        // 홀드 몸통은 노트보다 먼저 그린다. 머리·꼬리 노트가 몸통 위에 올라와야 한다.
        DrawHoldBodies(context, lanes, laneThickness, timelineLength);

        // 배치된 노트 그리기
        var notes = Notes;
        var selectedNotes = SelectedNotes;
        if (!ReferenceEquals(selectedNotes, _lastObservedSelectedNotes))
        {
            _lastObservedSelectedNotes = selectedNotes;
            _cachedSelectedSet = selectedNotes is { Count: > 0 } ? new HashSet<BmsNote>(selectedNotes) : null;
        }
        var selectedSet = _cachedSelectedSet;
        if (notes is not null)
        {
            for (var index = 0; index < notes.Count; index++)
            {
                var note = notes[index];

                var laneIndex = FindLaneIndex(lanes, note.LaneId);
                if (laneIndex == -1) continue;

                var noteTPos = ComputeNoteTPos(note, timelineLength);
                var noteBrush = GetNoteBrush(note.LaneId);
                var laneOffset = laneIndex * laneThickness;
                var blackPen = NoteOutlinePen;

                if (IsHorizontalView)
                {
                    if (IsCircleNoteShape)
                    {
                        var radius = Math.Min(7.5, (laneThickness - 4) / 2);
                        var center = new Point(noteTPos, laneOffset + laneThickness / 2);
                        context.DrawEllipse(noteBrush, blackPen, center, radius, radius);
                    }
                    else
                    {
                        var rect = new Rect(noteTPos - 3, laneOffset + 2, 6, laneThickness - 4);
                        context.FillRectangle(noteBrush, rect);
                        context.DrawRectangle(null, blackPen, rect);
                    }
                }
                else
                {
                    if (IsCircleNoteShape)
                    {
                        var radius = Math.Min(7.5, (laneThickness - 4) / 2);
                        var center = new Point(laneOffset + laneThickness / 2, noteTPos);
                        context.DrawEllipse(noteBrush, blackPen, center, radius, radius);
                    }
                    else
                    {
                        var rect = new Rect(laneOffset + 2, noteTPos - 3, laneThickness - 4, 6);
                        context.FillRectangle(noteBrush, rect);
                        context.DrawRectangle(null, blackPen, rect);
                    }
                }

                if (selectedSet is not null && selectedSet.Contains(note))
                {
                    var highlightPen = SelectionPen;
                    var highlightRect = IsHorizontalView
                        ? new Rect(noteTPos - 7, laneOffset + 1, 14, laneThickness - 2)
                        : new Rect(laneOffset + 1, noteTPos - 7, laneThickness - 2, 14);
                    context.DrawRectangle(null, highlightPen, highlightRect);
                }
            }
        }

        DrawHoldProblems(context, lanes, laneThickness, timelineLength);

        if (_dragStartPoint is { } dragStart && _dragCurrentPoint is { } dragEnd)
        {
            var selectionRect = NormalizedRect(dragStart, dragEnd);
            context.FillRectangle(DragFillBrush, selectionRect);
            context.DrawRectangle(null, DragOutlinePen, selectionRect);
        }

        DrawGridSyncFlash(context, totalWidth, totalHeight);
        DrawPlaybackCursor(context, totalWidth, totalHeight);
    }

    // 홀드 몸통.
    //
    // 양 끝 좌표를 각각 ComputeNoteTPos 로 구한다. 노트와 같은 시간축을 써야 몸통 끝이 끝 노트와 만난다.
    // 마디 단위로 선형 보간해 늘리면 BPM 변화나 변박 구간을 지나는 홀드의 끝이 끝 노트와 어긋난다.
    private void DrawHoldBodies(DrawingContext context, IReadOnlyList<LaneDefinition> lanes, double laneThickness, double timelineLength)
    {
        var links = HoldLinks;
        if (links is null || links.Count == 0)
            return;

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            var headLane = FindLaneIndex(lanes, link.Head.LaneId);
            var tailLane = FindLaneIndex(lanes, link.Tail.LaneId);

            // 뮤즈 대시는 채널 안에서만 짝지으므로 두 레인은 같다. 다른 프로파일을 붙였을 때
            // 몸통이 조용히 사라지지 않도록, 레인이 갈리면 선으로 이어 둔다.
            if (headLane == -1 || tailLane == -1)
                continue;

            var headPos = ComputeNoteTPos(link.Head, timelineLength);
            var tailPos = ComputeNoteTPos(link.Tail, timelineLength);

            if (headLane == tailLane)
            {
                var body = ComputeHoldBodyRect(headLane * laneThickness, laneThickness, headPos, tailPos, IsHorizontalView);
                context.FillRectangle(GetHoldBrush(link.Kind), body);
            }
            else
            {
                context.DrawLine(
                    GetHoldLinkPen(link.Kind),
                    LaneCenter(headLane, headPos, laneThickness),
                    LaneCenter(tailLane, tailPos, laneThickness));
            }
        }
    }

    private void DrawHoldProblems(DrawingContext context, IReadOnlyList<LaneDefinition> lanes, double laneThickness, double timelineLength)
    {
        var problems = HoldProblemNotes;
        if (problems is null || problems.Count == 0)
            return;

        for (var i = 0; i < problems.Count; i++)
        {
            var note = problems[i];
            var laneIndex = FindLaneIndex(lanes, note.LaneId);
            if (laneIndex == -1)
                continue;

            var tPos = ComputeNoteTPos(note, timelineLength);
            var laneOffset = laneIndex * laneThickness;
            var rect = IsHorizontalView
                ? new Rect(tPos - 10, laneOffset - 1, 20, laneThickness + 2)
                : new Rect(laneOffset - 1, tPos - 10, laneThickness + 2, 20);

            context.DrawRectangle(null, HoldProblemPen, rect);
        }
    }

    // 홀드 몸통 사각형. 레인 가운데에 HoldBodyWidthRatio 폭으로, 머리에서 꼬리까지.
    public static Rect ComputeHoldBodyRect(double laneOffset, double laneThickness, double headPos, double tailPos, bool isHorizontalView)
    {
        var width = Math.Max(4.0, laneThickness * HoldBodyWidthRatio);
        var inset = (laneThickness - width) / 2;
        var start = Math.Min(headPos, tailPos);
        var length = Math.Abs(tailPos - headPos);

        return isHorizontalView
            ? new Rect(start, laneOffset + inset, length, width)
            : new Rect(laneOffset + inset, start, width, length);
    }

    private Point LaneCenter(int laneIndex, double tPos, double laneThickness) =>
        IsHorizontalView
            ? new Point(tPos, laneIndex * laneThickness + laneThickness / 2)
            : new Point(laneIndex * laneThickness + laneThickness / 2, tPos);

    private double ComputeNoteTPos(BmsNote note, double timelineLength)
    {
        if (DurationSeconds > 0 && Bpm > 0)
        {
            // 노트의 시각도 격자와 같은 곳에서 구해야 둘이 어긋나지 않는다.
            var seconds = EffectiveTimeline.SecondsAt(note.Measure + note.Position);
            return ToTimelinePosition(seconds / DurationSeconds, timelineLength);
        }

        var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
        var totalOffset = (note.Measure + note.Position) * rowHeight;
        return IsHorizontalView ? totalOffset : (timelineLength - totalOffset);
    }

    private static int FindLaneIndex(IReadOnlyList<LaneDefinition> lanes, string laneId)
    {
        for (var j = 0; j < lanes.Count; j++)
        {
            if (lanes[j].Id == laneId)
                return j;
        }
        return -1;
    }

    private static Rect NormalizedRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        Focus();

        var point = e.GetCurrentPoint(this);
        var lanes = Lanes;
        if (lanes is null || lanes.Count == 0 || Bpm <= 0)
            return;

        // Ctrl/Shift 를 누른 채 끌면 **편집 모드에서도** 범위 선택이 된다.
        //
        // 예전에는 모드로만 갈려서, 찍고 -> 고르고 -> 방향키로 옮기려면 매번 ✏️ 토글을
        // 왕복해야 했다. 게다가 토글을 누르는 순간 포커스가 격자를 떠나 방향키도 안 먹었다.
        var additive = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);

        if (!IsEditMode || additive)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                _dragStartPoint = point.Position;
                _dragCurrentPoint = point.Position;
                _isAdditiveDrag = additive;
                e.Pointer.Capture(this);
                InvalidateVisual();
            }
            return;
        }

        var laneThickness = LaneWidth * HorizontalZoom;
        var timelineLength = GetTimelineHeight();

        var thicknessPos = IsHorizontalView ? point.Position.Y : point.Position.X;
        var clickedLaneIndex = (int)(thicknessPos / laneThickness);
        if (clickedLaneIndex < 0 || clickedLaneIndex >= lanes.Count)
            return;

        var clickedLaneId = lanes[clickedLaneIndex].Id;

        var tPos = IsHorizontalView ? point.Position.X : point.Position.Y;
        var split = Math.Max(1, BeatSplit);

        int measure = 0;
        double position = 0.0;

        var ratio = Math.Clamp(IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength)), 0.0, 1.0);

        // 클릭한 자리를 마디 위치로 되돌린다. 그리는 쪽과 같은 시간축을 쓴다.
        var clickedMeasurePosition = DurationSeconds > 0
            ? EffectiveTimeline.MeasurePositionAt(ratio * DurationSeconds)
            : ratio * MeasureCount;

        if (SnapToGrid)
        {
            var totalStepIndex = (int)Math.Round(clickedMeasurePosition * split);

            // 맨 끝을 클릭하면 반올림이 마디 경계를 딱 넘어서 measure == MeasureCount 가 된다.
            // 예전에는 그대로 거부해서 **곡 마지막 격자 칸에는 노트를 찍을 수 없었다.**
            // 거부하는 대신 마지막 칸으로 당겨준다.
            totalStepIndex = Math.Clamp(totalStepIndex, 0, (MeasureCount * split) - 1);

            measure = totalStepIndex / split;
            position = (double)(totalStepIndex % split) / split;
        }
        else
        {
            var clamped = Math.Clamp(clickedMeasurePosition, 0, MeasureCount - (1.0 / split));
            measure = (int)Math.Floor(clamped);
            position = clamped - measure;
        }

        if (measure < 0 || measure >= MeasureCount)
            return;

        var args = new NotePlacementArgs(clickedLaneId, measure, position);

        if (point.Properties.IsLeftButtonPressed)
        {
            if (PlaceNoteCommand?.CanExecute(args) == true)
            {
                PlaceNoteCommand.Execute(args);
                InvalidateVisual();
                e.Handled = true;
            }
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            if (RemoveNoteCommand?.CanExecute(args) == true)
            {
                RemoveNoteCommand.Execute(args);
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    private void OnPointerMovedForSelection(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (_dragStartPoint is null)
            return;

        _dragCurrentPoint = e.GetCurrentPoint(this).Position;
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_dragStartPoint is not { } dragStart || _dragCurrentPoint is not { } dragEnd)
            return;

        _dragStartPoint = null;
        _dragCurrentPoint = null;
        e.Pointer.Capture(null);

        var lanes = Lanes;
        var notes = Notes;
        if (lanes is null || lanes.Count == 0 || notes is null)
        {
            InvalidateVisual();
            return;
        }

        var laneThickness = LaneWidth * HorizontalZoom;
        var timelineLength = GetTimelineHeight();
        var selectionRect = NormalizedRect(dragStart, dragEnd).Inflate(2);

        var selected = new List<BmsNote>();
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var laneIndex = FindLaneIndex(lanes, note.LaneId);
            if (laneIndex == -1) continue;

            var noteTPos = ComputeNoteTPos(note, timelineLength);
            var laneOffset = laneIndex * laneThickness;
            var noteCenter = IsHorizontalView
                ? new Point(noteTPos, laneOffset + laneThickness / 2)
                : new Point(laneOffset + laneThickness / 2, noteTPos);

            if (selectionRect.Contains(noteCenter))
                selected.Add(note);
        }

        var args = new NoteSelectionArgs(selected, _isAdditiveDrag);
        _isAdditiveDrag = false;

        if (SelectNotesCommand?.CanExecute(args) == true)
        {
            SelectNotesCommand.Execute(args);
        }

        InvalidateVisual();
    }

}
