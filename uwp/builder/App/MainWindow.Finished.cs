using System.Diagnostics;
using Horizon.Builder.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Horizon.Builder.App;

public sealed partial class MainWindow
{
    private sealed record LocalInstallCheck(
        IReadOnlyList<(TargetProfile Target, SignedPackageOutput Output)> Eligible,
        bool RequiresExistingResourceFramework,
        string? ExistingResourceFrameworkName,
        bool ExactResourceFrameworkInstalled,
        string? FrameworkQueryError,
        InstalledClientPackage? InstalledClient,
        string? InstalledClientQueryError);

    private async Task<LocalInstallCheck> CheckLocalInstallAvailabilityAsync(System.Runtime.InteropServices.Architecture hostArchitecture)
    {
        var requiresExistingFramework = builtResourceMode == 1;
        var frameworkInstalled = !requiresExistingFramework;
        string? queryError = null;
        InstalledClientPackage? installedClient = null;
        string? installedClientQueryError = null;
        try { installedClient = await LocalPackageInstaller.FindInstalledClientAsync(PackageSigning.GetOrCreateIdentity().Publisher); }
        catch (Exception error) { installedClientQueryError = error.Message; }
        if (requiresExistingFramework && builtOutputs.Count > 0 && builtExistingResourceFrameworkName is { } frameworkName)
        {
            try
            {
                var expectedPublisher = PackageSigning.GetOrCreateIdentity().Publisher;
                frameworkInstalled = await LocalPackageInstaller.IsResourceFrameworkInstalledAsync(frameworkName, expectedPublisher);
            }
            catch (Exception error)
            {
                frameworkInstalled = false;
                queryError = error.Message;
            }
        }
        var eligible = builtOutputs.Where(item => LocalPackageInstaller.CanInstall(item.Target, hostArchitecture,
            Environment.OSVersion.Version, requiresExistingFramework, frameworkInstalled)).ToArray();
        if (installedClient is not null && Version.TryParse(PackageVersion.Text, out var builtVersion) && builtVersion <= installedClient.Version)
            eligible = [];
        return new(eligible, requiresExistingFramework, builtExistingResourceFrameworkName, frameworkInstalled, queryError,
            installedClient, installedClientQueryError);
    }

    private string FormatLocalInstallAvailability(LocalInstallCheck check)
    {
        if (builtOutputs.Count == 0) return string.Empty;
        var appStatus = check.InstalledClientQueryError is { } clientError
            ? $"Windows could not check for an existing MCHorizonUWP installation: {clientError}"
            : check.InstalledClient is { } installed
                ? $"MCHorizonUWP {installed.Version} is installed for {DisplayInstalledArchitecture(installed.Architecture)}. A higher package version is required to update it."
                : "No MCHorizonUWP package from this signing identity is installed for this Windows account.";
        if (check.InstalledClient is { } current && Version.TryParse(PackageVersion.Text, out var candidate) && candidate > current.Version
            && check.Eligible.Any(item => string.Equals(ArchitectureFor(item.Target), current.Architecture, StringComparison.OrdinalIgnoreCase)))
            appStatus = $"MCHorizonUWP {current.Version} is installed for {DisplayInstalledArchitecture(current.Architecture)}. The matching package will update the same app identity; Windows normally preserves local app data during package updates.";
        else if (check.InstalledClient is { } installedClient && Version.TryParse(PackageVersion.Text, out var nextVersion) && nextVersion > installedClient.Version
            && check.Eligible.Count > 0)
            appStatus = $"MCHorizonUWP {installedClient.Version} is installed for {DisplayInstalledArchitecture(installedClient.Architecture)}. A different architecture will be installed only after a data-compatibility warning.";
        if (check.RequiresExistingResourceFramework)
        {
            if (check.ExistingResourceFrameworkName is not { } frameworkName)
                return appStatus + " Install is unavailable because the required existing Horizon resource framework was not recorded. Rebuild and select the exact installed framework name.";
            if (check.FrameworkQueryError is { } error)
                return appStatus + $" Install is unavailable because the builder could not verify {frameworkName}: {error}. This build references an existing framework and did not produce its APPX.";
            if (!check.ExactResourceFrameworkInstalled)
                return appStatus + $" Install is unavailable because {frameworkName} from this signing identity is not installed for this Windows account. This build references an existing framework and did not produce its APPX. Install that exact framework on this PC or rebuild using separate or bundled resources.";
            if (check.Eligible.Count == 0)
                return appStatus + " The selected framework is installed, but none of the built architectures can be installed on this PC. Use the target device's installation guide.";
            return appStatus + $" The exact framework {frameworkName} is installed for this Windows account.";
        }
        return appStatus + (check.Eligible.Count > 0
            ? " The builder will install the client and the dependencies included in its architecture folder."
            : " Install on this PC is unavailable for the selected architecture or package version on this computer. Use the installation guide for the target device.");
    }

    private void UpdateFinishedOutputs()
    {
        FinishedOutputs.Children.Clear();
        foreach (var item in builtOutputs)
        {
            var output = item.Output;
            var directory = Path.GetFullPath(Path.GetDirectoryName(output.Client)!);
            var details = new StackPanel { Spacing = 8 };
            AddOutputPath(details, "Output folder", directory);
            AddOutputPath(details, "Client APPX", output.Client);
            AddOutputPath(details, "Public signing certificate", output.Certificate);
            AddOutputPath(details, "Installation guide", output.InstallationGuide);

            if (output.ResourceFramework is { } resourceFramework)
                AddOutputPath(details, "Horizon resource framework APPX", resourceFramework);
            else if (builtResourceMode == 1)
                AddOutputPath(details, "Horizon resource framework", $"Use the existing target framework: {builtExistingResourceFrameworkName}");
            else if (builtResourceMode == 2)
                AddOutputPath(details, "Horizon resource framework", "Minecraft 26.3 is bundled in the client APPX; no separate framework package is needed.");
            else
                AddOutputPath(details, "Horizon resource framework", "Not generated.");

            if (output.Dependencies is { Count: > 0 } dependencies)
            {
                AddOutputPath(details, "Microsoft framework dependencies folder", Path.GetDirectoryName(dependencies[0]));
                foreach (var dependency in dependencies)
                    AddOutputPath(details, Path.GetFileName(dependency), dependency);
            }
            else
                AddOutputPath(details, "Microsoft framework dependencies", "No framework APPX files were bundled.");

            var open = new Button { Content = "Open this folder", HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => OpenOutputFolder(directory);
            details.Children.Add(open);
            FinishedOutputs.Children.Add(new Expander
            {
                Header = $"{DisplayArchitecture(item.Target)} packages",
                Content = details,
                IsExpanded = FinishedOutputs.Children.Count == 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            });
        }
    }

    private static void AddOutputPath(StackPanel parent, string label, string? path)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(new TextBlock { Text = path ?? "Not generated.", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        parent.Children.Add(row);
    }

    private static void OpenOutputFolder(string directory)
    {
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private static string DisplayArchitecture(TargetProfile target) => target switch
    {
        TargetProfile.DesktopX64 => "x64",
        TargetProfile.DesktopX86 => "x86",
        TargetProfile.SurfaceProX => "ARM64",
        TargetProfile.WindowsMobile => "ARM32",
        _ => ArchitectureFor(target),
    };

    private static string DisplayInstalledArchitecture(string architecture) => architecture.ToLowerInvariant() switch
    {
        "x64" => "x64",
        "x86" => "x86",
        "arm" => "ARM32",
        "arm64" => "ARM64",
        "neutral" => "neutral",
        _ => architecture,
    };

    private string GetLocalInstallAction(LocalInstallCheck check)
    {
        if (check.InstalledClient is not { } installed || !Version.TryParse(PackageVersion.Text, out var candidate)
            || candidate <= installed.Version) return "Install on this PC";
        return check.Eligible.Any(item => string.Equals(ArchitectureFor(item.Target), installed.Architecture, StringComparison.OrdinalIgnoreCase))
            ? "Upgrade on this PC"
            : "Install on this PC";
    }

    private void StartAnotherBuild_Click(object sender, RoutedEventArgs e)
    {
        if (buildActionRunning || navigationBusy) return;
        packageVersionResolvedAgainstInstalled = false;
        if (!packageVersionCustomized) SetSuggestedPackageVersion(LocalRecipe.PackageVersionFor(DateTimeOffset.UtcNow));
        wizardStep = 0;
        highestVisited = 0;
        completedPages = 0;
        lastRenderedStep = -1;
        UpdateWizard();
        PageScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void SetSuggestedPackageVersion(string version)
    {
        suggestedPackageVersion = version;
        packageVersionUpdating = true;
        PackageVersion.Text = version;
        packageVersionUpdating = false;
        packageVersionCustomized = false;
    }

    private void ExitBuilder_Click(object sender, RoutedEventArgs e) => Close();
}
