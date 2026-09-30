using System.Windows;
using AIUsage.Core.Storage;
using AIUsage.Presentation.ViewModels;
using AIUsage.Presentation.Views;

namespace AIUsage.Tests;

public class ChipsSizeTests
{
    // The chips must be the same size whether the app shows them (app-level Styles.xaml with its TextBlock weight
    // triggers) or a Host widget does (nothing of ours at application level).
    [Fact]
    public async Task Chips_are_the_same_size_in_a_Host_and_in_the_standalone_app()
    {
        double hostLike = 0, appLike = 0;
        await WpfHost.Run(() =>
        {
            var vm = new UsageFeatureViewModel(new StateStore(Path.Combine(Path.GetTempPath(), "aiusage-chips-" + Guid.NewGuid().ToString("N"))), new FakeUi());

            double Measure()
            {
                var window = new UsageChipsWindow(vm, new ChipsActions { Click = () => { }, OpenDashboard = () => { } })
                {
                    Left = -3000
                };
                window.Show();
                window.UpdateLayout();
                var width = window.ActualWidth;
                window.CloseForExit();
                return width;
            }

            hostLike = Measure();
            var resources = Application.Current.Resources.MergedDictionaries;
            resources.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AIUsage.Presentation;component/Brushes.xaml") });
            resources.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AIUsage.Presentation;component/Styles.xaml") });
            appLike = Measure();
        });

        Assert.True(hostLike > 0);
        Assert.Equal(hostLike, appLike, 0.01);
    }
}
