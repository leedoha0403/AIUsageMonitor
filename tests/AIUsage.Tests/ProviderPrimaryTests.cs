using AIUsage.Core;
using AIUsage.Presentation.ViewModels;

namespace AIUsage.Tests;

// When the long window (weekly) is used up, the headline must not keep showing the 5H window's leftover.
public class ProviderPrimaryTests
{
    private static ProviderViewModel Create(int sessionUsed, int weeklyUsed)
    {
        var state = Defaults.CreateState().Providers.Values.First(p => p.Capabilities.SessionUsage);
        state.SessionUsagePercent = sessionUsed;
        state.WeeklyUsagePercent = weeklyUsed;
        state.SessionResetAt = DateTimeOffset.Now.AddHours(4);
        state.WeeklyResetAt = DateTimeOffset.Now.AddDays(3);
        return new ProviderViewModel(state, 0);
    }

    [Fact]
    public void Session_window_is_primary_while_weekly_has_room()
    {
        var vm = Create(sessionUsed: 20, weeklyUsed: 90);
        Assert.Equal(20, vm.PrimaryPercent);
    }

    [Fact]
    public void Exhausted_weekly_becomes_primary_even_if_session_is_fresh()
    {
        var vm = Create(sessionUsed: 0, weeklyUsed: 100);
        Assert.Equal(100, vm.PrimaryPercent);
        Assert.Equal(vm.WeeklyUsagePercent, vm.PrimaryPercent);
        Assert.Equal(vm.LongWindowLabel, vm.PrimaryLabel);
        Assert.Equal(Loc.T("pv.left", 0), vm.RemainingLine);
    }
}

public class DockCountdownTests
{
    [Fact]
    public void Shows_days_then_hours_and_minutes()
    {
        var text = AIUsage.Core.Formatters.DockCountdown(DateTimeOffset.Now.AddDays(3).AddHours(3).AddMinutes(35).AddSeconds(30));
        Assert.Equal("3d 03:35", text);
    }

    [Fact]
    public void Under_a_day_keeps_the_clock_format()
    {
        var text = AIUsage.Core.Formatters.DockCountdown(DateTimeOffset.Now.AddHours(4).AddMinutes(51).AddSeconds(30));
        Assert.Matches(@"^04:51:\d\d$", text);
    }
}

public class ChipCountdownTests
{
    [Fact]
    public void Shows_days_then_hours_and_minutes()
    {
        var text = AIUsage.Core.Formatters.ShortCountdown(DateTimeOffset.Now.AddDays(3).AddHours(3).AddMinutes(35).AddSeconds(30));
        Assert.Equal("3d 03:35", text);
    }
}
