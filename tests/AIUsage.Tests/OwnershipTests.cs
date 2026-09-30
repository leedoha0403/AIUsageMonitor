using AIUsage.Core;

namespace AIUsage.Tests;

// "One collector at a time" (design §9-3). Each test uses its own mutex names so tests and the real app never clash.
public class OwnershipTests
{
    private static (string Standalone, string Widget) Names() =>
        (@"Local\AIUsageTest-Standalone-" + Guid.NewGuid().ToString("N"), @"Local\AIUsageTest-Widget-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Widget_collects_when_nobody_else_is_running()
    {
        var (standalone, widget) = Names();
        using var ownership = new CollectionOwnership(false, standalone, widget);

        Assert.True(ownership.Evaluate());   // changed: false -> true
        Assert.True(ownership.MayCollect);
        Assert.False(ownership.Evaluate());  // steady
    }

    [Fact]
    public void Widget_yields_while_the_standalone_app_runs_and_resumes_after()
    {
        var (standalone, widget) = Names();
        using var ownership = new CollectionOwnership(false, standalone, widget);
        ownership.Evaluate();
        Assert.True(ownership.MayCollect);

        var standaloneMutex = new Mutex(true, standalone);
        Assert.True(ownership.Evaluate());
        Assert.False(ownership.MayCollect);

        standaloneMutex.ReleaseMutex();
        standaloneMutex.Dispose();
        Assert.True(ownership.Evaluate());
        Assert.True(ownership.MayCollect);
    }

    [Fact]
    public void A_second_widget_process_does_not_collect()
    {
        var (standalone, widget) = Names();
        using var first = new CollectionOwnership(false, standalone, widget);
        first.Evaluate();

        // Mutex ownership is per thread, so the "other process" runs on its own thread.
        var secondMayCollect = true;
        var thread = new Thread(() =>
        {
            using var second = new CollectionOwnership(false, standalone, widget);
            second.Evaluate();
            secondMayCollect = second.MayCollect;
        });
        thread.Start();
        thread.Join();

        Assert.True(first.MayCollect);
        Assert.False(secondMayCollect);
    }

    [Fact]
    public void Standalone_always_collects()
    {
        var (standalone, widget) = Names();
        using var ownership = new CollectionOwnership(true, standalone, widget);
        ownership.Evaluate();
        Assert.True(ownership.MayCollect);
    }

    [Fact]
    public void Releasing_lets_the_next_widget_take_over()
    {
        var (standalone, widget) = Names();
        var first = new CollectionOwnership(false, standalone, widget);
        first.Evaluate();
        first.Dispose();

        using var second = new CollectionOwnership(false, standalone, widget);
        second.Evaluate();
        Assert.True(second.MayCollect);
    }
}
