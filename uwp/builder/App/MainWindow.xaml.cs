using Horizon.Builder.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace Horizon.Builder.App;

public sealed partial class MainWindow : Window
{
    private const int ReviewStep = 7;
    private const int BuildStep = 8;
    private static readonly string[] DependencyIds = ["git", "python", "rustup", "rust", "msvc", "sdk", "llvm", "ui", "vclibs", "minecraft-assets"];
    private CancellationTokenSource? builderUpdateCheckCancellation;

    public MainWindow()
    {
        InitializeComponent();
        Title = "MCHorizonUWP Builder";
        BuilderActivityLog.Record("App", "MCHorizonUWP Builder started.");
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Horizon.ico"));
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
        else
        {
            AppTitleBar.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
        }
        WizardPips.SelectedIndexChanged += WizardPips_SelectedIndexChanged;
        Closed += (_, _) =>
        {
            builderUpdateCheckCancellation?.Cancel();
            builderUpdateCheckCancellation?.Dispose();
            minecraftSignInCancellation?.Cancel();
            assetPrefetchCancellation?.Cancel();
        };
        Profile.SelectionChanged += Profile_SelectionChanged;
        ReleaseSelection.SelectionChanged += (_, _) =>
        {
            UpdateBuildActions();
            if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
            MaybeStartAssetPrefetch();
        };
        ResourceMode.SelectionChanged += ResourceMode_SelectionChanged;
        AutoBuild.Checked += (_, _) => _ = MaybeAutoStartAsync();
        AutoBuild.Unchecked += (_, _) => { if (wizardStep == ReviewStep && readinessReady) ReadinessStatus.Text = "Ready to build"; };
        Profile.SelectedIndex = (int)BuildSelection.DefaultTarget(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        PackageVersion.Text = LocalRecipe.DefaultPackageVersion;
        UpdateSignInActions(busy: false);
        if (MinecraftSignIn.HasSavedSession)
            MinecraftSignInStatus.Text = "A saved Microsoft sign-in is available on this PC.";
        UpdateResourceChoices();
        UpdateWizard();
        UpdateReleaseChoices();
        BuilderUpdateToggle.IsOn = BuilderUpdatePreferences.ReadEnabled();
        BuilderUpdateToggle.Toggled += BuilderUpdateToggle_Toggled;
        if (BuilderUpdateToggle.IsOn) StartBuilderUpdateCheck();
        else BuilderUpdateStatus.Text = "Builder update checks are turned off.";
    }

    private void BuilderUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
            BuilderUpdatePreferences.WriteEnabled(BuilderUpdateToggle.IsOn);
        }
        catch (Exception error)
        {
            BuilderActivityLog.Record("Updates", $"Could not save the update-check setting: {error.Message}");
            BuilderUpdateStatus.Text = "The update-check setting could not be saved.";
        }

        if (BuilderUpdateToggle.IsOn) StartBuilderUpdateCheck();
        else
        {
            builderUpdateCheckCancellation?.Cancel();
            BuilderUpdateLink.Visibility = Visibility.Collapsed;
            BuilderUpdateStatus.Text = "Builder update checks are turned off.";
            BuilderActivityLog.Record("Updates", "Builder update checks were turned off.");
        }
    }

    private void StartBuilderUpdateCheck()
    {
        builderUpdateCheckCancellation?.Cancel();
        builderUpdateCheckCancellation?.Dispose();
        builderUpdateCheckCancellation = new CancellationTokenSource();
        BuilderUpdateStatus.Text = "Checking for builder updates…";
        _ = CheckForBuilderUpdateAsync(builderUpdateCheckCancellation.Token);
    }

    private async Task CheckForBuilderUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var update = await BuilderUpdateChecker.CheckAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || !BuilderUpdateToggle.IsOn) return;
            if (update is null)
            {
                BuilderUpdateStatus.Text = "The latest MCHorizonUWP Builder release is installed.";
                BuilderActivityLog.Record("Updates", "No newer stable builder release was found.");
                return;
            }

            BuilderUpdateStatus.Text = $"MCHorizonUWP Builder {update.Version} is available.";
            BuilderUpdateLink.NavigateUri = update.ReleaseUri;
            BuilderUpdateLink.Visibility = Visibility.Visible;
            BuilderActivityLog.Record("Updates", $"Builder {update.Version} is available: {update.ReleaseUri}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!BuilderUpdateToggle.IsOn) return;
            BuilderUpdateStatus.Text = "Builder updates could not be checked. You can continue using the builder.";
            BuilderActivityLog.Record("Updates", $"Update check unavailable: {error.Message}");
        }
    }
    private IReadOnlyList<NativeModListing> nativeMods = [];
    private readonly HashSet<string> selectedNativeMods = new(StringComparer.Ordinal);
    private CancellationTokenSource? minecraftSignInCancellation;
    private CompatibilityCatalog? catalog;
    private bool buildActionRunning;
    private bool navigationBusy;
    private bool dependencyRunning;
    private bool dependencyReady;
    private bool dependencyStarted;
    private bool updatingPips;
    private bool readinessRunning;
    private bool readinessReady;
    private bool readinessRefreshRequested;
    private bool autoStartScheduled;
    private int lastRenderedStep = -1;
    private int highestVisited;
    private int completedPages;
    private readonly HashSet<string> readyDependencies = new(StringComparer.Ordinal);
    private string? activeDependency;
    private string? dependencyArchitecture;
    private string? requestedDependencyArchitecture;
    private bool minecraftOwnershipVerified;
    private SignedPackageOutput? builtOutput;
    private readonly Dictionary<string, Task<PreparedRelease>> preparedReleaseTasks = new(StringComparer.Ordinal);
    private CancellationTokenSource? assetPrefetchCancellation;
    private Task? assetPrefetchTask;
    private bool assetPrefetchAttempted;
    private bool assetPrefetchSucceeded;
    private bool assetPrefetchRunning;
    private ActivityLogWindow? activityLogWindow;
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

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (navigationBusy) return;
        if (wizardStep == 0 && !dependencyStarted)
        {
            navigationBusy = true;
            UpdateWizard();
            try
            {
                DependencyHeader.Text = "Dependencies · checking installed tools…";
                DependencySummary.Text = "Checking what is already installed…";
                DependencyRing.Visibility = Visibility.Visible;
                DependencyRing.IsActive = true;
                var architecture = SelectedArchitecture;
                var missing = await Task.Run(() => DependencyProvisioner.MissingAsync(new SystemProbe(), architecture));
                DependencyRing.IsActive = false;
                DependencyRing.Visibility = Visibility.Collapsed;
                var absentTools = missing.Count;
                DependencyHeader.Text = $"Dependencies · {Math.Clamp(9 - absentTools, 0, 9)}/9 required tools found for {architecture}";
                if (missing.Count > 0)
                {
                    var content = new StackPanel { Spacing = 12 };
                    content.Children.Add(new TextBlock { Text = $"These components are missing for {architecture}:", TextWrapping = TextWrapping.Wrap });
                    content.Children.Add(new TextBlock { Text = string.Join("\n", missing.Select(item => "• " + item)), TextWrapping = TextWrapping.Wrap });
                    content.Children.Add(new TextBlock { Text = "Microsoft C++ files are downloaded directly from Microsoft, hash-verified, and extracted into a private per-user cache. The Visual Studio IDE and machine-wide Build Tools installer are not used.", TextWrapping = TextWrapping.Wrap });
                    content.Children.Add(new TextBlock { Text = "Only the selected architecture and shared host files are prepared. You can continue through setup while downloads run.", TextWrapping = TextWrapping.Wrap });
                    var terms = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    foreach (var (label, url) in new[] {
                        ("Windows SDK", "https://aka.ms/WinSDKLicenseURL"),
                        ("Microsoft C++ components", "https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution"),
                        ("Microsoft UI XAML", "https://github.com/microsoft/microsoft-ui-xaml/blob/main/LICENSE"),
                        ("LLVM/Clang", "https://llvm.org/LICENSE.txt"),
                        ("Git", "https://git-scm.com/about/free-and-open-source"),
                        ("Python", "https://docs.python.org/3/license.html"),
                        ("Rust", "https://www.rust-lang.org/policies/licenses") })
                        terms.Children.Add(new HyperlinkButton { Content = label, NavigateUri = new Uri(url) });
                    content.Children.Add(terms);
                    var dialog = new ContentDialog { XamlRoot = WelcomePage.XamlRoot, Title = "Install required dependencies?", Content = content,
                        PrimaryButtonText = "Install in background", CloseButtonText = "Not now", DefaultButton = ContentDialogButton.Primary };
                    if (await dialog.ShowAsync() == ContentDialogResult.Primary) BeginDependencySetup();
                    else
                    {
                        DependencySummary.Text = "Setup has not started. Select Next when you are ready.";
                        DependencyExpander.IsExpanded = true;
                        RetryDependencies.Visibility = Visibility.Collapsed;
                        return;
                    }
                }
                else BeginDependencySetup(); // Verify existing framework signatures in the background.
            }
            catch (Exception error)
            {
                DependencyRing.IsActive = false;
                DependencyRing.Visibility = Visibility.Collapsed;
                DependencyHeader.Text = "Dependencies · check failed";
                DependencySummary.Text = "Could not check dependencies: " + error.Message;
                DependencyExpander.IsExpanded = true;
                RetryDependencies.Visibility = Visibility.Collapsed;
                return;
            }
            finally { navigationBusy = false; UpdateWizard(); }
        }
        if (wizardStep < ReviewStep) wizardStep++;
        completedPages = Math.Max(completedPages, wizardStep);
        highestVisited = Math.Max(highestVisited, wizardStep);
        UpdateWizard();
    }

    private void WizardPips_SelectedIndexChanged(PipsPager sender, PipsPagerSelectedIndexChangedEventArgs args)
    {
        if (updatingPips || navigationBusy) return;
        if (buildActionRunning) { UpdateWizard(); return; }
        var selected = sender.SelectedPageIndex;
        if (selected <= highestVisited) wizardStep = selected;
        UpdateWizard();
    }

    private void RetryDependencies_Click(object sender, RoutedEventArgs e) => BeginDependencySetup();

    private void BeginDependencySetup()
    {
        requestedDependencyArchitecture = SelectedArchitecture;
        if (dependencyRunning) return;
        BuilderActivityLog.Record("Dependencies", $"Checking and preparing build tools for {requestedDependencyArchitecture}.");
        dependencyStarted = true;
        if (catalog is null && RefreshReleases.IsEnabled) _ = LoadCatalogAsync();
        dependencyReady = false;
        dependencyRunning = true;
        readyDependencies.Clear();
        foreach (var id in DependencyIds)
            ShowDependencyProgress(new(id, "Waiting", false, false));
        UpdateDependencyHeader();
        DependencyRing.Visibility = Visibility.Visible;
        DependencyRing.IsActive = true;
        RetryDependencies.Visibility = Visibility.Collapsed;
        DependencyExpander.IsExpanded = true;
        DependencySummary.Text = "Preparing dependencies in the background. You can continue through the wizard.";
        UpdateBuildActions();
        var progress = new Progress<DependencyProgress>(ShowDependencyProgress);
        _ = RunDependencySetupAsync(progress);
    }

    private async Task RunDependencySetupAsync(IProgress<DependencyProgress> progress)
    {
        try
        {
            while (true)
            {
                var architecture = requestedDependencyArchitecture ?? SelectedArchitecture;
                await Task.Run(() => DependencyProvisioner.PrepareAsync(progress, architecture));
                dependencyArchitecture = architecture;
                if (string.Equals(requestedDependencyArchitecture ?? SelectedArchitecture, architecture, StringComparison.Ordinal)) break;
                BuilderActivityLog.Record("Dependencies", $"Architecture changed during setup; preparing {requestedDependencyArchitecture} next.");
            }
            dependencyReady = true;
            DependencySummary.Text = assetPrefetchRunning
                ? $"Build tools are ready for {dependencyArchitecture}. Minecraft assets are still being prepared."
                : $"Build tools are ready for {dependencyArchitecture}.";
            BuilderActivityLog.Record("Dependencies", $"Build tools are ready for {dependencyArchitecture}.");
        }
        catch (Exception error)
        {
            if (activeDependency is { } active) ShowDependencyProgress(new(active, "Failed; see details above", false, false));
            DependencySummary.Text = "Dependency setup could not finish: " + error.Message;
            BuilderActivityLog.Record("Dependencies", "Setup failed: " + error.Message);
            RetryDependencies.Visibility = Visibility.Visible;
        }
        finally
        {
            dependencyRunning = false;
            UpdateBuildActions();
            UpdateDependencyHeader();
            if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
        }
    }

    private void ShowDependencyProgress(DependencyProgress update)
    {
        var controls = update.Id switch
        {
            "git" => (GitProgress, GitState), "python" => (PythonProgress, PythonState), "rustup" => (RustupProgress, RustupState),
            "rust" => (RustProgress, RustState), "msvc" => (MsvcProgress, MsvcState), "sdk" => (SdkProgress, SdkState), "llvm" => (LlvmProgress, LlvmState),
            "ui" => (UiProgress, UiState), "vclibs" => (VcProgress, VcState), "minecraft-assets" => (AssetProgress, AssetState),
            _ => (null, null)
        };
        if (controls.Item1 is null || controls.Item2 is null) return;
        BuilderActivityLog.Record("Dependency", $"{update.Id}: {update.Detail}");
        if (update.Complete) readyDependencies.Add(update.Id);
        else readyDependencies.Remove(update.Id);
        UpdateDependencyHeader();
        controls.Item1.IsIndeterminate = update.Running && update.Percent is null;
        controls.Item1.Value = update.Complete ? 100 : update.Percent ?? 0;
        controls.Item2.Text = update.Detail;
        ToolTipService.SetToolTip(controls.Item2, update.Detail);
        if (update.Running) { activeDependency = update.Id; DependencySummary.Text = update.Detail; }
        else if (activeDependency == update.Id) activeDependency = null;
        if (update.Id == "git" && update.Complete) MaybeStartAssetPrefetch();
    }

    private void UpdateDependencyHeader()
    {
        var architecture = requestedDependencyArchitecture ?? dependencyArchitecture ?? SelectedArchitecture;
        DependencyHeader.Text = $"Dependencies · {readyDependencies.Count}/{DependencyIds.Length} ready for {architecture}";
        var active = dependencyRunning || assetPrefetchRunning;
        DependencyRing.IsActive = active;
        DependencyRing.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWizard()
    {
        FrameworkElement[] pages = [WelcomePage, TargetPage, AccountPage, ReleasePage, FrameworkPage, ModsPage, PackageVersionPage, ReviewPage, BuildPage];
        string[] labels = ["Welcome", "Architecture", "Microsoft account", "Horizon release", "Resources", "Native mods", "Package version", "Review", "Build"];
        for (var i = 0; i < pages.Length; i++) pages[i].Visibility = i == wizardStep ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = $"Step {wizardStep + 1} of {pages.Length} · {labels[wizardStep]}";
        SummaryTitle.Text = labels[wizardStep];
        string[] summaries = [
            "Check the requirements and prepare the tools needed for your build.",
            "Choose the architecture of the device that will run Horizon.",
            "Sign in with the Microsoft account that owns Minecraft: Java Edition.",
            "Choose a compatible Horizon patch and UWP client release.",
            "Choose how the Minecraft resources are installed on the target.",
            "Choose built-in features and trusted Horizon native mods.",
            "Set the four-part Windows package version.",
            "Review the build and let the builder start when everything is ready.",
            "Follow the build and find your signed package and installation guide."
        ];
        SummaryDetail.Text = summaries[wizardStep];
        updatingPips = true;
        WizardPips.SelectedPageIndex = wizardStep;
        updatingPips = false;
        PageProgress.Value = completedPages;
        BackButton.IsEnabled = !navigationBusy && !buildActionRunning && wizardStep > 0;
        NextButton.IsEnabled = !navigationBusy && wizardStep < ReviewStep;
        if (wizardStep == ReviewStep && lastRenderedStep != ReviewStep) _ = RefreshReadinessAsync();
        lastRenderedStep = wizardStep;
    }

    private void WizardBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 760;
        SummaryColumn.Width = wide ? new GridLength(310) : new GridLength(1, GridUnitType.Star);
        ContentColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SummaryRow.Height = wide ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        ContentRow.Height = wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(PageScroller, wide ? 0 : 1);
        Grid.SetColumn(PageScroller, wide ? 1 : 0);
        SummaryCard.MaxHeight = wide ? double.PositiveInfinity : 260;
    }

    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateReleaseChoices();
        if (!dependencyStarted || string.Equals(dependencyArchitecture, SelectedArchitecture, StringComparison.Ordinal)) return;
        dependencyReady = false;
        BeginDependencySetup();
    }
    private void ResourceMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateResourceChoices();
    private void UpdateResourceChoices()
    {
        if (ResourceMode is null || ExistingFrameworkName is null || ResourceModeDescription is null) return;
        var reuse = ResourceMode.SelectedIndex == 1;
        ExistingFrameworkName.IsEnabled = reuse;
        ResourceModeDescription.Text = ResourceMode.SelectedIndex switch
        {
            0 => "Recommended: the builder downloads and prepares Minecraft 26.3, then creates a separate signed resource framework. Compatible client updates can reuse it.",
            1 => "Choose this only if the exact signed resource framework is already installed on the target. Enter its full package name below; no new Minecraft resource package is created.",
            2 => "The builder downloads and prepares Minecraft 26.3, then includes those resources in the client package. Each package is larger but can be installed on its own.",
            _ => "The builder prepares Minecraft 26.3 automatically after sign-in."
        };
        if (reuse)
        {
            assetPrefetchCancellation?.Cancel();
            AssetPrefetchStatus.Text = "Not needed: the selected framework is already installed on the target.";
            ShowDependencyProgress(new("minecraft-assets", "Not needed for this build", false, true, 100));
        }
        else
        {
            if (assetPrefetchTask is not { IsCompleted: false } && !assetPrefetchSucceeded) assetPrefetchAttempted = false;
            MaybeStartAssetPrefetch();
        }
    }

    private BuildSelection CurrentSelection() => new((TargetProfile)Profile.SelectedIndex,
        NativeTouchMod.IsChecked == true, PackageVersion.Text, false,
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
                : "The signed catalog was verified. Choose a compatible release.";
        }
        var matching = previous is null ? null : ReleaseSelection.Items.OfType<ReleaseChoice>()
            .FirstOrDefault(item => item.Patch == previous.Patch && item.Client == previous.Client && item.Architecture == previous.Architecture);
        ReleaseSelection.SelectedItem = matching ?? (ReleaseSelection.Items.Count > 0 ? ReleaseSelection.Items[0] : null);
        UpdateBuildActions();
    }

    private void UpdateBuildActions()
    {
        if (ReleaseSelection is null || RunRecipe is null) return;
        var available = !buildActionRunning && ReleaseSelection.SelectedItem is ReleaseChoice;
        RunRecipe.IsEnabled = available && minecraftOwnershipVerified && dependencyReady && readinessReady && !readinessRunning;
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
            MaybeStartAssetPrefetch();
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

    private void MaybeStartAssetPrefetch()
    {
        if (ResourceMode.SelectedIndex == 1)
        {
            AssetPrefetchStatus.Text = "Not needed: the selected framework is already installed on the target.";
            if (dependencyStarted) ShowDependencyProgress(new("minecraft-assets", "Not needed for this build", false, true, 100));
            return;
        }
        if (!minecraftOwnershipVerified)
        {
            AssetPrefetchStatus.Text = "Waiting for Microsoft sign-in and Minecraft ownership confirmation.";
            if (dependencyStarted) ShowDependencyProgress(new("minecraft-assets", "Waiting for sign-in", false, false));
            return;
        }
        if (assetPrefetchSucceeded) return;
        if (assetPrefetchTask is { IsCompleted: false } || assetPrefetchAttempted) return;
        if (ReleaseSelection.SelectedItem is not ReleaseChoice choice)
        {
            AssetPrefetchStatus.Text = "Ownership confirmed. Waiting for a compatible Horizon release before checking Minecraft assets.";
            if (dependencyStarted) ShowDependencyProgress(new("minecraft-assets", "Waiting for release selection", false, false));
            return;
        }
        SystemProbe.RefreshProcessPath();
        if (!readyDependencies.Contains("git") && new SystemProbe().FindOnPath("git.exe") is null)
        {
            AssetPrefetchStatus.Text = "Ownership confirmed. Waiting for Git before preparing the source and Minecraft assets.";
            if (dependencyStarted) ShowDependencyProgress(new("minecraft-assets", "Waiting for Git", false, false));
            return;
        }

        assetPrefetchAttempted = true;
        assetPrefetchRunning = true;
        assetPrefetchCancellation?.Dispose();
        assetPrefetchCancellation = new CancellationTokenSource();
        CancelAssetPrefetch.Visibility = Visibility.Visible;
        RetryAssetPrefetch.Visibility = Visibility.Collapsed;
        AssetPrefetchStatus.Text = "Preparing the selected Horizon release so Minecraft assets can start downloading.";
        ShowDependencyProgress(new("minecraft-assets", "Preparing Minecraft assets", true, false));
        var progress = new Progress<string>(message =>
        {
            AssetPrefetchStatus.Text = message;
            BuilderActivityLog.Record("Minecraft assets", message);
            ShowDependencyProgress(new("minecraft-assets", message, true, false));
        });
        assetPrefetchTask = PrefetchMinecraftAssetsAsync(choice, progress, assetPrefetchCancellation.Token);
    }

    private async Task PrefetchMinecraftAssetsAsync(ReleaseChoice choice, IProgress<string> progress, CancellationToken cancellationToken)
    {
        try
        {
            var prepared = await EnsurePreparedReleaseAsync(choice, progress, cancellationToken);
            var result = await MinecraftAssetPreparation.PrefetchAsync(prepared.SourceDirectory, minecraftOwnershipVerified,
                null, progress, cancellationToken);
            assetPrefetchSucceeded = true;
            AssetPrefetchStatus.Text = $"Minecraft 26.3 files are ready. Reused {result.LocalFilesReused} local files and downloaded {result.NetworkFilesFetched} files ({result.BytesFetched / (1024d * 1024):0} MiB).";
            ShowDependencyProgress(new("minecraft-assets", "Ready", false, true, 100));
            BuilderActivityLog.Record("Minecraft assets", "Minecraft 26.3 assets prefetched and verified.");
        }
        catch (OperationCanceledException)
        {
            AssetPrefetchStatus.Text = "Minecraft asset download canceled. Retry when you’re ready.";
            ShowDependencyProgress(new("minecraft-assets", "Canceled; retry when ready", false, false));
            BuilderActivityLog.Record("Minecraft assets", "Prefetch canceled by the user.");
        }
        catch (Exception error)
        {
            AssetPrefetchStatus.Text = "Minecraft assets could not be prefetched yet: " + error.Message;
            ShowDependencyProgress(new("minecraft-assets", "Failed; see details above", false, false));
            RetryAssetPrefetch.Visibility = Visibility.Visible;
            BuilderActivityLog.Record("Minecraft assets", "Prefetch failed: " + error.Message);
        }
        finally
        {
            assetPrefetchRunning = false;
            CancelAssetPrefetch.Visibility = Visibility.Collapsed;
            if (ResourceMode.SelectedIndex == 1)
            {
                AssetPrefetchStatus.Text = "Not needed: the selected framework is already installed on the target.";
                ShowDependencyProgress(new("minecraft-assets", "Not needed for this build", false, true, 100));
            }
            else if (!minecraftOwnershipVerified)
            {
                AssetPrefetchStatus.Text = "Waiting for Microsoft sign-in and Minecraft ownership confirmation.";
                ShowDependencyProgress(new("minecraft-assets", "Waiting for sign-in", false, false));
                RetryAssetPrefetch.Visibility = Visibility.Collapsed;
            }
            else if (!assetPrefetchSucceeded && minecraftOwnershipVerified)
                RetryAssetPrefetch.Visibility = Visibility.Visible;
            else RetryAssetPrefetch.Visibility = Visibility.Collapsed;
            assetPrefetchCancellation?.Dispose();
            assetPrefetchCancellation = null;
            assetPrefetchTask = null;
            UpdateDependencyHeader();
            if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
        }
    }

    private Task<PreparedRelease> EnsurePreparedReleaseAsync(ReleaseChoice choice, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var key = ReleaseKey(choice);
        if (preparedReleaseTasks.TryGetValue(key, out var existing))
        {
            if (!existing.IsCanceled && !existing.IsFaulted) return existing;
            preparedReleaseTasks.Remove(key);
        }
        var task = PrepareReleaseSourceAsync(choice, progress, cancellationToken);
        preparedReleaseTasks.Add(key, task);
        return task;
    }

    private static string ReleaseKey(ReleaseChoice choice) => $"{choice.Patch}|{choice.Client}|{choice.Architecture}";

    private async Task<PreparedRelease> PrepareReleaseSourceAsync(ReleaseChoice choice, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        SystemProbe.RefreshProcessPath();
        var git = new SystemProbe().FindOnPath("git.exe") ?? throw new InvalidOperationException("Git is still being prepared.");
        var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".huwp", "b");
        Directory.CreateDirectory(baseDirectory);
        var workspace = Path.Combine(baseDirectory, "r" + Guid.NewGuid().ToString("N")[..15]);
        progress?.Report("Downloading and verifying the selected Horizon release source.");
        using var transport = new CatalogTransport();
        return await ReleaseSource.PrepareAsync(transport, CatalogTrust.CatalogUri, CatalogTrust.SignatureUri,
            CatalogTrust.PublicKey(), choice.Patch, choice.Client, CatalogTrust.BuilderVersion, choice.Architecture, workspace, git, cancellationToken);
    }

    private async void StartMinecraftSignIn_Click(object sender, RoutedEventArgs e)
    {
        minecraftOwnershipVerified = false;
        UpdateBuildActions();
        UpdateSignInActions(busy: true);
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
            UpdateSignInActions(busy: false);
            MaybeStartAssetPrefetch();
            if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
        }
    }

    private async void UseSavedMinecraftSignIn_Click(object sender, RoutedEventArgs e)
    {
        minecraftOwnershipVerified = false;
        UpdateBuildActions();
        UpdateSignInActions(busy: true);
        MinecraftSignInStatus.Text = "Refreshing the saved Microsoft sign-in and checking ownership…";
        using var signIn = new MinecraftSignIn();
        using var cancellation = new CancellationTokenSource();
        minecraftSignInCancellation = cancellation;
        try
        {
            var owns = await signIn.CompleteSavedOwnershipCheckAsync(cancellation.Token);
            minecraftOwnershipVerified = owns;
            MinecraftSignInStatus.Text = owns
                ? "Minecraft: Java Edition ownership confirmed with your saved Microsoft sign-in."
                : "The saved account does not have Minecraft: Java Edition.";
        }
        catch (OperationCanceledException) { MinecraftSignInStatus.Text = "Saved sign-in cancelled."; }
        catch (Exception error) { MinecraftSignInStatus.Text = $"Saved sign-in could not be completed: {error.Message}"; }
        finally
        {
            minecraftSignInCancellation = null;
            UpdateSignInActions(busy: false);
            UpdateBuildActions();
            MaybeStartAssetPrefetch();
            if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
        }
    }

    private void SignOutMinecraft_Click(object sender, RoutedEventArgs e)
    {
        assetPrefetchCancellation?.Cancel();
        MinecraftSignIn.ForgetSavedSession();
        minecraftOwnershipVerified = false;
        MinecraftSignInStatus.Text = "Signed out. The saved Microsoft sign-in was removed from this PC.";
        AssetPrefetchStatus.Text = "Waiting for Microsoft sign-in and Minecraft ownership confirmation.";
        if (dependencyStarted && ResourceMode.SelectedIndex != 1)
            ShowDependencyProgress(new("minecraft-assets", "Waiting for sign-in", false, false));
        RetryAssetPrefetch.Visibility = Visibility.Collapsed;
        UpdateSignInActions(busy: false);
        UpdateBuildActions();
        if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
    }

    private void UpdateSignInActions(bool busy)
    {
        var saved = MinecraftSignIn.HasSavedSession;
        StartMinecraftSignIn.Visibility = !busy && !minecraftOwnershipVerified ? Visibility.Visible : Visibility.Collapsed;
        StartMinecraftSignIn.IsEnabled = !busy;
        UseSavedMinecraftSignIn.Visibility = !busy && !minecraftOwnershipVerified && saved ? Visibility.Visible : Visibility.Collapsed;
        UseSavedMinecraftSignIn.IsEnabled = !busy;
        ForgetSavedMinecraft.Visibility = !busy && !minecraftOwnershipVerified && saved ? Visibility.Visible : Visibility.Collapsed;
        ForgetSavedMinecraft.IsEnabled = !busy;
        SignOutMinecraft.Visibility = !busy && minecraftOwnershipVerified ? Visibility.Visible : Visibility.Collapsed;
        SignOutMinecraft.IsEnabled = !busy;
        CancelMinecraftSignIn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelMinecraftSignIn.IsEnabled = busy;
    }

    private void CancelMinecraftSignIn_Click(object sender, RoutedEventArgs e) => minecraftSignInCancellation?.Cancel();

    private void CancelAssetPrefetch_Click(object sender, RoutedEventArgs e) => assetPrefetchCancellation?.Cancel();
    private void RetryAssetPrefetch_Click(object sender, RoutedEventArgs e)
    {
        assetPrefetchAttempted = false;
        assetPrefetchSucceeded = false;
        MaybeStartAssetPrefetch();
    }

    private void ViewActivityLog_Click(object sender, RoutedEventArgs e)
    {
        if (activityLogWindow is not null)
        {
            activityLogWindow.Activate();
            return;
        }

        var logWindow = new ActivityLogWindow();
        activityLogWindow = logWindow;
        logWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(activityLogWindow, logWindow)) activityLogWindow = null;
        };
        logWindow.Activate();
    }

    private async Task RefreshReadinessAsync()
    {
        if (wizardStep != ReviewStep || buildActionRunning) return;
        if (readinessRunning) { readinessRefreshRequested = true; return; }
        readinessRunning = true;
        readinessReady = false;
        ReadinessRing.Visibility = Visibility.Visible;
        ReadinessRing.IsActive = true;
        ReadinessStatus.Text = "Checking your setup and build plan…";
        UpdateBuildActions();
        RecipeSteps.Children.Clear();
        Results.Children.Clear();
        try
        {
            string? resources = null;
            string? minecraft = null;
            var selection = CurrentSelection();
            var release = ReleaseSelection.SelectedItem as ReleaseChoice ?? throw new InvalidOperationException("Choose a compatible catalog release.");
            var plan = LocalRecipe.Plan(selection);
            var download = ResourceMode.SelectedIndex != 1;
            if (!download) LocalRecipe.ValidateResourceSelection(selection, resources, minecraft);
            var probe = new SystemProbe();
            SystemProbe.RefreshProcessPath();
            var checks = new SetupChecker(probe).Scan(selection.Target, splitRelease: true, unifiedToolchain: true, compactToolchain: true);
            RenderSetupChecks(checks);
            var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe),
                splitRelease: true, unifiedToolchain: true, compactToolchain: true);
            foreach (var blocker in blockers) RecipeSteps.Children.Add(new TextBlock { Text = blocker, TextWrapping = TextWrapping.Wrap });
            if (download) RecipeSteps.Children.Add(new TextBlock { Text = "Download and prepare Minecraft resources in the private build workspace.", TextWrapping = TextWrapping.Wrap });
            foreach (var step in plan.Steps) RecipeSteps.Children.Add(new TextBlock { Text = step.Name == "Snapshot pinned source" ? "Download and verify the selected release source" : step.Name, TextWrapping = TextWrapping.Wrap });
            if (selection.RegistryNativeMods is { Count: > 0 } mods)
                RecipeSteps.Children.Add(new TextBlock { Text = $"Selected native mods: {string.Join(", ", mods)}. Their compatibility is checked while building.", TextWrapping = TextWrapping.Wrap });
            RecipeSteps.Children.Add(new TextBlock { Text = "The client and any new resource framework will be signed with your local certificate. The public certificate and installation guide are included.", TextWrapping = TextWrapping.Wrap });
            var resourceChoice = ResourceMode.SelectedIndex switch { 0 => "Separate resource framework", 1 => "Existing resource framework", _ => "Resources bundled with the client" };
            var selectedMods = new List<string>();
            if (NativeTouchMod.IsChecked == true) selectedMods.Add("Touch controls");
            if (NativeDeviceVibration.IsChecked == true) selectedMods.Add("Device vibration");
            selectedMods.AddRange(selectedNativeMods.OrderBy(id => id, StringComparer.Ordinal));
            var modsSummary = selectedMods.Count == 0 ? "None" : string.Join(", ", selectedMods);
            var targetLabel = (Profile.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? SelectedArchitecture;
            BuildSummary.Text = $"Target: {targetLabel}\nHorizon patch: {release.Patch}  ·  UWP client: {release.Client}\nResources: {resourceChoice}\nNative mods: {modsSummary}\nPackage version: {selection.PackageVersion}";
            if (!minecraftOwnershipVerified) ReadinessStatus.Text = "Sign in to confirm Minecraft ownership before building.";
            else if (!dependencyReady || (download && !assetPrefetchSucceeded)) ReadinessStatus.Text = dependencyRunning || assetPrefetchRunning
                ? "You are set — waiting for dependencies…" : "Dependency setup needs attention. Open Dependencies to see details or retry.";
            else if (blockers.Count > 0 || checks.Any(check => check.Status == CheckStatus.Missing))
                ReadinessStatus.Text = "Some setup checks need attention. Open the sections below for details.";
            else
            {
                readinessReady = true;
                ReadinessStatus.Text = "Ready to build";
            }
        }
        catch (Exception error) { BuildSummary.Text = ""; ReadinessStatus.Text = $"Could not validate the build: {error.Message}"; }
        finally
        {
            readinessRunning = false;
            var waiting = wizardStep == ReviewStep && (dependencyRunning || assetPrefetchRunning) && !readinessReady;
            ReadinessRing.IsActive = waiting;
            ReadinessRing.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
            UpdateBuildActions();
            if (readinessRefreshRequested)
            {
                readinessRefreshRequested = false;
                if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
            }
            else if (readinessReady) _ = MaybeAutoStartAsync();
        }
    }

    private async Task MaybeAutoStartAsync()
    {
        if (autoStartScheduled || wizardStep != ReviewStep || !readinessReady || AutoBuild.IsChecked != true || buildActionRunning) return;
        autoStartScheduled = true;
        try
        {
            ReadinessStatus.Text = "Ready to build — starting in 3 seconds…";
            await Task.Delay(3000);
            if (wizardStep == ReviewStep && readinessReady && AutoBuild.IsChecked == true && !buildActionRunning)
                await StartBuildAsync();
        }
        finally { autoStartScheduled = false; }
    }

    private async void RunRecipe_Click(object sender, RoutedEventArgs e) => await StartBuildAsync();

    private async Task StartBuildAsync()
    {
        if (!readinessReady || !dependencyReady || !minecraftOwnershipVerified || buildActionRunning) return;
        builtOutput = null;
        InstallLocal.Visibility = Visibility.Collapsed;
        buildActionRunning = true;
        wizardStep = BuildStep;
        completedPages = Math.Max(completedPages, BuildStep);
        highestVisited = Math.Max(highestVisited, BuildStep);
        string? preparedReleaseKey = null;
        UpdateWizard();
        UpdateBuildActions();
        try
        {
            if (!minecraftOwnershipVerified) throw new InvalidOperationException("Sign in with an account that owns Minecraft: Java Edition before building.");
            string? resources = null;
            string? minecraft = null;
            var selection = CurrentSelection();
            var release = ReleaseSelection.SelectedItem as ReleaseChoice ?? throw new InvalidOperationException("Choose a compatible catalog release.");
            preparedReleaseKey = ReleaseKey(release);
            var download = ResourceMode.SelectedIndex != 1;
            if (!download) LocalRecipe.ValidateResourceSelection(selection, resources, minecraft);
            var progress = new Progress<string>(message =>
            {
                RecipeStatus.Text = message;
                BuilderActivityLog.Record("Build", message);
            });
            SystemProbe.RefreshProcessPath();
            var probe = new SystemProbe();
            var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe),
                splitRelease: true, unifiedToolchain: true, compactToolchain: true);
            if (blockers.Count > 0) throw new InvalidOperationException("Dependency setup is incomplete. " + string.Join(" ", blockers));
            if (assetPrefetchTask is { IsCompleted: false } prefetchTask) await prefetchTask;
            var prepared = await EnsurePreparedReleaseAsync(release, progress, CancellationToken.None);
            if (download) resources = await MinecraftAssetPreparation.PrepareAsync(prepared, selection, minecraftOwnershipVerified,
                probe, progress, minecraft);
            var result = await LocalBuildRunner.RunPreparedAsync(prepared, resources, selection, probe, progress, minecraft);
            var signed = result.SignedOutput ?? throw new InvalidOperationException("The build did not produce signed output.");
            builtOutput = signed;
            completedPages = BuildStep;
            UpdateWizard();
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
        catch (Exception error)
        {
            RecipeStatus.Text = $"{RecipeStatus.Text} Build failed: {error.Message}";
            BuilderActivityLog.Record("Build", "Build failed: " + error.Message);
        }
        finally
        {
            // RunPreparedAsync modifies its isolated source tree. Do not reuse it for a later build.
            if (preparedReleaseKey is not null) preparedReleaseTasks.Remove(preparedReleaseKey);
            buildActionRunning = false;
            UpdateWizard();
            UpdateBuildActions();
        }
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
    private async void BrowseNativeMods_Click(object sender, RoutedEventArgs e)
    {
        BrowseNativeMods.IsEnabled = false;
        NativeModsStatus.Text = "Loading Horizon’s signed native mod registry…";
        try
        {
            if (nativeMods.Count == 0) nativeMods = await NativeModIndex.FetchAsync();
            var picker = new NativeModsDialog(nativeMods, selectedNativeMods)
            {
                Width = Math.Max(420, Math.Min(1000, WindowRoot.ActualWidth - 48)),
                Height = Math.Max(360, Math.Min(620, WindowRoot.ActualHeight - 220))
            };
            var dialog = new ContentDialog
            {
                XamlRoot = ModsPage.XamlRoot,
                Title = "Horizon native mods",
                Content = picker,
                PrimaryButtonText = "Apply selection",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                MaxWidth = Math.Min(1100, Math.Max(460, WindowRoot.ActualWidth - 32)),
                MaxHeight = Math.Min(820, Math.Max(480, WindowRoot.ActualHeight - 32))
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                selectedNativeMods.Clear();
                selectedNativeMods.UnionWith(picker.SelectedIds);
                UpdateNativeModCount();
                if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
            }
        }
        catch (Exception error) { NativeModsStatus.Text = $"Could not load native mods: {error.Message}"; }
        finally { BrowseNativeMods.IsEnabled = true; }
    }

    private void ClearNativeMods_Click(object sender, RoutedEventArgs e)
    {
        selectedNativeMods.Clear();
        UpdateNativeModCount();
        if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
    }

    private void UpdateNativeModCount()
    {
        var names = nativeMods.Where(mod => selectedNativeMods.Contains(mod.Id)).OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(mod => mod.Name).ToArray();
        NativeModsStatus.Text = names.Length == 0 ? "No Horizon repository mods selected."
            : $"{names.Length} Horizon native mod(s) selected: {string.Join(", ", names)}";
        ClearNativeMods.Visibility = names.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RenderSetupChecks(IReadOnlyList<SetupCheck> checks)
    {
        Results.Children.Clear();
        foreach (var check in checks)
        {
            var panel = new StackPanel { Spacing = 4 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var waiting = check.Status == CheckStatus.Missing && !dependencyReady;
            var marker = new TextBlock { Text = waiting ? "…" : check.Status switch { CheckStatus.Found => "✓", CheckStatus.Missing => "✕", _ => "ℹ" },
                FontWeight = Microsoft.UI.Text.FontWeights.Bold };
            if (check.Status == CheckStatus.Found) marker.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
            heading.Children.Add(marker);
            heading.Children.Add(new TextBlock { Text = check.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(heading);
            var detail = waiting ? (dependencyRunning ? "Waiting for automatic dependency setup." : "Open Dependencies above to start or retry setup.") : check.Detail;
            panel.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });
            if (!waiting && check.Instructions is { } instructions)
                panel.Children.Add(new HyperlinkButton { Content = "Open publisher instructions", NavigateUri = instructions });
            Results.Children.Add(panel);
        }
    }
}
