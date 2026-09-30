using AIUsage.Presentation.ViewModels;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using AIUsage.Core;
using AIUsage.Core.Storage;
using AIUsage.Presentation;
using UsageMonitorWpf.Shell;

namespace UsageMonitorWpf.ViewModels;

// Standalone-app shell on top of the shared feature view model: window layout/density, theme, tray/chips
// options, start-at-login and self-update. None of this belongs to a Host-embedded widget.
public sealed class MainViewModel : UsageFeatureViewModel
{
    private const int SettingsTabIndex = 6;

    private string _windowVersion;
    private string _theme;
    private bool _alwaysOnTop;
    private string _presetName = "";
    private bool _isCheckingUpdate;
    private bool _updateAvailable;
    private bool _isDownloadingUpdate;
    private double _updateDownloadProgress;
    private string _updateStatusText = "";
    private string _latestUpdateVersion = "";
    private string _latestReleaseUrl = "";
    private UpdateAsset? _updateZipAsset;
    private UpdateAsset? _updateExeAsset;
    private UpdateAsset? _updateChecksumsAsset;

    public MainViewModel(IUsageStore store, IUiServices ui) : base(store, ui)
    {
        CustomThemePresets = new ObservableCollection<CustomThemePreset>(State.Settings.CustomThemePresets);
        _windowVersion = State.Settings.WindowVersion;
        _theme = State.Settings.Theme;
        _alwaysOnTop = State.Settings.AlwaysOnTop;

        OpenLicenseCommand = new RelayCommand(() => ShowLegalDocument("LICENSE", Loc.T("ui.license"), Loc.T("ui.licenseDialogDesc")));
        OpenOpenSourceLicensesCommand = new RelayCommand(() => ShowLegalDocument("THIRD-PARTY-NOTICES.md", Loc.T("ui.openSourceLicenses"), Loc.T("ui.thirdPartyDialogDesc")));
        OpenRepositoryCommand = new RelayCommand(() => OpenUrl(RepositoryUrl));
        ReportIssueCommand = new RelayCommand(() => OpenUrl(IssueUrl));
        CheckForUpdateCommand = new RelayCommand(() => _ = CheckForUpdateAsync(manual: true));
        DownloadUpdateCommand = new RelayCommand(() => _ = DownloadUpdateAsync());
        OpenReleaseNotesCommand = new RelayCommand(() => OpenUrl(_latestReleaseUrl));
        ResetCustomThemeCommand = new RelayCommand(ResetCustomTheme);
        SaveThemePresetCommand = new RelayCommand(SaveThemePreset);
        ApplyThemePresetCommand = new ParamCommand(p => { if (p is CustomThemePreset preset) ApplyThemePreset(preset); });
        DeleteThemePresetCommand = new ParamCommand(p => { if (p is CustomThemePreset preset) DeleteThemePreset(preset); });

        // Update check waits a bit longer so it never competes with startup usage collection.
        var updateTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        updateTick.Tick += (_, _) => { updateTick.Stop(); _ = CheckForUpdateAsync(manual: false); };
        updateTick.Start();
        ThemeService.Apply(Theme, State.Settings.CustomTheme);
        // The base constructor built the diagnostics before this shell's mode was known.
        RefreshTextViews();
    }

    public event Action? ExitRequested;

    public override string InternalVersion { get; } = typeof(MainViewModel).Assembly
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? "0.0.0-internal";

    protected override string ModeDescription => $"{Loc.Term("opt.", WindowVersion)} / {Loc.Term("opt.", WidgetMode)}";

    protected override void OnShellLanguageChanged()
    {
        foreach (var option in new[] { WindowVersions, Themes, OnOffOptions }.SelectMany(x => x))
        {
            option.Refresh();
        }
    }

    public ObservableCollection<CustomThemePreset> CustomThemePresets { get; }
    public IReadOnlyList<OptionItem> WindowVersions { get; } = Options("Mini", "Expanded");
    public IReadOnlyList<OptionItem> Themes { get; } = Options("System", "Light", "Dark", "Custom");
    public IReadOnlyList<OptionItem> OnOffOptions { get; } = [new OptionItem("On", () => Loc.T("ui.on")), new OptionItem("Off", () => Loc.T("ui.off"))];

    public ICommand ResetCustomThemeCommand { get; }
    public ICommand SaveThemePresetCommand { get; }
    public ICommand ApplyThemePresetCommand { get; }
    public ICommand DeleteThemePresetCommand { get; }
    public ICommand OpenLicenseCommand { get; }
    public ICommand OpenOpenSourceLicensesCommand { get; }
    public ICommand OpenRepositoryCommand { get; }
    public ICommand ReportIssueCommand { get; }
    public ICommand CheckForUpdateCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand OpenReleaseNotesCommand { get; }

    public string RepositoryUrl => "https://github.com/leedoha0403/AIUsageMonitor";
    public string IssueUrl => "https://github.com/leedoha0403/AIUsageMonitor/issues";

    public bool IsMiniVersion => WindowVersion == "Mini";
    public bool IsExpandedVersion => WindowVersion == "Expanded";

    private static void ShowLegalDocument(string fileName, string title, string subtitle)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        var body = File.Exists(path) ? File.ReadAllText(path) : Loc.T("ui.legalFileMissing", fileName);
        var window = new UsageMonitorWpf.LegalTextWindow(title, subtitle, body)
        {
            Owner = System.Windows.Application.Current.MainWindow?.IsVisible == true ? System.Windows.Application.Current.MainWindow : null
        };
        window.ShowDialog();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, Loc.T("ui.appName"));
        }
    }

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (IsCheckingUpdate || IsDownloadingUpdate) return;
        IsCheckingUpdate = true;
        if (manual) UpdateStatusText = Loc.T("ui.checkingUpdate");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var release = await UpdateChecker.FetchLatestAsync(cts.Token);
            if (release is null)
            {
                if (manual) UpdateStatusText = Loc.T("ui.updateCheckFailed");
                return;
            }

            var currentVersion = InternalVersion.Replace("-internal", "", StringComparison.OrdinalIgnoreCase);
            if (!UpdateChecker.IsNewer(release.TagName, currentVersion))
            {
                UpdateAvailable = false;
                if (manual) UpdateStatusText = Loc.T("ui.upToDate");
                return;
            }

            _updateZipAsset = release.FindZip();
            _updateExeAsset = release.FindExe();
            _updateChecksumsAsset = release.FindChecksums();
            _latestReleaseUrl = release.HtmlUrl;
            LatestUpdateVersion = release.TagName;
            OnPropertyChanged(nameof(UpdateAvailableText));

            if (_updateZipAsset is null && _updateExeAsset is null)
            {
                UpdateAvailable = false;
                if (manual) UpdateStatusText = Loc.T("ui.updateNoAsset");
                return;
            }

            UpdateAvailable = true;
            UpdateStatusText = "";
            if (!manual)
            {
                RaiseNotification(
                    Loc.T("mv.updateAvailableTitle", release.TagName),
                    Loc.T("mv.updateAvailableBody"),
                    () => SelectedTabIndex = SettingsTabIndex);
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async Task DownloadUpdateAsync()
    {
        if (_updateExeAsset is not null && _updateChecksumsAsset is not null && SelfUpdater.CanReplaceInPlace())
        {
            await InstallUpdateAsync(_updateExeAsset, _updateChecksumsAsset);
            return;
        }

        if (_updateZipAsset is null || IsDownloadingUpdate) return;
        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateStatusText = "";
        try
        {
            var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var progress = new Progress<double>(p => UpdateDownloadProgress = p);
            var path = await UpdateChecker.DownloadAssetAsync(_updateZipAsset, destination, progress, cts.Token);

            if (_updateChecksumsAsset is not null)
            {
                var sums = await UpdateChecker.FetchTextAsync(_updateChecksumsAsset.BrowserDownloadUrl, cts.Token);
                if (sums is not null && !UpdateChecker.VerifyChecksum(sums, _updateZipAsset.Name, path))
                {
                    File.Delete(path);
                    UpdateStatusText = Loc.T("ui.updateChecksumFailed");
                    return;
                }
            }

            UpdateStatusText = Loc.T("ui.updateDownloaded");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateStatusText = Loc.T("ui.updateDownloadFailed", ex.Message);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    // Downloads the standalone exe, verifies it against SHA256SUMS.txt (entry required), then hands over to
    // SelfUpdater and exits so the helper can swap the file and restart the app.
    private async Task InstallUpdateAsync(UpdateAsset exeAsset, UpdateAsset checksumsAsset)
    {
        if (IsDownloadingUpdate) return;
        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateStatusText = "";
        var exit = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var progress = new Progress<double>(p => UpdateDownloadProgress = p);
            var path = await UpdateChecker.DownloadAssetAsync(exeAsset, SelfUpdater.UpdateDirectory, progress, cts.Token);

            var sums = await UpdateChecker.FetchTextAsync(checksumsAsset.BrowserDownloadUrl, cts.Token);
            if (sums is null || !UpdateChecker.VerifyChecksum(sums, exeAsset.Name, path, requireEntry: true))
            {
                File.Delete(path);
                UpdateStatusText = Loc.T("ui.updateChecksumFailed");
                return;
            }

            UpdateStatusText = Loc.T("ui.updateInstalling");
            if (!SelfUpdater.LaunchHelper(path))
            {
                UpdateStatusText = Loc.T("ui.updateDownloadFailed", "updater launch failed");
                return;
            }
            exit = true;
        }
        catch (Exception ex)
        {
            UpdateStatusText = Loc.T("ui.updateDownloadFailed", ex.Message);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
        if (exit) ExitRequested?.Invoke();
    }

    public void SaveWindowPlacement(double left, double top)
    {
        State.WindowLeft = left;
        State.WindowTop = top;
        SaveStateOnly();
    }

    public void SaveWidgetPlacement(double left, double top, string dockEdge, bool folded)
    {
        State.WidgetLeft = left;
        State.WidgetTop = top;
        State.WidgetDockEdge = dockEdge;
        State.WidgetFolded = folded;
        SaveStateOnly();
    }

    private void SetCustomThemeColor(string? value, Action<string> set, [CallerMemberName] string? propertyName = null)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        set(normalized);
        OnPropertyChanged(propertyName);
        if (IsCustomTheme) ThemeService.Apply(Theme, State.Settings.CustomTheme);
        SaveStateOnly();
    }

    private void SaveThemePreset()
    {
        var name = PresetName.Trim();
        if (name.Length == 0) return;

        var existing = State.Settings.CustomThemePresets.FirstOrDefault(p => p.Name == name);
        if (existing != null)
        {
            existing.Theme = State.Settings.CustomTheme.Clone();
        }
        else
        {
            var preset = new CustomThemePreset { Name = name, Theme = State.Settings.CustomTheme.Clone() };
            State.Settings.CustomThemePresets.Add(preset);
            CustomThemePresets.Add(preset);
        }

        PresetName = "";
        SaveStateOnly();
    }

    private void ApplyThemePreset(CustomThemePreset preset) => ApplyCustomTheme(preset.Theme.Clone());

    private void ResetCustomTheme() => ApplyCustomTheme(new CustomThemeSettings());

    private void ApplyCustomTheme(CustomThemeSettings theme)
    {
        State.Settings.CustomTheme = theme;
        OnPropertyChanged(nameof(CustomInk));
        OnPropertyChanged(nameof(CustomMuted));
        OnPropertyChanged(nameof(CustomAccent));
        OnPropertyChanged(nameof(CustomPanel));
        OnPropertyChanged(nameof(CustomCanvas));
        OnPropertyChanged(nameof(CustomLine));
        OnPropertyChanged(nameof(CustomHeader));
        OnPropertyChanged(nameof(CustomHeaderStart));
        OnPropertyChanged(nameof(CustomHeaderMiddle));
        OnPropertyChanged(nameof(CustomHeaderEnd));
        if (IsCustomTheme) ThemeService.Apply(Theme, State.Settings.CustomTheme);
        SaveStateOnly();
    }

    private void DeleteThemePreset(CustomThemePreset preset)
    {
        State.Settings.CustomThemePresets.RemoveAll(p => p.Name == preset.Name);
        CustomThemePresets.Remove(preset);
        SaveStateOnly();
    }

    public bool IsCheckingUpdate { get => _isCheckingUpdate; private set => Set(ref _isCheckingUpdate, value); }
    public bool UpdateAvailable { get => _updateAvailable; private set => Set(ref _updateAvailable, value); }
    public bool IsDownloadingUpdate { get => _isDownloadingUpdate; private set => Set(ref _isDownloadingUpdate, value); }
    public string UpdateStatusText { get => _updateStatusText; private set => Set(ref _updateStatusText, value); }
    public string LatestUpdateVersion { get => _latestUpdateVersion; private set => Set(ref _latestUpdateVersion, value); }
    public string UpdateAvailableText => Loc.T("ui.updateAvailable", LatestUpdateVersion);

    public double UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        private set { Set(ref _updateDownloadProgress, value); OnPropertyChanged(nameof(UpdateDownloadProgressText)); }
    }
    public string UpdateDownloadProgressText => Loc.T("ui.downloadingUpdate", (int)Math.Round(UpdateDownloadProgress * 100));

    public string WindowVersion
    {
        get => _windowVersion;
        set
        {
            if (value != null && Set(ref _windowVersion, value))
            {
                State.Settings.WindowVersion = value;
                SaveStateOnly();
                OnPropertyChanged(nameof(IsMiniVersion));
                OnPropertyChanged(nameof(IsExpandedVersion));
            }
        }
    }

    public string Theme
    {
        get => _theme;
        set
        {
            if (value != null && Set(ref _theme, value))
            {
                State.Settings.Theme = value;
                ThemeService.Apply(value, State.Settings.CustomTheme);
                SaveStateOnly();
                OnPropertyChanged(nameof(IsCustomTheme));
            }
        }
    }

    public bool IsCustomTheme => Theme == "Custom";

    public string CustomInk
    {
        get => State.Settings.CustomTheme.Ink;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.Ink = v);
    }

    public string CustomMuted
    {
        get => State.Settings.CustomTheme.Muted;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.Muted = v);
    }

    public string CustomAccent
    {
        get => State.Settings.CustomTheme.Accent;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.Accent = v);
    }

    public string CustomPanel
    {
        get => State.Settings.CustomTheme.Panel;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.Panel = v);
    }

    public string CustomCanvas
    {
        get => State.Settings.CustomTheme.Canvas;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.Canvas = v);
    }

    public string CustomLine
    {
        get => State.Settings.CustomTheme.Line;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.Line = v);
    }

    public string CustomHeader
    {
        get => State.Settings.CustomTheme.Header;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.Header = v);
    }

    public string CustomHeaderStart
    {
        get => State.Settings.CustomTheme.HeaderStart;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.HeaderStart = v);
    }

    public string CustomHeaderMiddle
    {
        get => State.Settings.CustomTheme.HeaderMiddle;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.HeaderMiddle = v);
    }

    public string CustomHeaderEnd
    {
        get => State.Settings.CustomTheme.HeaderEnd;
        set => SetCustomThemeColor(value, v => State.Settings.CustomTheme.HeaderEnd = v);
    }

    public string PresetName
    {
        get => _presetName;
        set => Set(ref _presetName, value ?? "");
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set { if (Set(ref _alwaysOnTop, value)) { State.Settings.AlwaysOnTop = value; SaveStateOnly(); } }
    }

    public double WidgetOpacity
    {
        get => State.Settings.WidgetOpacity;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, 0.2, 1.0), 2);
            if (Math.Abs(State.Settings.WidgetOpacity - clamped) < 0.001) return;
            State.Settings.WidgetOpacity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WidgetOpacityText));
            SaveStateOnly();
        }
    }

    public string WidgetOpacityText => $"{WidgetOpacity * 100:0}%";

    public bool HoverOpaque
    {
        get => State.Settings.HoverOpaque;
        set { State.Settings.HoverOpaque = value; OnPropertyChanged(); SaveStateOnly(); }
    }

    public bool RunAtStartup
    {
        get => StartupService.IsEnabled;
        set
        {
            try
            {
                StartupService.SetEnabled(value);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Loc.T("mv.startupFailed", ex.Message), Loc.T("ui.appName"));
            }
            OnPropertyChanged();
        }
    }
}
