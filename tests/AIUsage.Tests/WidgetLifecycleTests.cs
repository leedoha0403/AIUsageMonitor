using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsage.Presentation.ViewModels;
using AIUsage.Presentation.Views;
using AIUsage.Widget;
using Dora.Widget.Abstractions;
using Dora.Widget.Runtime;

namespace AIUsage.Tests;

// Drives the widget the way the Host runtime does: create, restore, views, save, shutdown.
public class WidgetLifecycleTests
{
    internal static string Dump(DependencyObject d, int depth = 0)
    {
        var sb = new System.Text.StringBuilder();
        if (d is FrameworkElement fe) sb.AppendLine($"{new string(' ', depth * 2)}{fe.GetType().Name} {fe.Name} vis={fe.Visibility} desired={fe.DesiredSize}");
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) sb.Append(Dump(VisualTreeHelper.GetChild(d, i), depth + 1));
        return sb.ToString();
    }

    private static WidgetRuntime NewRuntime(IWidgetStateStore store, Func<WidgetManifest, IWidgetPermissionService>? permissions = null)
    {
        var registry = new WidgetRegistry();
        var names = (Standalone: @"Local\AIUsageTest-S-" + Guid.NewGuid().ToString("N"), Widget: @"Local\AIUsageTest-W-" + Guid.NewGuid().ToString("N"));
        registry.Register(() => new AIUsageWidget(names.Standalone, names.Widget));
        return new WidgetRuntime(registry, new WidgetStateMachine(new ManualTimerScheduler()), store, permissions: permissions);
    }

    [Fact]
    public async Task Summary_and_detail_surfaces_share_one_view_model()
    {
        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(new InMemoryWidgetStateStore());
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);

            var summary = (HostSummaryView)runtime.GetSummaryView(instance);
            var detail = (UsageDetailView)runtime.CreateDetailView(instance)!;

            Assert.IsType<UsageFeatureViewModel>(summary.DataContext);
            Assert.Same(summary.DataContext, detail.DataContext);   // spec §9

            await runtime.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Summary_view_follows_the_mode_the_host_selects()
    {
        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(new InMemoryWidgetStateStore());
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var summary = (HostSummaryView)runtime.GetSummaryView(instance);

            foreach (var (host, view) in new[]
                     {
                         (WidgetDisplayMode.Collapsed, UsageDisplayMode.Collapsed),
                         (WidgetDisplayMode.Compact, UsageDisplayMode.Compact),
                         (WidgetDisplayMode.Natural, UsageDisplayMode.Natural)
                     })
            {
                summary.OnDisplayModeChanged(host);
                Assert.Equal(view, summary.DisplayMode);
                summary.Measure(new System.Windows.Size(600, 600));
                Assert.True(summary.DesiredSize.Width > 0 && summary.DesiredSize.Height > 0, $"{host} renders");
            }

            await runtime.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Rendered_sizes_stay_within_the_declared_layout_profile()
    {
        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(new InMemoryWidgetStateStore());
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var layout = instance.Manifest.Layout;
            var summary = (HostSummaryView)runtime.GetSummaryView(instance);

            foreach (var mode in Enum.GetValues<WidgetDisplayMode>())
            {
                summary.OnDisplayModeChanged(mode);
                summary.InvalidateMeasure();   // a view outside a live layout tree does not re-measure by itself
                var preferred = layout.PreferredSize(mode);
                // The Host gives the widget its preferred width; the content must fit the minimum height.
                summary.Measure(new System.Windows.Size(preferred.Width, double.PositiveInfinity));
                summary.Arrange(new System.Windows.Rect(0, 0, preferred.Width, Math.Max(summary.DesiredSize.Height, 1)));
                summary.UpdateLayout();
                var minimum = layout.MinimumSize(mode);
                Assert.True(summary.DesiredSize.Width <= preferred.Width + 0.5, $"{mode} width {summary.DesiredSize.Width} > {preferred.Width}");
                Assert.True(summary.DesiredSize.Height >= minimum.Height, $"{mode} content is implausibly small: {summary.DesiredSize} {Dump(summary)}");
            }

            await runtime.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Detail_view_offers_feature_settings_but_not_app_level_ones()
    {
        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(new InMemoryWidgetStateStore());
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var detail = (UsageDetailView)runtime.CreateDetailView(instance)!;

            var tabs = (TabControl)detail.FindName("DetailTabs");
            Assert.Equal(7, tabs.Items.Count);   // overview, history, accounts, snapshot, diagnostics, refresh, settings
            var settings = (TabItem)tabs.Items[6]!;
            Assert.IsType<FeatureSettingsView>(settings.Content);

            await runtime.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Feature_state_survives_a_restart_through_the_host_store()
    {
        var store = new InMemoryWidgetStateStore();
        string instanceId = "";

        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(store);
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            instanceId = instance.State.InstanceId;
            var vm = (UsageFeatureViewModel)((HostSummaryView)runtime.GetSummaryView(instance)).DataContext;
            vm.DisplayUsageAs = "Used";
            vm.HistoryRange = "7D";
            await runtime.SaveStateAsync(instance);
            await runtime.ShutdownAsync();
        });

        var saved = store.Load(instanceId);
        Assert.NotNull(saved);
        Assert.Equal(AIUsageWidgetManifest.StateVersion, saved!.StateVersion);
        Assert.DoesNotContain("WidgetLeft", saved.Json);

        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(store);
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId, instanceId);
            var vm = (UsageFeatureViewModel)((HostSummaryView)runtime.GetSummaryView(instance)).DataContext;
            Assert.Equal("Used", vm.DisplayUsageAs);
            Assert.Equal("7D", vm.HistoryRange);
            await runtime.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Saving_before_any_view_exists_keeps_the_restored_state()
    {
        var store = new InMemoryWidgetStateStore();
        await WpfHost.Run(async () =>
        {
            var first = NewRuntime(store);
            var a = await first.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId, "inst-1");
            ((UsageFeatureViewModel)((HostSummaryView)first.GetSummaryView(a)).DataContext).DisplayUsageAs = "Used";
            await first.SaveStateAsync(a);
            await first.ShutdownAsync();

            var second = NewRuntime(store);
            var b = await second.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId, "inst-1");
            await second.SaveStateAsync(b);                 // no view was ever created
            await second.ShutdownAsync();
        });

        var kept = store.Load("inst-1");
        Assert.Contains("Used", kept!.Json);
    }

    [Fact]
    public async Task Single_instance_is_enforced_by_the_runtime()
    {
        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(new InMemoryWidgetStateStore());
            await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId));
            await runtime.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Without_granted_capabilities_the_widget_does_not_collect()
    {
        await WpfHost.Run(async () =>
        {
            // Host default: nothing granted, no prompt.
            var runtime = NewRuntime(new InMemoryWidgetStateStore());
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var vm = (UsageFeatureViewModel)((HostSummaryView)runtime.GetSummaryView(instance)).DataContext;

            Assert.False(vm.CanCollect);
            Assert.True(vm.HasCollectionNotice);
            var before = vm.IsRefreshing;
            await vm.RefreshAsync(force: true);
            Assert.Equal(before, vm.IsRefreshing);

            await runtime.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Granting_capabilities_enables_collection()
    {
        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(new InMemoryWidgetStateStore(),
                m => new WidgetPermissionService(m.Capabilities, WidgetCapabilities.FileSystem | WidgetCapabilities.Network));
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var vm = (UsageFeatureViewModel)((HostSummaryView)runtime.GetSummaryView(instance)).DataContext;

            Assert.False(vm.IsCollectionSuspended);   // isolated mutex names: nobody else is collecting
            Assert.True(vm.CanCollect);
            Assert.False(vm.HasCollectionNotice);

            await runtime.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Shutdown_stops_the_view_model()
    {
        await WpfHost.Run(async () =>
        {
            var runtime = NewRuntime(new InMemoryWidgetStateStore());
            var instance = await runtime.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            runtime.GetSummaryView(instance);
            await runtime.RemoveInstanceAsync(instance.State.InstanceId, deleteState: true);
            Assert.Empty(runtime.Instances);
        });
    }

    [Fact]
    public async Task A_removed_instance_hands_collection_ownership_to_the_next_one()
    {
        var standalone = @"Local\AIUsageTest-S-" + Guid.NewGuid().ToString("N");
        var widget = @"Local\AIUsageTest-W-" + Guid.NewGuid().ToString("N");
        WidgetRuntime Runtime()
        {
            var registry = new WidgetRegistry();
            registry.Register(() => new AIUsageWidget(standalone, widget));
            return new WidgetRuntime(registry, new WidgetStateMachine(new ManualTimerScheduler()), new InMemoryWidgetStateStore());
        }

        await WpfHost.Run(async () =>
        {
            var first = Runtime();
            var a = await first.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var vmA = (UsageFeatureViewModel)((HostSummaryView)first.GetSummaryView(a)).DataContext;
            Assert.False(vmA.IsCollectionSuspended);

            // A second widget process cannot collect while the first owns it.
            var second = Runtime();
            var b = await second.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var vmB = (UsageFeatureViewModel)((HostSummaryView)second.GetSummaryView(b)).DataContext;
            Assert.True(vmB.IsCollectionSuspended);
            await second.ShutdownAsync();

            await first.RemoveInstanceAsync(a.State.InstanceId, deleteState: true);
            var third = Runtime();
            var c = await third.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var vmC = (UsageFeatureViewModel)((HostSummaryView)third.GetSummaryView(c)).DataContext;
            Assert.False(vmC.IsCollectionSuspended);
            await third.ShutdownAsync();
        });
    }

    [Fact]
    public async Task Scheduled_refresh_needs_process_execution_on_top_of_collection_access()
    {
        await WpfHost.Run(async () =>
        {
            var noProcess = NewRuntime(new InMemoryWidgetStateStore(),
                m => new WidgetPermissionService(m.Capabilities, WidgetCapabilities.FileSystem | WidgetCapabilities.Network));
            var a = await noProcess.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var vmA = (UsageFeatureViewModel)((HostSummaryView)noProcess.GetSummaryView(a)).DataContext;
            Assert.True(vmA.CanCollect);
            Assert.False(vmA.CanRunScheduledRefresh);
            await noProcess.ShutdownAsync();

            var all = NewRuntime(new InMemoryWidgetStateStore(),
                m => new WidgetPermissionService(m.Capabilities, WidgetCapabilities.FileSystem | WidgetCapabilities.Network | WidgetCapabilities.ProcessExecution));
            var b = await all.CreateInstanceAsync(AIUsageWidgetManifest.WidgetId);
            var vmB = (UsageFeatureViewModel)((HostSummaryView)all.GetSummaryView(b)).DataContext;
            Assert.True(vmB.CanRunScheduledRefresh);
            await all.ShutdownAsync();
        });
    }
}
