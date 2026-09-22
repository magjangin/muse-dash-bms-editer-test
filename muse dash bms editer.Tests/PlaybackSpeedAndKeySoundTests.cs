using System;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

public sealed class PlaybackSpeedAndKeySoundTests
{
    [Fact]
    public void 기본_재생배속은_1이며_키음은_활성화되어_있다()
    {
        using var vm = new MainWindowViewModel();

        Assert.Equal(1.0, vm.PlaybackSpeed);
        Assert.True(vm.IsKeySoundEnabled);
        Assert.Contains("켜짐", vm.KeySoundToggleText);
    }

    [Fact]
    public void 키음_토글_명령이_상태와_텍스트를_정상적으로_바꾼다()
    {
        using var vm = new MainWindowViewModel();

        vm.ToggleKeySoundCommand.Execute(null);
        Assert.False(vm.IsKeySoundEnabled);
        Assert.Contains("끄기", vm.KeySoundToggleText);

        vm.ToggleKeySoundCommand.Execute(null);
        Assert.True(vm.IsKeySoundEnabled);
        Assert.Contains("켜짐", vm.KeySoundToggleText);
    }

    [Fact]
    public void 재생배속이_범위_내에서_변경된다()
    {
        using var vm = new MainWindowViewModel();

        vm.PlaybackSpeed = 0.5;
        Assert.Equal(0.5, vm.PlaybackSpeed);

        vm.PlaybackSpeed = 0.1;
        Assert.Equal(0.1, vm.PlaybackSpeed);
    }
}
