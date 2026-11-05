using Horizon.Builder.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Horizon.Builder.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => minecraftSignInCancellation?.Cancel();
        Profile.SelectionChanged += Profile_SelectionChanged;
        Renderer.SelectionChanged += Renderer_SelectionChanged;
        ResourceMode.SelectionChanged += ResourceMode_SelectionChanged;
        Profile.SelectedIndex = (int)BuildSelection.DefaultTarget(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        PackageVersion.Text = LocalRecipe.DefaultPackageVersion;
        UpdateRendererChoices();
        UpdateResourceChoices();
        UpdateWizard();
        UpdateReleaseChoices();
        UpstreamNightly.Text = $"Horizon nightly {LocalRecipe.UpstreamNightlyDate} · {LocalRecipe.UpstreamNightlyShortCommit} (UWP port)";
        _ = CheckNightlyAsync();
        _ = LoadCatalogAsync();
    }

    private async Task CheckNightlyAsync()
    {
        try
        {
            var release = await NightlyRelease.FetchAsync();
            NightlyStatus.Text = release.SourceCommit == LocalRecipe.UpstreamNightlyCommit
                ? "This recipe follows the current published Horizon nightly."
                : $"Horizon has published nightly source {release.SourceCommit[..7]} ({release.PublishedAt:yyyy-MM-dd} UTC). The UWP port must be integrated and tested before this recipe can build it.";
        }
        catch (Exception)
        {
            NightlyStatus.Text = "Could not check for a newer nightly. The pinned recipe is still available.";
        }
    }
    private string? sourceDirectory;
    private string? resourceDirectory;
    private string? minecraftDirectory;
    private IReadOnlyList<NativeModListing> nativeMods = [];
    private CancellationTokenSource? minecraftSignInCancellation;
    private CompatibilityCatalog? catalog;
    private sealed record ReleaseChoice(string Patch, string Client, string Architecture, CompatibilityResult Compatibility)
    {
        public override string ToString() => $"Patch {Patch} · UWP client {Client} · {(Compatibility.BuildVerified ? "build verified" : "build not verified")}";
    }
    private int wizardStep;

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (wizardStep > 0) wizardStep--;
        UpdateWizard();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (wizardStep < 5) wizardStep++;
        UpdateWizard();
    }

    private void UpdateWizard()
    {
        FrameworkElement[] pages = [WelcomePage, TargetPage, SourcePage, OptionsPage, SetupPage, BuildPage];
        string[] labels = ["Welcome", "Target", "Source and resources", "Options", "Setup", "Build"];
        for (var i = 0; i < pages.Length; i++) pages[i].Visibility = i == wizardStep ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = $"Step {wizardStep + 1} of {pages.Length} · {labels[wizardStep]}";
        BackButton.IsEnabled = wizardStep > 0;
        NextButton.IsEnabled = wizardStep < pages.Length - 1;
    }

    private string SelectedRenderer => Renderer.SelectedIndex switch { 1 => "angle", 2 => "dx12", _ => "native-d3d11" };

    private void UpdateRendererChoices()
    {
        if (Profile is null || Renderer is null || NativeTouchMod is null) return;
        Renderer.IsEnabled = true;
        var nativeD3d11 = Renderer.SelectedIndex == 0;
        NativeTouchMod.IsEnabled = nativeD3d11;
        if (!nativeD3d11) NativeTouchMod.IsChecked = false;
    }

    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateRendererChoices(); UpdateReleaseChoices(); }
    private void Renderer_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRendererChoices();
    private void ResourceMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateResourceChoices();

    private void UpdateResourceChoices()
    {
        if (ResourceMode is null || ExistingFrameworkName is null || ChooseResources is null || ChooseMinecraft is null || PrepareSounds is null) return;
        var reuse = ResourceMode.SelectedIndex == 1;
        ExistingFrameworkName.IsEnabled = reuse;
        ChooseResources.IsEnabled = !reuse;
        ChooseMinecraft.IsEnabled = !reuse;
        PrepareSounds.IsEnabled = !reuse;
        if (reuse) PrepareSounds.IsChecked = false;
    }

    private BuildSelection CurrentSelection() => new((TargetProfile)Profile.SelectedIndex,
        NativeTouchMod.IsChecked == true, PackageVersion.Text, PrepareSounds.IsChecked == true,
        ResourceMode.SelectedIndex == 0, NativeDeviceVibration.IsChecked == true,
        HorizonIdentity.ClientId, SelectedRenderer, ResourceMode.SelectedIndex == 1,
        ResourceMode.SelectedIndex == 1 ? ExistingFrameworkName.Text : "");

    private string SelectedArchitecture => (TargetProfile)Profile.SelectedIndex switch
    {
        TargetProfile.DesktopX64 => "x64", TargetProfile.DesktopX86 => "x86",
        TargetProfile.SurfaceProX => "arm64", TargetProfile.WindowsMobile => "arm", _ => "",
    };

    private void UpdateReleaseChoices(bool preferCatalog = false)
    {
        if (ReleaseSelection is null || Profile is null) return;
        var previous = ReleaseSelection.SelectedItem as ReleaseChoice;
        var previousLocal = ReleaseSelection.SelectedItem is string;
        ReleaseSelection.Items.Clear();
        ReleaseSelection.Items.Add("Local source checkout");
        if (catalog is not null)
        {
            var arch = SelectedArchitecture;
            foreach (var patch in catalog.Patches.OrderByDescending(item => new Version(item.Version)))
                foreach (var client in catalog.Clients.OrderByDescending(item => new Version(item.Version)))
                {
                    var check = catalog.Check(patch.Version, client.Version, CatalogTrust.BuilderVersion, arch);
                    if (check.Allowed) ReleaseSelection.Items.Add(new ReleaseChoice(patch.Version, client.Version, arch, check));
                }
            ReleaseStatus.Text = ReleaseSelection.Items.Count == 1
                ? "No catalog release is declared compatible with this target and builder."
                : "The signed catalog was verified. Candidate assets are still draft releases and cannot yet be downloaded.";
        }
        var matching = previous is null ? null : ReleaseSelection.Items.OfType<ReleaseChoice>()
            .FirstOrDefault(item => item.Patch == previous.Patch && item.Client == previous.Client && item.Architecture == previous.Architecture);
        ReleaseSelection.SelectedItem = matching ?? (previousLocal && !preferCatalog ? ReleaseSelection.Items[0]
            : ReleaseSelection.Items.Count > 1 ? ReleaseSelection.Items[1] : ReleaseSelection.Items[0]);
    }

    private async Task LoadCatalogAsync()
    {
        RefreshReleases.IsEnabled = false;
        ReleaseStatus.Text = "Checking the signed release catalog…";
        try
        {
            using var transport = new CatalogTransport();
            catalog = await CatalogTrust.FetchAsync(transport);
            UpdateReleaseChoices(preferCatalog: true);
        }
        catch (Exception)
        {
            catalog = null;
            UpdateReleaseChoices();
            ReleaseStatus.Text = "The signed catalog could not be loaded. A local source checkout can still be used.";
        }
        finally { RefreshReleases.IsEnabled = true; }
    }

    private async void RefreshReleases_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();

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

    private async void StartMinecraftSignIn_Click(object sender, RoutedEventArgs e)
    {
        StartMinecraftSignIn.IsEnabled = false;
        CancelMinecraftSignIn.IsEnabled = true;
        MinecraftSignInLink.Visibility = Visibility.Collapsed;
        MinecraftSignInStatus.Text = "Starting Microsoft sign-in…";
        using var signIn = new MinecraftSignIn();
        using var cancellation = new CancellationTokenSource();
        minecraftSignInCancellation = cancellation;
        try
        {
            var challenge = await signIn.BeginAsync(cancellation.Token);
            MinecraftSignInLink.NavigateUri = challenge.VerificationUri;
            MinecraftSignInLink.Visibility = Visibility.Visible;
            MinecraftSignInStatus.Text = $"Open the sign-in page and enter code {challenge.UserCode}.";
            var owns = await signIn.CompleteOwnershipCheckAsync(challenge, cancellation.Token);
            MinecraftSignInStatus.Text = owns
                ? "Minecraft: Java Edition ownership confirmed. Automatic resource preparation is not available in this preview."
                : "This account does not have Minecraft: Java Edition. Sign in with an account that owns it.";
        }
        catch (OperationCanceledException) { MinecraftSignInStatus.Text = "Sign-in cancelled."; }
        catch (Exception error) { MinecraftSignInStatus.Text = $"Sign-in could not be completed: {error.Message}"; }
        finally
        {
            minecraftSignInCancellation = null;
            MinecraftSignInLink.Visibility = Visibility.Collapsed;
            StartMinecraftSignIn.IsEnabled = true;
            CancelMinecraftSignIn.IsEnabled = false;
        }
    }

    private void CancelMinecraftSignIn_Click(object sender, RoutedEventArgs e) => minecraftSignInCancellation?.Cancel();

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
            var selection = CurrentSelection();
            var release = ReleaseSelection.SelectedItem as ReleaseChoice;
            var plan = LocalRecipe.Plan(selection);
            if (release is null && source is null) throw new InvalidOperationException("Choose a local source tree or a catalog release.");
            LocalRecipe.ValidateResourceSelection(selection, resources, minecraft);
            var probe = new SystemProbe();
            if (release is null) await LocalRecipe.ValidateSourceAsync(source!, probe);
            var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe), splitRelease: release is not null);
            RecipeStatus.Text = blockers.Count == 0
                ? release is null ? "Pinned source revision and required tool locations were found. The local build will be unsigned."
                    : "The catalog combination and required tool locations were found. Draft release assets must be published before download."
                : "The recipe has missing prerequisites:";
            foreach (var blocker in blockers) RecipeSteps.Children.Add(new TextBlock { Text = blocker, TextWrapping = TextWrapping.Wrap });
            if (selection.ValidatedExistingResourceFramework is null && !selection.PrepareSounds && LocalRecipe.SoundWarning(resources!) is { } warning)
                RecipeSteps.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap });
            foreach (var step in plan.Steps) RecipeSteps.Children.Add(new TextBlock { Text = release is not null && step.Name == "Snapshot pinned source" ? "Download and verify the selected release source" : step.Name, TextWrapping = TextWrapping.Wrap });
            if (release is null)
                foreach (var note in plan.Notes) RecipeSteps.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap });
            else
                RecipeSteps.Children.Add(new TextBlock { Text = "The client and any new resource framework will be signed with your local certificate. The public certificate and installation guide are included.", TextWrapping = TextWrapping.Wrap });
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
            var selection = CurrentSelection();
            var release = ReleaseSelection.SelectedItem as ReleaseChoice;
            if (release is null && source is null) throw new InvalidOperationException("Choose a local source tree or a catalog release.");
            LocalRecipe.ValidateResourceSelection(selection, resources, minecraft);
            var progress = new Progress<string>(message => RecipeStatus.Text = message);
            var probe = new SystemProbe();
            LocalBuildResult result;
            if (release is null) result = await LocalBuildRunner.RunAsync(source!, resources, selection, probe, progress, minecraft);
            else
            {
                var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe), splitRelease: true);
                if (blockers.Count > 0) throw new InvalidOperationException(string.Join(" ", blockers));
                var git = probe.FindOnPath("git.exe") ?? throw new InvalidOperationException("Git is required to assemble the release.");
                var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".huwp", "b");
                Directory.CreateDirectory(baseDirectory);
                var workspace = Path.Combine(baseDirectory, "r" + Guid.NewGuid().ToString("N")[..15]);
                RecipeStatus.Text = "Downloading and verifying the selected source release…";
                using var transport = new CatalogTransport();
                var prepared = await ReleaseSource.PrepareAsync(transport, CatalogTrust.CatalogUri, CatalogTrust.SignatureUri,
                    CatalogTrust.PublicKey(), release.Patch, release.Client, CatalogTrust.BuilderVersion, release.Architecture, workspace, git);
                result = await LocalBuildRunner.RunPreparedAsync(prepared, resources, selection, probe, progress, minecraft);
            }
            RecipeStatus.Text = result.SignedOutput is { } signed
                ? $"Signed client: {signed.Client}. Public certificate: {signed.Certificate}. Installation guide: {signed.InstallationGuide}."
                : $"Unsigned client package: {result.UnsignedPackage}. Sign it with a matching publisher certificate before installation.";
            if (result.SignedOutput?.ResourceFramework is { } signedFramework)
                RecipeStatus.Text += $" Signed resource framework: {signedFramework}. It contains private Minecraft resources.";
            else if (result.ResourceFrameworkPackage is { } framework)
                RecipeStatus.Text += $" Unsigned resource framework: {framework}. Sign it with the same publisher and install it as a dependency. It contains private Minecraft resources.";
            if (selection.ValidatedExistingResourceFramework is { } existing)
                RecipeStatus.Text += $" Requires existing resource framework {existing} on the target.";
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
        InstallStatus.Text = "Starting Windows Package Manager…";
        try
        {
            var tool = ReferenceEquals(sender, InstallRustup) ? InstallableTool.Rustup
                : ReferenceEquals(sender, InstallGit) ? InstallableTool.Git
                : ReferenceEquals(sender, InstallPython) ? InstallableTool.Python
                : throw new InvalidOperationException("Unknown tool selection.");
            InstallStatus.Text = tool == InstallableTool.Rustup
                ? "Windows Package Manager is running. Follow its prompts."
                : $"Installing {tool} with Windows Package Manager…";
            await WindowsPackageManager.InstallAsync(new SystemProbe(), tool);
            SystemProbe.RefreshProcessPath();
            InstallStatus.Text = "WinGet completed. Check setup again.";
        }
        catch (Exception error)
        {
            InstallStatus.Text = $"The tool was not installed: {error.Message}";
        }
        finally { InstallRustup.IsEnabled = true; InstallGit.IsEnabled = true; InstallPython.IsEnabled = true; }
    }

    private async void BrowseNativeMods_Click(object sender, RoutedEventArgs e)
    {
        BrowseNativeMods.IsEnabled = false;
        NativeModSearch.IsEnabled = false;
        NativeModsResults.Children.Clear();
        NativeModsStatus.Text = "Loading Horizon’s native mod registry…";
        try
        {
            nativeMods = await NativeModIndex.FetchAsync();
            NativeModSearch.IsEnabled = true;
            RenderNativeMods();
        }
        catch (Exception error) { NativeModsStatus.Text = $"Could not load native mods: {error.Message}"; }
        finally { BrowseNativeMods.IsEnabled = true; }
    }
    private void NativeModSearch_TextChanged(object sender, TextChangedEventArgs e) => RenderNativeMods();

    private void RenderNativeMods()
    {
        if (NativeModsResults is null || NativeModSearch is null || NativeModsStatus is null) return;
        NativeModsResults.Children.Clear();
        var search = NativeModSearch.Text.Trim();
        var matching = nativeMods.Where(mod => search.Length == 0 || mod.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || mod.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || mod.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
            || mod.Authors.Any(author => author.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
        NativeModsStatus.Text = $"{matching.Length} native mods list Minecraft {LocalRecipe.MinecraftVersion}. UWP compatibility is not yet verified; installing registry mods is not available in this preview.";
        foreach (var mod in matching)
        {
            var item = new StackPanel { Spacing = 3 };
            item.Children.Add(new TextBlock { Text = $"{mod.Name} · {mod.Version}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            item.Children.Add(new TextBlock { Text = mod.Summary, TextWrapping = TextWrapping.Wrap });
            NativeModsResults.Children.Add(item);
        }
    }
    private void CheckSetup_Click(object sender, RoutedEventArgs e)
    {
        Results.Children.Clear();
        var selection = CurrentSelection();
        Results.Children.Add(new TextBlock
        {
            Text = $"Requested target: {selection.RustTarget} | Features: {string.Join(", ", selection.Features)}",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var check in new SetupChecker(new SystemProbe()).Scan(selection.Target, splitRelease: ReleaseSelection.SelectedItem is ReleaseChoice))
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
