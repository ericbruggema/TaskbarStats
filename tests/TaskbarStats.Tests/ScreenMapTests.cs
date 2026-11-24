using Xunit;

namespace TaskbarStats.Tests;

/// <summary>Schermwissel (bijv. inloggen via VNC: eerst 1024x768, dan 1280x1024): het venster moet op zijn plek mee verhuizen.</summary>
public class ScreenMapTests
{
    private static ScreenInfo Scr(int w, int h, int left = 0, int top = 0, string name = @"\\.\DISPLAY1", bool primary = true, int taskbar = 40)
        => new(name, primary, new Rectangle(left, top, w, h), new Rectangle(left, top, w, h - taskbar));

    private static readonly Size Widget = new(300, 38);

    [Fact]
    public void WidgetBesideTheTray_StaysBottomRight_WhenTheResolutionGrows()
    {
        var small = new[] { Scr(1024, 768) };
        var big = new[] { Scr(1280, 1024) };
        var win = new Rectangle(1024 - 220 - Widget.Width, 768 - 40 + 1, Widget.Width, Widget.Height);   // naast het systeemvak
        var p = ScreenMap.Remap(win, small, big, workArea: false);
        Assert.Equal(1280 - 220 - Widget.Width, p.X);          // zelfde afstand tot de rechterrand
        Assert.Equal(1024 - (768 - win.Bottom) - Widget.Height, p.Y);   // zelfde afstand tot de onderrand
    }

    [Fact]
    public void ItComesBack_WhenTheResolutionGoesBackToTheOriginal()
    {
        var a = new[] { Scr(1920, 1080) };
        var b = new[] { Scr(1024, 768) };
        var start = new Rectangle(1400, 1042, Widget.Width, Widget.Height);
        var onSmall = ScreenMap.Remap(start, a, b, workArea: false);
        var back = ScreenMap.Remap(new Rectangle(onSmall, Widget), b, a, workArea: false);
        Assert.Equal(start.Location, back);
    }

    [Fact]
    public void LeftAnchoredWindow_KeepsItsDistanceToTheLeftEdge()
    {
        var p = ScreenMap.Remap(new Rectangle(60, 100, 300, 200), new[] { Scr(1024, 768) }, new[] { Scr(1920, 1080) }, workArea: true);
        Assert.Equal(60, p.X);
        Assert.Equal(100, p.Y);
    }

    [Fact]
    public void ShrinkingTheScreen_KeepsTheWindowInsideTheScreen()
    {
        var p = ScreenMap.Remap(new Rectangle(1500, 900, 300, 38), new[] { Scr(1920, 1080) }, new[] { Scr(800, 600) }, workArea: false);
        Assert.InRange(p.X, 0, 800 - 300);
        Assert.InRange(p.Y, 0, 600 - 38);
    }

    [Fact]
    public void UnchangedScreens_LeaveAVisibleWindowAlone()
    {
        var s = new[] { Scr(1920, 1080) };
        var win = new Rectangle(1234, 1040, 300, 38);
        Assert.Equal(win.Location, ScreenMap.Remap(win, s, s, workArea: false));
    }

    [Fact]
    public void WindowOutsideEveryScreen_IsPulledBackIn_EvenIfTheScreensDidNotChange()
    {
        var s = new[] { Scr(1024, 768) };
        var p = ScreenMap.Remap(new Rectangle(3000, 2000, 300, 38), s, s, workArea: false);
        Assert.InRange(p.X, 0, 1024 - 300);
        Assert.InRange(p.Y, 0, 768 - 38);
    }

    [Fact]
    public void SecondMonitorGone_MovesTheWindowToThePrimaryScreen()
    {
        var before = new[] { Scr(1920, 1080), Scr(1920, 1080, left: 1920, name: @"\\.\DISPLAY2", primary: false) };
        var after = new[] { Scr(1920, 1080) };
        var p = ScreenMap.Remap(new Rectangle(1920 + 1500, 1042, 300, 38), before, after, workArea: false);
        Assert.InRange(p.X, 0, 1920 - 300);
        Assert.InRange(p.Y, 0, 1080 - 38);
    }

    [Fact]
    public void SecondMonitor_KeepsItsWindowOnThatMonitor_WhenOnlyItsResolutionChanges()
    {
        var before = new[] { Scr(1920, 1080), Scr(1280, 1024, left: 1920, name: @"\\.\DISPLAY2", primary: false) };
        var after = new[] { Scr(1920, 1080), Scr(1024, 768, left: 1920, name: @"\\.\DISPLAY2", primary: false) };
        var p = ScreenMap.Remap(new Rectangle(1920 + 1280 - 20 - 300, 1024 - 40, 300, 38), before, after, workArea: false);
        Assert.Equal(1920 + 1024 - 20 - 300, p.X);   // nog steeds 20 px van de rechterrand van de tweede monitor
    }

    [Fact]
    public void Same_NoticesAChangedResolution_ButNotAnIdenticalList()
    {
        Assert.True(ScreenMap.Same(new[] { Scr(1024, 768) }, new[] { Scr(1024, 768) }));
        Assert.False(ScreenMap.Same(new[] { Scr(1024, 768) }, new[] { Scr(1280, 1024) }));
        Assert.False(ScreenMap.Same(new[] { Scr(1024, 768) }, new[] { Scr(1024, 768), Scr(800, 600, left: 1024, name: "X", primary: false) }));
    }

    [Fact]
    public void Capture_ReturnsTheRealScreens()
    {
        var s = ScreenMap.Capture();
        Assert.NotEmpty(s);
        Assert.Contains(s, x => x.Primary);
        Assert.All(s, x => Assert.True(x.Bounds.Width > 0 && x.Bounds.Height > 0));
    }
}
