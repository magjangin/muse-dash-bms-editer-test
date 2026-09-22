using System;
using System.IO;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 곡 폴더에서 차트·음원·영상을 고르는 규칙.
// 폴더 열기와 건반 BMS 가져오기가 같이 쓴다. 한때 메인 창 코드 안에만 있어서 테스트가 없었다.
public sealed class FolderMediaTests : IDisposable
{
    private readonly string _root;

    public FolderMediaTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private string Folder(string name, params string[] files)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(folder, file), "");

        return folder;
    }

    [Fact]
    public void 폴더_이름과_같은_파일을_먼저_고른다()
    {
        // 이름순으로는 a_preview.ogg 가 먼저지만, 폴더와 이름이 같은 쪽이 그 곡의 본 파일이다.
        var folder = Folder("rise", "a_preview.ogg", "rise.ogg");

        var found = FolderMedia.FindBestFile(folder, FolderMedia.AudioExtensions);

        Assert.Equal("rise.ogg", Path.GetFileName(found));
    }

    [Fact]
    public void 같은_이름이_없으면_이름순_첫_번째를_고른다()
    {
        var folder = Folder("zero two", "music.ogg", "b.ogg");

        var found = FolderMedia.FindBestFile(folder, FolderMedia.AudioExtensions);

        Assert.Equal("b.ogg", Path.GetFileName(found));
    }

    [Fact]
    public void 음원은_OGG_만_고른다()
    {
        // 실제 곡 폴더에는 music.mp3 와 music.ogg 가 같이 있다. 재생은 OGG 만 풀 수 있다.
        var folder = Folder("song", "music.mp3", "music.xmp");

        Assert.Null(FolderMedia.FindBestFile(folder, FolderMedia.AudioExtensions));
    }

    [Fact]
    public void 폴더가_없으면_null()
    {
        Assert.Null(FolderMedia.FindBestFile(Path.Combine(_root, "없는 폴더"), FolderMedia.ChartExtensions));
    }
}
