using Horizon.Builder.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Horizon.Builder.App;

public sealed partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }
    private async void InstallRustup_Click(object sender, RoutedEventArgs e)
    {
        InstallRustup.IsEnabled = false;
        InstallStatus.Text = "Windows Package Manager is running. Follow its prompts.";
        try
        {
            await WindowsPackageManager.InstallRustupAsync(new SystemProbe());
            InstallStatus.Text = "WinGet completed. Restart the builder, then check setup again.";
        }
        catch (Exception error)
        {
            InstallStatus.Text = $"Rustup was not installed: {error.Message}";
        }
        finally { InstallRustup.IsEnabled = true; }
    }
    private void CheckSetup_Click(object sender, RoutedEventArgs e)
    {
        Results.Children.Clear();
        var profile = (TargetProfile)Profile.SelectedIndex;
        var selection = new BuildSelection(profile, NativeTouchMod.IsChecked == true);
        Results.Children.Add(new TextBlock
        {
            Text = $"Requested target: {selection.RustTarget} | Features: {string.Join(", ", selection.Features)}",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var check in new SetupChecker(new SystemProbe()).Scan(profile))
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = $"{check.Name} — {check.Status}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = check.Detail, TextWrapping = TextWrapping.Wrap });
            if (check.Instructions is { } instructions)
                panel.Children.Add(new HyperlinkButton { Content = "Open publisher instructions", NavigateUri = instructions });
            Results.Children.Add(panel);
        }
    }
}
