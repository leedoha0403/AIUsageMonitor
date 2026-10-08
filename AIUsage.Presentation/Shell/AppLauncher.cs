using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AIUsage.Presentation.Shell;

// Starts the desktop apps the mini widget links to. Both ship as Store (MSIX) packages, so "installed" means the
// package is registered for the current user; they are started through their AppsFolder entry.
public sealed class AppLauncher
{
    private const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public static readonly AppLauncher Claude = new("Claude_", "Claude", "Claude.exe", @"Assets\Square44x44Logo.targetsize-256_altform-unplated.png", null);
    // The Codex logo is white on transparent; the app itself draws it on this blue plate.
    public static readonly AppLauncher Codex = new("OpenAI.Codex_", "App", "ChatGPT.exe", @"assets\Square44x44Logo.targetsize-256_altform-unplated.png", Color.FromRgb(0x31, 0x43, 0xFF));

    private readonly string _packagePrefix;
    private readonly string _appId;
    private readonly string _exeName;
    private readonly string _iconRelativePath;
    private ImageSource? _icon;
    private string? _iconPackage;

    private AppLauncher(string packagePrefix, string appId, string exeName, string iconRelativePath, Color? plate)
    {
        _packagePrefix = packagePrefix;
        _appId = appId;
        _exeName = exeName;
        _iconRelativePath = iconRelativePath;
        if (plate is { } c)
        {
            IconPlate = new SolidColorBrush(c);
            IconPlate.Freeze();
        }
    }

    // Background drawn behind the logo (null when the logo already carries its own).
    public Brush? IconPlate { get; }

    // The app's own logo, read from the installed package; null when the app or the file is missing.
    public ImageSource? Icon
    {
        get
        {
            try
            {
                var package = FindPackage();
                if (package == null) return null;
                if (_icon != null && _iconPackage == package) return _icon;
                using var key = Registry.CurrentUser.OpenSubKey(PackagesKey + "\\" + package);
                var root = key?.GetValue("PackageRootFolder") as string;
                if (string.IsNullOrEmpty(root)) return null;
                var file = System.IO.Path.Combine(root, _iconRelativePath);
                if (!System.IO.File.Exists(file)) return null;
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(file);
                image.DecodePixelWidth = 64;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                _iconPackage = package;
                return _icon = image;
            }
            catch
            {
                return null;
            }
        }
    }

    // Full package name ("Claude_2.1.0.0_x64__pzs8sxrjxfjjc") of the installed package, or null when not installed.
    private string? FindPackage()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PackagesKey);
            return key?.GetSubKeyNames().FirstOrDefault(n => n.StartsWith(_packagePrefix, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    public bool IsInstalled => FindPackage() != null;

    public bool Launch()
    {
        try
        {
            var package = FindPackage();
            if (package == null) return false;
            // "Name_Version_Arch__PublisherId" -> family name "Name_PublisherId"
            var parts = package.Split('_');
            if (parts.Length < 5) return false;
            var family = parts[0] + "_" + parts[^1];
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{family}!{_appId}") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AIUsage.Core.AppLog.Write($"could not start {_exeName}: {ex.Message}");
            return false;
        }
    }
}
