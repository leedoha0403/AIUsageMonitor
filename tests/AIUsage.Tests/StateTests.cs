using AIUsage.Core;
using AIUsage.Core.Storage;

namespace AIUsage.Tests;

// Spec §11/§12: persist only feature state, with a schema version, and never Host-owned state.
public class StateTests
{
    private static AppState SampleState()
    {
        var state = Defaults.CreateState();
        state.Settings.DisplayUsageAs = "Used";
        state.Settings.FavoriteProvider = "codex";
        state.Settings.NotificationThresholds = [50, 90];
        state.Settings.HistoryRange = "7D";
        // Shell / Host-owned values that must not travel with the widget.
        state.WidgetLeft = 123;
        state.WidgetTop = 456;
        state.WidgetDockEdge = "Right";
        state.WidgetFolded = true;
        state.ChipsLeft = 7;
        state.Settings.Theme = "Dark";
        state.Settings.WidgetOpacity = 0.5;
        state.Settings.ShowTaskbarChips = false;
        state.Settings.AlwaysOnTop = false;
        return state;
    }

    [Fact]
    public void Snapshot_roundtrips_feature_settings_and_accounts()
    {
        var json = FeatureStateSnapshot.Capture(SampleState()).Serialize();
        var restored = FeatureStateSnapshot.Deserialize(FeatureStateSnapshot.CurrentVersion, json);

        Assert.NotNull(restored);
        var state = restored!.ToAppState();
        Assert.Equal("Used", state.Settings.DisplayUsageAs);
        Assert.Equal("codex", state.Settings.FavoriteProvider);
        Assert.Equal(new[] { 50, 90 }, state.Settings.NotificationThresholds);
        Assert.Equal("7D", state.Settings.HistoryRange);
        Assert.Contains("claude", state.Providers.Keys);
    }

    [Fact]
    public void Snapshot_never_contains_host_or_shell_owned_state()
    {
        var json = FeatureStateSnapshot.Capture(SampleState()).Serialize();

        foreach (var forbidden in new[] { "WidgetLeft", "WidgetTop", "WidgetDockEdge", "WidgetFolded", "ChipsLeft",
                                          "WidgetOpacity", "ShowTaskbarChips", "AlwaysOnTop", "Theme", "WindowVersion", "WidgetMode" })
        {
            Assert.DoesNotContain($"\"{forbidden}\"", json);
        }
    }

    [Fact]
    public void Restoring_does_not_bring_back_shell_values()
    {
        var restored = FeatureStateSnapshot.Deserialize(FeatureStateSnapshot.CurrentVersion, FeatureStateSnapshot.Capture(SampleState()).Serialize())!;
        var state = restored.ToAppState();
        Assert.Null(state.WidgetLeft);
        Assert.Equal("None", state.WidgetDockEdge);
        Assert.Equal(new AppSettings().Theme, state.Settings.Theme);
    }

    [Fact]
    public void State_from_a_newer_version_is_rejected_so_the_widget_starts_fresh()
    {
        var json = FeatureStateSnapshot.Capture(SampleState()).Serialize();
        Assert.Null(FeatureStateSnapshot.Deserialize(FeatureStateSnapshot.CurrentVersion + 1, json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public void Unusable_state_is_rejected_not_thrown(string json)
    {
        Assert.Null(FeatureStateSnapshot.Deserialize(FeatureStateSnapshot.CurrentVersion, json));
    }

    [Fact]
    public void Normalizer_completes_a_sparse_state()
    {
        var sparse = new AppState { Providers = new(), Settings = new AppSettings { Language = "", HistoryRange = " ", NotificationThresholds = [] } };
        var state = StateNormalizer.Normalize(sparse);

        Assert.Equal("Korean", state.Settings.Language);
        Assert.Equal("1D", state.Settings.HistoryRange);
        Assert.NotEmpty(state.Settings.NotificationThresholds);
        foreach (var id in Defaults.ProviderOrder) Assert.Contains(id, state.Providers.Keys);
    }

    [Fact]
    public void Standalone_state_file_still_loads_through_the_normalizer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aiusage-state-" + Guid.NewGuid().ToString("N"));
        var store = new StateStore(dir);
        store.SaveState(SampleState());

        var loaded = new StateStore(dir).LoadState();
        Assert.Equal("Used", loaded.Settings.DisplayUsageAs);
        Assert.Equal("Dark", loaded.Settings.Theme);          // the standalone app keeps its shell state
        Assert.Equal(123, loaded.WidgetLeft);
    }
}
