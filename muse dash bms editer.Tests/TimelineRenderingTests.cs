using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using bms_editer.Views.Controls;
using Xunit;

namespace bms_editer.Tests;

// 재생 중 프레임을 떨어뜨리던 두 가지(전체 다시 그리기, 전체 그리기)를 고친 자리.
//
// 뷰포트 컬링은 "스크롤하면 빈 화면"이 나기 쉬운 기능이고, 겹 컨트롤은 "커서가 안 보이게" 되기 쉬운
// 기능이다. 둘 다 숫자만 봐서는 안 잡혀서 실제 창을 그려 픽셀로 본다.
public sealed class TimelineRenderingTests
{
    private sealed class LineProbe : TimelineControlBase
    {
        public List<(double Position, string Kind, int Measure, double Seconds)> Lines(double length) =>
            EnumerateGridLines(length).Select(l => (l.Position, l.Kind.ToString(), l.Measure, l.Seconds)).ToList();

        public List<(double Position, string Kind, int Measure, double Seconds)> Lines(double length, double start, double end) =>
            EnumerateGridLines(length, new TimelineRange(start, end)).Select(l => (l.Position, l.Kind.ToString(), l.Measure, l.Seconds)).ToList();
    }

    private sealed class CountingProbe : TimelineControlBase
    {
        public int RenderCount { get; private set; }

        public (bool IsAll, double Start, double End) LastRange { get; private set; }

        protected override Size MeasureOverride(Size availableSize) => new(200, GetTimelineHeight());

        public override void Render(DrawingContext context)
        {
            var range = BeginTimelineRender();
            LastRange = (range.IsAll, range.Start, range.End);
            RenderCount++;
            context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        }
    }

    private static ChartTimeline ChangingTimeline() =>
        new(120, new Dictionary<int, double> { [1] = 0.75, [3] = 1.5 }, new[] { new BpmChange(2, 0.5, 240) });

    // 건너뛰는 계산이 틀리면 화면 경계의 선이 조용히 빠진다. 전부 센 것과 똑같아야 한다.
    [Theory]
    [InlineData(true, 0.0)]
    [InlineData(false, 0.0)]
    [InlineData(true, 12.0)]
    [InlineData(false, 12.0)]
    public void 범위를_준_격자선은_전체에서_그_범위만_고른_것과_같다(bool horizontal, double durationSeconds)
    {
        var probe = new LineProbe
        {
            DurationSeconds = durationSeconds,
            Bpm = 120,
            MeasureCount = 16,
            BeatSplit = 16,
            GridMeasure = 4,
            RowHeight = 16,
            VerticalZoom = 1,
            IsHorizontalView = horizontal,
            Timeline = ChangingTimeline(),
        };

        const double length = 1024.0;
        var all = probe.Lines(length);
        Assert.NotEmpty(all);

        foreach (var (start, end) in new[] { (0.0, 100.0), (300.0, 420.0), (900.0, 1024.0), (-50.0, 40.0), (1000.0, 1400.0) })
        {
            var expected = all.Where(l => l.Position >= start && l.Position <= end).ToList();
            Assert.Equal(expected, probe.Lines(length, start, end));
        }
    }

    private static T InWindow<T>(Control content, int width, int height, Func<Window, T> body)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            return body(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static Color PixelAt(WriteableBitmap frame, int x, int y)
    {
        using var locked = frame.Lock();
        var offset = (y * locked.RowBytes) + (x * 4);
        var b = Marshal.ReadByte(locked.Address, offset);
        var g = Marshal.ReadByte(locked.Address, offset + 1);
        var r = Marshal.ReadByte(locked.Address, offset + 2);
        return Color.FromRgb(r, g, b);
    }

    private static bool LooksLikeCursor(Color c) => c.G > 180 && c.R < 140 && c.B < 170;

    // 기록과 화면 반영이 틱 하나씩 어긋날 수 있어 두 번 돌린다.
    private static void Pump()
    {
        for (var i = 0; i < 2; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // 조건이 맞는 화면이 나올 때까지 틱을 더 돌린다. 헤드리스 렌더는 틱 하나로 반영되지 않을 때가 있다.
    // 진짜로 안 그려지는 버그라면 몇 번을 돌려도 조건이 맞지 않아 그대로 실패한다.
    private static WriteableBitmap FrameWhere(Window window, Func<WriteableBitmap, bool> ready)
    {
        WriteableBitmap? frame = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Pump();

            // 아직 한 장도 안 그려졌으면 null 이 온다. 첫 프레임은 렌더 스레드를 조금 기다려야 나올 때가 있다.
            if (window.CaptureRenderedFrame() is not { } captured)
            {
                System.Threading.Thread.Sleep(20);
                continue;
            }

            frame = captured;
            if (ready(frame))
                break;
        }

        return frame ?? throw new InvalidOperationException("창이 한 번도 그려지지 않았다");
    }

    [Fact]
    public void 재생_커서가_움직여도_격자는_다시_그리지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var probe = new CountingProbe { DurationSeconds = 10, Bpm = 120, MeasureCount = 8, RowHeight = 16, VerticalZoom = 1, IsPlaybackCursorVisible = true };

        InWindow(probe, 300, 500, _ =>
        {
            Pump();
            var before = probe.RenderCount;
            Assert.True(before > 0, "처음에는 한 번 그려야 한다");

            for (var i = 0; i < 10; i++)
            {
                probe.PlaybackPositionSeconds = i * 0.5;
                Pump();
            }

            Assert.Equal(before, probe.RenderCount);

            // 격자가 정말 바뀌는 값은 여전히 다시 그려야 한다.
            probe.BeatSplit = 8;
            Pump();
            Assert.True(probe.RenderCount > before, "격자 분할을 바꾸면 다시 그려야 한다");
            return true;
        });
    });

    // 커서를 격자에서 떼어 낸 뒤에도 화면에는 그대로 그려져야 한다.
    [Fact]
    public void 재생_커서가_겹_컨트롤에서_실제로_그려지고_따라_움직인다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var grid = new NoteGridControl
        {
            Lanes = LaneDefinition.CreateDefault(),
            Notes = Array.Empty<BmsNote>(),
            DurationSeconds = 10,
            Bpm = 120,
            MeasureCount = 8,
            RowHeight = 16,
            VerticalZoom = 1,
            BeatSplit = 16,
            GridMeasure = 4,
            LaneWidth = 40,
            IsPlaybackCursorVisible = true,
            PlaybackPositionSeconds = 5,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        InWindow(grid, 400, 500, window =>
        {
            // 타임라인 길이 = 10초 × 16 × 1 × 4 / 2 = 320px. 세로 뷰는 아래가 0초.
            var first = FrameWhere(window, f => LooksLikeCursor(PixelAt(f, 20, 160)));
            Assert.True(LooksLikeCursor(PixelAt(first, 20, 160)), $"5초 자리에 커서가 없다: {PixelAt(first, 20, 160)}");

            grid.PlaybackPositionSeconds = 2.5;
            var moved = FrameWhere(window, f => LooksLikeCursor(PixelAt(f, 20, 240)) && !LooksLikeCursor(PixelAt(f, 20, 160)));
            Assert.True(LooksLikeCursor(PixelAt(moved, 20, 240)), $"2.5초 자리로 안 옮겨졌다: {PixelAt(moved, 20, 240)}");
            Assert.False(LooksLikeCursor(PixelAt(moved, 20, 160)), "옛 자리에 커서가 남았다");
            return true;
        });
    });

    // 예전 컬링이 "불안정"해서 되돌려진 이유가 이것이다. 스크롤해도 Render 가 다시 불리지 않아 빈 화면이 남았다.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 멀리_스크롤해도_노트와_격자가_빈칸이_되지_않는다(bool horizontal) => HoldTestSupport.RunOnUiThread(() =>
    {
        // 레인 13(네 번째 칸) 150마디. 한 마디 64px, 전체 200마디.
        var note = new BmsNote { LaneId = "13", Measure = 150, Position = 0, WavKey = "01" };
        var grid = new NoteGridControl
        {
            Lanes = LaneDefinition.CreateDefault(),
            Notes = new[] { note },
            MeasureCount = 200,
            RowHeight = 16,
            VerticalZoom = 1,
            BeatSplit = 16,
            GridMeasure = 4,
            Bpm = 120,
            LaneWidth = 40,
            IsHorizontalView = horizontal,
        };

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = grid,
        };

        InWindow(scroller, 600, 500, window =>
        {
            Pump();

            // 노트 좌표: 가로 뷰 x = 150×64 = 9600, 세로 뷰 y = 12800 − 9600 = 3200.
            const double along = 200.0;
            scroller.Offset = horizontal ? new Vector(9600 - along, 0) : new Vector(0, 3200 - along);

            Color At(WriteableBitmap f) => horizontal ? PixelAt(f, (int)along, 140) : PixelAt(f, 140, (int)along);
            static bool IsNote(Color c) => c.R > 200 && c.G > 200 && c.B > 200;

            var pixel = At(FrameWhere(window, f => IsNote(At(f))));
            Assert.True(IsNote(pixel), $"스크롤한 자리의 노트가 안 그려졌다: {pixel}");
            return true;
        });
    });

    // EffectiveViewportChanged 는 값이 바뀔 때만 온다. 첫 스크롤 전까지 곡 전체를 그리는 일이 없게 잠근다.
    [Fact]
    public void 스크롤_뷰어_안에서는_첫_렌더부터_보이는_구간만_그린다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var probe = new CountingProbe { DurationSeconds = 0, Bpm = 120, MeasureCount = 400, RowHeight = 16, VerticalZoom = 1 };
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = probe,
        };

        InWindow(scroller, 300, 400, _ =>
        {
            Pump();
            Assert.False(probe.LastRange.IsAll, "첫 렌더가 곡 전체를 그렸다");
            Assert.True(probe.LastRange.End < 2000, $"보이는 구간보다 훨씬 많이 그렸다: {probe.LastRange}");

            scroller.Offset = new Vector(0, 6000);
            Pump();
            Assert.True(probe.LastRange.Start <= 6000 && probe.LastRange.End >= 6300,
                $"스크롤한 자리를 그리지 않았다: {probe.LastRange}");
            return true;
        });
    });

    // 어셈블리 이름은 배포 프로필마다 다르고 공백도 들어 있다. 이름을 코드에 박으면 배포본에서만 조용히 실패한다.
    [Theory]
    [InlineData("icon_personal.ico")]
    [InlineData("icon_release.ico")]
    public void 창_아이콘은_실행_중인_어셈블리_이름으로_찾는다(string iconFile) => HoldTestSupport.RunOnUiThread(() =>
    {
        var assemblyName = typeof(bms_editer.MainWindow).Assembly.GetName().Name;

        Assert.True(Avalonia.Platform.AssetLoader.Exists(new Uri($"avares://{assemblyName}/Assets/{iconFile}")));
    });

    [Fact]
    public void 스페이스는_포커스가_버튼에_있어도_버튼을_다시_누르지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new bms_editer.MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var editToggle = window.GetVisualDescendants()
            .OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .First(b => Equals(b.Content, "✏️"));
        editToggle.Focus();
        vm.IsEditMode = true;

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsEditMode, "스페이스가 편집 모드 버튼을 또 눌렀다");
        window.Close();
    });
}

// 뷰모델·서비스 쪽 회귀 테스트.
public sealed class PlaybackAndEditingRegressionTests : IDisposable
{
    private readonly string _directory;

    public PlaybackAndEditingRegressionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private MainWindowViewModel Loaded(string body)
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, "#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n#WAV02 b.wav\r\n" + body, new UTF8Encoding(false));

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);
        return vm;
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.9999, 0.99995)]
    [InlineData(2.0, 1.0)]
    [InlineData(3.0, 1.5)]
    [InlineData(4000.0, 2000.0)]
    public void 마디_위치_되찾기는_마디_경계에서도_맞는다(double seconds, double expected)
    {
        Assert.Equal(expected, ChartTimeline.Uniform(120).MeasurePositionAt(seconds), 9);
    }

    [Fact]
    public void 시각과_마디_위치는_변속이_있어도_서로_되돌아간다()
    {
        var timeline = new ChartTimeline(
            150,
            new Dictionary<int, double> { [2] = 0.5, [40] = 1.25 },
            new[] { new BpmChange(5, 0.25, 300), new BpmChange(80, 0.0, 75) });

        for (var position = 0.0; position < 200.0; position += 0.37)
            Assert.Equal(position, timeline.MeasurePositionAt(timeline.SecondsAt(position)), 6);
    }

    [Fact]
    public void int_범위를_넘는_마디_번호가_있어도_파일은_열린다()
    {
        var vm = Loaded("#00111:0102\r\n#99999999999911:0102\r\n");

        Assert.Equal(2, vm.Chart.Notes.Count);
        Assert.Contains(vm.Chart.PreservedLines, line => line.Text.StartsWith("#99999999999911", StringComparison.Ordinal));
    }

    [Fact]
    public void 같은_칸을_같은_키음으로_다시_찍어도_기록이_쌓이지_않는다()
    {
        var vm = Loaded("#00113:01\r\n");
        vm.SelectedWavItem = vm.WavList[0];
        var note = vm.Chart.Notes[0];

        vm.PlaceNoteCommand.Execute(new NotePlacementArgs(note.LaneId, note.Measure, note.Position));

        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
        Assert.Single(vm.Chart.Notes);
    }

    [Fact]
    public void 마디_복제를_되돌리면_복제_전에_고른_노트가_돌아온다()
    {
        var vm = Loaded("#00113:0102\r\n");
        vm.SetNoteSelection(new[] { vm.Chart.Notes[1] });

        vm.CopyNotesByMeasureOffset(new[] { vm.Chart.Notes[1] }, 2);
        Assert.Equal(3, vm.Chart.Notes.Count);

        vm.UndoCommand.Execute(null);

        Assert.Equal(2, vm.Chart.Notes.Count);
        var restored = Assert.Single(vm.SelectedNotes);
        Assert.Same(vm.Chart.Notes[1], restored);
    }

    [Fact]
    public void 일괄_삭제는_고른_노트만_지우고_선택에서도_뺀다()
    {
        var vm = Loaded("#00113:01020102\r\n");
        var doomed = new[] { vm.Chart.Notes[0], vm.Chart.Notes[2] };
        var kept = vm.Chart.Notes[1];
        vm.SetNoteSelection(new[] { doomed[0], kept });

        Assert.Equal(2, vm.DeleteNotes(doomed));

        Assert.Equal(2, vm.Chart.Notes.Count);
        Assert.DoesNotContain(doomed[0], vm.Chart.Notes);
        Assert.Same(kept, Assert.Single(vm.SelectedNotes));
    }

    [Fact]
    public void 키음은_재생을_부른_쪽을_기다리게_하지_않고_뒤에서_읽힌다()
    {
        var path = Path.Combine(_directory, "late.wav");
        WriteSilentWav(path, frames: 2000);

        using var player = new KeySoundPlayer();
        player.Play(path);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (player.GetOrLoad(path) is null && DateTime.UtcNow < deadline)
            System.Threading.Thread.Sleep(10);

        Assert.NotNull(player.GetOrLoad(path));
    }

    [Fact]
    public void 없는_키음을_재생해도_예외가_나지_않고_나중에_파일을_넣으면_읽힌다()
    {
        var path = Path.Combine(_directory, "missing.wav");
        using var player = new KeySoundPlayer();

        player.Play(path);
        player.Play(path);
        Assert.Null(player.GetOrLoad(path));

        WriteSilentWav(path, frames: 100);
        Assert.NotNull(player.GetOrLoad(path));
    }

    [Fact]
    public void 오류_기록은_쓴_파일의_경로를_돌려준다()
    {
        var path = CrashLog.Write(new InvalidOperationException("시험용 오류"), "Test");

        Assert.NotNull(path);
        Assert.Contains("시험용 오류", File.ReadAllText(path!));
        File.Delete(path!);
    }

    private static void WriteSilentWav(string path, int frames)
    {
        using var writer = new BinaryWriter(File.Create(path));
        var dataSize = frames * 4;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(44100);
        writer.Write(44100 * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }
}
