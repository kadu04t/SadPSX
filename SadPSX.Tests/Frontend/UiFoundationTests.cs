using SadPSX.Frontend.UI.Animation;
using SadPSX.Frontend.UI.Navigation;
using SadPSX.Frontend.UI.Screens;
using SadPSX.Frontend.UI.Theming;
using Xunit;

namespace SadPSX.Tests.Frontend;

public sealed class UiFoundationTests
{
    [Fact]
    public void FocusNavigationWrapsAndSkipsDisabledItems()
    {
        var focus = new UiFocusNavigator(4, index => index != 1);

        Assert.Equal(0, focus.SelectedIndex);
        Assert.True(focus.Move(UiAction.Down));
        Assert.Equal(2, focus.SelectedIndex);
        Assert.True(focus.Move(UiAction.Up));
        Assert.Equal(0, focus.SelectedIndex);
        Assert.True(focus.Move(UiAction.Up));
        Assert.Equal(3, focus.SelectedIndex);
    }

    [Fact]
    public void ScreenNavigationMaintainsHistory()
    {
        var screens = new UiScreenNavigator(UiScreenId.Dashboard);

        screens.Navigate(UiScreenId.Library);
        screens.Navigate(UiScreenId.GameDetails);

        Assert.True(screens.TryGoBack());
        Assert.Equal(UiScreenId.Library, screens.Current);
        Assert.True(screens.TryGoBack());
        Assert.Equal(UiScreenId.Dashboard, screens.Current);
        Assert.False(screens.TryGoBack());
    }

    [Fact]
    public void AnimationUsesElapsedTimeAndReachesItsTarget()
    {
        var animation = new AnimatedValue(0);
        animation.SetTarget(1, TimeSpan.FromMilliseconds(200));

        animation.Advance(TimeSpan.FromMilliseconds(100));

        Assert.InRange(animation.Value, 0.5f, 0.99f);
        Assert.True(animation.IsAnimating);

        animation.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(1f, animation.Value);
        Assert.False(animation.IsAnimating);
    }

    [Fact]
    public void ThemeColorsCanBeInterpolated()
    {
        UiColor result = UiColor.Lerp(
            new UiColor(0, 20, 40),
            new UiColor(100, 120, 140),
            0.5f);

        Assert.Equal(new UiColor(50, 70, 90), result);
    }
}
