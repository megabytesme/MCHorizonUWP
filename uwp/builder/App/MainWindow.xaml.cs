using Horizon.Builder.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Horizon.Builder.App;

public sealed partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }
    private string? sourceDirectory;
    private string? resourceDirectory;
    private string? minecraftDirectory;

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    private async void ChooseSource_Click(object sender, RoutedEventArgs e)
    {
        var chosen = await PickFolderAsync();
        if (chosen is not null) { sourceDirectory = chosen; SourcePath.Text = chosen; }
    }

    private async void ChooseResources_Click(object sender, RoutedEventArgs e)
    {
        var chosen = await PickFolderAsync();
        if (chosen is not null) { resourceDirectory = chosen; ResourcesPath.Text = chosen; }
    }

    private async void ChooseMinecraft_Click(object sender, RoutedEventArgs e)
    {
        var chosen = await PickFolderAsync();
        if (chosen is not null) { minecraftDirectory = chosen; MinecraftPath.Text = chosen; }
    }

    private async void CheckRecipe_Click(object sender, RoutedEventArgs e)
    {
        CheckRecipe.IsEnabled = false;
        RecipeSteps.Children.Clear();
        try
        {
            var source = sourceDirectory;
            var resources = resourceDirectory;
            var minecraft = minecraftDirectory;
            if (source is null || resources is null)
                throw new InvalidOperationException("Choose both a local source tree and prepared resources.");
            var selection = new BuildSelection((TargetProfile)Profile.SelectedIndex, NativeTouchMod.IsChecked == true, PackageVersion.Text, PrepareSounds.IsChecked == true, PackageFramework.IsChecked == true);
            var plan = LocalRecipe.Plan(selection);
            LocalRecipe.ValidateResources(resources);
            if (selection.PrepareSounds) LocalRecipe.ValidateSoundPreparation(resources, minecraft);
            var probe = new SystemProbe();
            await LocalRecipe.ValidateSourceAsync(source, probe);
            var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe));
            RecipeStatus.Text = blockers.Count == 0
                ? "Pinned source revision and required tool locations were found. Cargo will fetch missing dependencies when the build starts; signing is not yet available."
                : "The recipe has missing prerequisites:";
            foreach (var blocker in blockers) RecipeSteps.Children.Add(new TextBlock { Text = blocker, TextWrapping = TextWrapping.Wrap });
            if (!selection.PrepareSounds && LocalRecipe.SoundWarning(resources) is { } warning)
                RecipeSteps.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap });
            foreach (var step in plan.Steps) RecipeSteps.Children.Add(new TextBlock { Text = step.Name, TextWrapping = TextWrapping.Wrap });
            foreach (var note in plan.Notes) RecipeSteps.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap });
        }
        catch (Exception error) { RecipeStatus.Text = $"Could not validate the recipe: {error.Message}"; }
        finally { CheckRecipe.IsEnabled = true; }
    }

    private async void RunRecipe_Click(object sender, RoutedEventArgs e)
    {
        RunRecipe.IsEnabled = false;
        try
        {
            var source = sourceDirectory;
            var resources = resourceDirectory;
            var minecraft = minecraftDirectory;
            if (source is null || resources is null)
                throw new InvalidOperationException("Choose both a local source tree and prepared resources.");
            var selection = new BuildSelection((TargetProfile)Profile.SelectedIndex, NativeTouchMod.IsChecked == true, PackageVersion.Text, PrepareSounds.IsChecked == true, PackageFramework.IsChecked == true);
            var progress = new Progress<string>(message => RecipeStatus.Text = message);
            var result = await LocalBuildRunner.RunAsync(source, resources, selection, new SystemProbe(), progress, minecraft);
            RecipeStatus.Text = $"Unsigned client package: {result.UnsignedPackage}. Sign it with a matching publisher certificate before installation.";
            if (result.ResourceFrameworkPackage is { } framework)
                RecipeStatus.Text += $" Unsigned resource framework: {framework}. Sign it with the same publisher and install it as a dependency. It contains private Minecraft resources.";
            if (result.SoundWarning is { } warning)
                RecipeStatus.Text += " " + warning;
        }
        catch (Exception error) { RecipeStatus.Text = $"{RecipeStatus.Text} Build failed: {error.Message}"; }
        finally { RunRecipe.IsEnabled = true; }
    }
    private async void InstallTool_Click(object sender, RoutedEventArgs e)
    {
        InstallRustup.IsEnabled = false;
        InstallGit.IsEnabled = false;
        InstallPython.IsEnabled = false;
        InstallStatus.Text = "Windows Package Manager is running. Follow its prompts.";
        try
        {
            var tool = ReferenceEquals(sender, InstallRustup) ? InstallableTool.Rustup
                : ReferenceEquals(sender, InstallGit) ? InstallableTool.Git
                : ReferenceEquals(sender, InstallPython) ? InstallableTool.Python
                : throw new InvalidOperationException("Unknown tool selection.");
            await WindowsPackageManager.InstallAsync(new SystemProbe(), tool);
            InstallStatus.Text = "WinGet completed. Restart the builder, then check setup again.";
        }
        catch (Exception error)
        {
            InstallStatus.Text = $"The tool was not installed: {error.Message}";
        }
        finally { InstallRustup.IsEnabled = true; InstallGit.IsEnabled = true; InstallPython.IsEnabled = true; }
    }
    private void CheckSetup_Click(object sender, RoutedEventArgs e)
    {
        Results.Children.Clear();
        var profile = (TargetProfile)Profile.SelectedIndex;
        var selection = new BuildSelection(profile, NativeTouchMod.IsChecked == true, PackageVersion.Text, PrepareSounds.IsChecked == true, PackageFramework.IsChecked == true);
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
