using AIUsage.Widget;
using Dora.Widget.Abstractions;
using Dora.Widget.Runtime;

namespace AIUsage.Tests;

// Spec §21 (minimum compliance) checked against the real manifest and the Host's own validator.
public class ContractTests
{
    private static readonly WidgetManifest Manifest = AIUsageWidgetManifest.Create();

    [Fact]
    public void Manifest_passes_host_validation_without_errors_or_warnings()
    {
        var issues = ManifestValidator.Validate(Manifest);
        Assert.Empty(issues);
    }

    [Fact]
    public void Manifest_id_is_reverse_domain_and_not_the_display_name()
    {
        Assert.Equal("dev.leedoha.aiusage.summary", Manifest.Id);
        Assert.NotEqual(Manifest.Name, Manifest.Id);
    }

    [Fact]
    public void Manifest_declares_all_three_modes_with_minimums_not_above_preferred()
    {
        foreach (var mode in Enum.GetValues<WidgetDisplayMode>())
        {
            var preferred = Manifest.Layout.PreferredSize(mode);
            var minimum = Manifest.Layout.MinimumSize(mode);
            Assert.True(minimum.FitsIn(preferred), $"{mode}: min must fit in preferred");
        }
        Assert.True(Manifest.Layout.CollapsedSize.Height <= Manifest.Layout.CompactSize.Height);
        Assert.True(Manifest.Layout.CompactSize.Height <= Manifest.Layout.NaturalSize.Height);
    }

    [Fact]
    public void Manifest_declares_exactly_the_capabilities_the_feature_uses()
    {
        var expected = WidgetCapabilities.FileSystem | WidgetCapabilities.Network |
                       WidgetCapabilities.ProcessExecution | WidgetCapabilities.Notifications;
        Assert.Equal(expected, Manifest.Capabilities);
    }

    [Fact]
    public void Manifest_flags_match_what_is_implemented()
    {
        Assert.False(Manifest.AllowMultipleInstances);
        Assert.True(Manifest.SupportsDetailView);
        Assert.True(Manifest.SupportsFloating);
    }

    [Fact]
    public void Registry_accepts_the_widget()
    {
        var registry = new WidgetRegistry();
        var issues = registry.Register(() => new AIUsageWidget());
        Assert.Empty(issues);
        Assert.True(registry.TryGetManifest(AIUsageWidgetManifest.WidgetId, out _));
    }

    [Theory]
    [InlineData(WidgetDisplayMode.Collapsed, 0)]
    [InlineData(WidgetDisplayMode.Compact, 1)]
    [InlineData(WidgetDisplayMode.Natural, 2)]
    public void Host_display_modes_map_to_view_modes(WidgetDisplayMode host, int view)
    {
        Assert.Equal(view, (int)HostSummaryView.Map(host));
    }

    [Fact]
    public void Widget_has_a_parameterless_constructor_for_the_host_plugin_loader()
    {
        Assert.NotNull(typeof(AIUsageWidget).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void Host_plugin_loader_registers_the_widget_from_its_output_folder()
    {
        var folder = Path.GetDirectoryName(typeof(AIUsageWidget).Assembly.Location)!;
        var registry = new WidgetRegistry();
        var handlers = new List<IWidgetDetachHandler>();
        var result = WidgetAssemblyLoader.LoadInto(registry, folder, handlers);

        Assert.Empty(result.Errors);
        Assert.True(registry.TryGetManifest(AIUsageWidgetManifest.WidgetId, out _), "the loader must find and register AIUsage.Widget.dll");
        var handler = Assert.Single(handlers);
        Assert.Equal(AIUsageWidgetManifest.WidgetId, handler.WidgetId);   // the hand-over handler is found beside the widget
    }
}
