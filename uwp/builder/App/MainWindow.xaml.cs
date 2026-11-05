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
        Title = $"Horizon UWP Builder — Preview {CatalogTrust.BuilderVersion}";
        Closed += (_, _) => minecraftSignInCancellation?.Cancel();
        Profile.SelectionChanged += Profile_SelectionChanged;
        ReleaseSelection.SelectionChanged += (_, _) => UpdateBuildActions();
        ResourceMode.SelectionChanged += ResourceMode_SelectionChanged;
        Profile.SelectedIndex = (int)BuildSelection.DefaultTarget(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        PackageVersion.Text = LocalRecipe.DefaultPackageVersion;
        UpdateResourceChoices();
        UpdateWizard();
        UpdateReleaseChoices();
        _ = LoadCatalogAsync();
    }
    private string? resourceDirectory;
    private string? minecraftDirectory;
    private IReadOnlyList<NativeModListing> nativeMods = [];
    private readonly HashSet<string> selectedNativeMods = new(StringComparer.Ordinal);
    private CancellationTokenSource? minecraftSignInCancellation;
    private CompatibilityCatalog? catalog;
    private bool buildActionRunning;
    private bool minecraftOwnershipVerified;
    private SignedPackageOutput? builtOutput;
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

    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateReleaseChoices();
    private void ResourceMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateResourceChoices();
    private void DownloadMinecraft_Click(object sender, RoutedEventArgs e) => UpdateResourceChoices();

    private void UpdateResourceChoices()
    {
        if (ResourceMode is null || ExistingFrameworkName is null || DownloadMinecraft is null || ChooseResources is null || ChooseMinecraft is null || PrepareSounds is null) return;
        var reuse = ResourceMode.SelectedIndex == 1;
        if (reuse) DownloadMinecraft.IsChecked = false;
        var download = !reuse && DownloadMinecraft.IsChecked == true;
        ExistingFrameworkName.IsEnabled = reuse;
        DownloadMinecraft.IsEnabled = !reuse;
        ChooseResources.IsEnabled = !reuse && !download;
        ChooseMinecraft.IsEnabled = !reuse && !download;
        PrepareSounds.IsEnabled = !reuse && !download;
        if (reuse || download) PrepareSounds.IsChecked = false;
    }

    private BuildSelection CurrentSelection() => new((TargetProfile)Profile.SelectedIndex,
        NativeTouchMod.IsChecked == true, PackageVersion.Text, PrepareSounds.IsChecked == true,
        ResourceMode.SelectedIndex == 0, NativeDeviceVibration.IsChecked == true,
        HorizonIdentity.ClientId, ReuseResourceFramework: ResourceMode.SelectedIndex == 1,
        ExistingResourceFramework: ResourceMode.SelectedIndex == 1 ? ExistingFrameworkName.Text : "",
        RegistryNativeMods: selectedNativeMods.OrderBy(id => id, StringComparer.Ordinal).ToArray());

    private string SelectedArchitecture => (TargetProfile)Profile.SelectedIndex switch
    {
        TargetProfile.DesktopX64 => "x64", TargetProfile.DesktopX86 => "x86",
        TargetProfile.SurfaceProX => "arm64", TargetProfile.WindowsMobile => "arm", _ => "",
    };

    private void UpdateReleaseChoices()
    {
        if (ReleaseSelection is null || Profile is null) return;
        var previous = ReleaseSelection.SelectedItem as ReleaseChoice;
        ReleaseSelection.Items.Clear();
        if (catalog is not null)
        {
            var arch = SelectedArchitecture;
            foreach (var patch in catalog.Patches.OrderByDescending(item => new Version(item.Version)))
                foreach (var client in catalog.Clients.OrderByDescending(item => new Version(item.Version)))
                {
                    var check = catalog.Check(patch.Version, client.Version, CatalogTrust.BuilderVersion, arch);
                    if (check.Allowed) ReleaseSelection.Items.Add(new ReleaseChoice(patch.Version, client.Version, arch, check));
                }
            ReleaseStatus.Text = ReleaseSelection.Items.Count == 0
                ? "No catalog release is declared compatible with this target and builder."
                : "The signed catalog was verified. Choose a compatible preview release.";
        }
        var matching = previous is null ? null : ReleaseSelection.Items.OfType<ReleaseChoice>()
            .FirstOrDefault(item => item.Patch == previous.Patch && item.Client == previous.Client && item.Architecture == previous.Architecture);
        ReleaseSelection.SelectedItem = matching ?? (ReleaseSelection.Items.Count > 0 ? ReleaseSelection.Items[0] : null);
        UpdateBuildActions();
    }

    private void UpdateBuildActions()
    {
        if (ReleaseSelection is null || RunRecipe is null || CheckRecipe is null) return;
        var available = !buildActionRunning && ReleaseSelection.SelectedItem is ReleaseChoice;
        RunRecipe.IsEnabled = available && minecraftOwnershipVerified;
        CheckRecipe.IsEnabled = available;
    }

    private async Task LoadCatalogAsync()
    {
        RefreshReleases.IsEnabled = false;
        ReleaseStatus.Text = "Checking the signed release catalog…";
        try
        {
            using var transport = new CatalogTransport();
            catalog = await CatalogTrust.FetchAsync(transport);
            UpdateReleaseChoices();
        }
        catch (Exception)
        {
            catalog = null;
            UpdateReleaseChoices();
            ReleaseStatus.Text = "The signed catalog could not be loaded. Check your connection and try again.";
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

    private async void StartMinecraftSignIn_Click(object sender, RoutedEventArgs e)
    {
        minecraftOwnershipVerified = false;
        UpdateBuildActions();
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
            minecraftOwnershipVerified = owns;
            UpdateBuildActions();
            MinecraftSignInStatus.Text = owns
                ? "Minecraft: Java Edition ownership confirmed. Your resources can now be downloaded and prepared."
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
        buildActionRunning = true;
        UpdateBuildActions();
        RecipeSteps.Children.Clear();
        try
        {
            var resources = resourceDirectory;
            var minecraft = minecraftDirectory;
            var selection = CurrentSelection();
            var release = ReleaseSelection.SelectedItem as ReleaseChoice ?? throw new InvalidOperationException("Choose a compatible catalog release.");
            var plan = LocalRecipe.Plan(selection);
            var download = ResourceMode.SelectedIndex != 1 && DownloadMinecraft.IsChecked == true;
            if (!download) LocalRecipe.ValidateResourceSelection(selection, resources, minecraft);
            var probe = new SystemProbe();
            var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe),
                splitRelease: true, unifiedToolchain: release.Client == "0.1.1");
            RecipeStatus.Text = blockers.Count == 0
                ? "The catalog combination and required tool locations were found. The release files will be verified when downloaded."
                : "The recipe has missing prerequisites:";
            foreach (var blocker in blockers) RecipeSteps.Children.Add(new TextBlock { Text = blocker, TextWrapping = TextWrapping.Wrap });
            if (!download && selection.ValidatedExistingResourceFramework is null && !selection.PrepareSounds && LocalRecipe.SoundWarning(resources!) is { } warning)
                RecipeSteps.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap });
            if (download) RecipeSteps.Children.Add(new TextBlock { Text = "Download and prepare Minecraft resources in the private build workspace.", TextWrapping = TextWrapping.Wrap });
            foreach (var step in plan.Steps) RecipeSteps.Children.Add(new TextBlock { Text = step.Name == "Snapshot pinned source" ? "Download and verify the selected release source" : step.Name, TextWrapping = TextWrapping.Wrap });
            if (selection.RegistryNativeMods is { Count: > 0 } mods)
                RecipeSteps.Children.Add(new TextBlock { Text = $"Selected native mods: {string.Join(", ", mods)}. Their compatibility is checked while building.", TextWrapping = TextWrapping.Wrap });
            RecipeSteps.Children.Add(new TextBlock { Text = "The client and any new resource framework will be signed with your local certificate. The public certificate and installation guide are included.", TextWrapping = TextWrapping.Wrap });
        }
        catch (Exception error) { RecipeStatus.Text = $"Could not validate the recipe: {error.Message}"; }
        finally { buildActionRunning = false; UpdateBuildActions(); }
    }

    private async void RunRecipe_Click(object sender, RoutedEventArgs e)
    {
        builtOutput = null;
        InstallLocal.Visibility = Visibility.Collapsed;
        buildActionRunning = true;
        UpdateBuildActions();
        try
        {
            if (!minecraftOwnershipVerified) throw new InvalidOperationException("Sign in with an account that owns Minecraft: Java Edition before building.");
            var resources = resourceDirectory;
            var minecraft = minecraftDirectory;
            var selection = CurrentSelection();
            var release = ReleaseSelection.SelectedItem as ReleaseChoice ?? throw new InvalidOperationException("Choose a compatible catalog release.");
            var download = ResourceMode.SelectedIndex != 1 && DownloadMinecraft.IsChecked == true;
            if (!download) LocalRecipe.ValidateResourceSelection(selection, resources, minecraft);
            var progress = new Progress<string>(message => RecipeStatus.Text = message);
            var probe = new SystemProbe();
            await WindowsPackageManager.EnsureBuildBasicsAsync(probe, tool => WindowsPackageManager.InstallAsync(probe, tool),
                SystemProbe.RefreshProcessPath, progress);
            await WindowsPackageManager.EnsureClangAsync(probe, selection.Target, tool => WindowsPackageManager.InstallAsync(probe, tool), progress);
            await WindowsPackageManager.EnsureRustToolchainAsync(() => LocalRecipe.InspectRustAsync(probe),
                () => WindowsPackageManager.InstallRustToolchainAsync(probe), progress);
            var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe),
                splitRelease: true, unifiedToolchain: release.Client == "0.1.1");
            if (blockers.Count > 0) throw new InvalidOperationException(string.Join(" ", blockers));
            var git = probe.FindOnPath("git.exe") ?? throw new InvalidOperationException("Git is required to assemble the release.");
            var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".huwp", "b");
            Directory.CreateDirectory(baseDirectory);
            var workspace = Path.Combine(baseDirectory, "r" + Guid.NewGuid().ToString("N")[..15]);
            RecipeStatus.Text = "Downloading and verifying the selected source release…";
            using var transport = new CatalogTransport();
            var prepared = await ReleaseSource.PrepareAsync(transport, CatalogTrust.CatalogUri, CatalogTrust.SignatureUri,
                CatalogTrust.PublicKey(), release.Patch, release.Client, CatalogTrust.BuilderVersion, release.Architecture, workspace, git);
            if (download) resources = await MinecraftAssetPreparation.PrepareAsync(prepared, selection, minecraftOwnershipVerified, probe, progress);
            var result = await LocalBuildRunner.RunPreparedAsync(prepared, resources, selection, probe, progress, minecraft);
            var signed = result.SignedOutput ?? throw new InvalidOperationException("The build did not produce signed output.");
            builtOutput = signed;
            if (LocalPackageInstaller.CanInstall(selection.Target, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture, Environment.OSVersion.Version))
                InstallLocal.Visibility = Visibility.Visible;
            RecipeStatus.Text = $"Signed client: {signed.Client}. Public certificate: {signed.Certificate}. Installation guide: {signed.InstallationGuide}.";
            if (signed.Dependencies is { Count: > 0 })
                RecipeStatus.Text += $" Microsoft framework dependencies: {Path.GetDirectoryName(signed.Dependencies[0])}.";
            if (result.SignedOutput?.ResourceFramework is { } signedFramework)
                RecipeStatus.Text += $" Signed resource framework: {signedFramework}. It contains private Minecraft resources.";
            if (selection.ValidatedExistingResourceFramework is { } existing)
                RecipeStatus.Text += $" Requires existing resource framework {existing} on the target.";
            if (result.SoundWarning is { } warning)
                RecipeStatus.Text += " " + warning;
        }
        catch (Exception error) { RecipeStatus.Text = $"{RecipeStatus.Text} Build failed: {error.Message}"; }
        finally { buildActionRunning = false; UpdateBuildActions(); }
    }

    private async void InstallLocal_Click(object sender, RoutedEventArgs e)
    {
        if (builtOutput is not { } output) return;
        var dialog = new ContentDialog
        {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            Title = "Install Horizon on this PC?",
            Content = "Windows will trust your local signing certificate for packages from this builder and install the client and its required frameworks for your account. Windows may ask for administrator approval.",
            PrimaryButtonText = "Install", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        InstallLocal.IsEnabled = false;
        try
        {
            await LocalPackageInstaller.InstallAsync(output, new Progress<string>(message => RecipeStatus.Text = message));
            RecipeStatus.Text = "Horizon was installed for your Windows account.";
        }
        catch (Exception error) { RecipeStatus.Text = $"Installation did not complete: {error.Message}"; }
        finally { InstallLocal.IsEnabled = true; }
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
        NativeModsStatus.Text = $"{matching.Length} native mods list Minecraft {LocalRecipe.MinecraftVersion}; {selectedNativeMods.Count} selected. Native mods run code with the player's permissions. UWP compatibility varies by mod.";
        foreach (var mod in matching)
        {
            var item = new StackPanel { Spacing = 3 };
            var choice = new CheckBox { Content = $"{mod.Name} · {mod.Version}", IsChecked = selectedNativeMods.Contains(mod.Id) };
            choice.Checked += (_, _) => { selectedNativeMods.Add(mod.Id); UpdateNativeModCount(matching.Length); };
            choice.Unchecked += (_, _) => { selectedNativeMods.Remove(mod.Id); UpdateNativeModCount(matching.Length); };
            item.Children.Add(choice);
            item.Children.Add(new TextBlock { Text = mod.Summary, TextWrapping = TextWrapping.Wrap });
            NativeModsResults.Children.Add(item);
        }
    }
    private void UpdateNativeModCount(int visible) => NativeModsStatus.Text =
        $"{visible} native mods list Minecraft {LocalRecipe.MinecraftVersion}; {selectedNativeMods.Count} selected. Native mods run code with the player's permissions. UWP compatibility varies by mod.";
    private void CheckSetup_Click(object sender, RoutedEventArgs e)
    {
        Results.Children.Clear();
        var selection = CurrentSelection();
        Results.Children.Add(new TextBlock
        {
            Text = $"Requested target: {selection.RustTarget} | Features: {string.Join(", ", selection.Features)}",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var check in new SetupChecker(new SystemProbe()).Scan(selection.Target, splitRelease: true,
            unifiedToolchain: ReleaseSelection.SelectedItem is ReleaseChoice { Client: "0.1.1" }))
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
