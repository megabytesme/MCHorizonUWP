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
    private const int FinishedStep = 9;
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
        ReleaseSelection.SelectionChanged += (_, _) =>
        {
            var currentCommit = SelectedHorizonCommit();
            if (currentCommit is not null)
            {
                if (observedHorizonCommit is not null && !observedHorizonCommit.Equals(currentCommit, StringComparison.OrdinalIgnoreCase))
                    ResetNativeModSelectionForHorizonChange();
                observedHorizonCommit = currentCommit;
            }
            UpdateBuildActions();
            UpdateWizard();
            if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
            MaybeStartAssetPrefetch();
        };
        ResourceMode.SelectionChanged += ResourceMode_SelectionChanged;
        ExistingFrameworkName.TextChanged += (_, _) => UpdateWizard();
        PackageVersion.TextChanged += (_, _) =>
        {
            if (!packageVersionUpdating) packageVersionCustomized = !string.Equals(PackageVersion.Text, suggestedPackageVersion, StringComparison.Ordinal);
            UpdateBuildActions();
            UpdateWizard();
        };
        AutoBuild.Checked += (_, _) => _ = MaybeAutoStartAsync();
        AutoBuild.Unchecked += (_, _) => { if (wizardStep == ReviewStep && readinessReady) ReadinessStatus.Text = "Ready to build"; };
        var defaultTarget = BuildSelection.DefaultTarget(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        Profile.SelectedIndex = (int)defaultTarget;
        ArchX64.IsChecked = defaultTarget == TargetProfile.DesktopX64;
        ArchArm64.IsChecked = defaultTarget == TargetProfile.SurfaceProX;
        ArchArm32.IsChecked = defaultTarget == TargetProfile.WindowsMobile;
        ArchX86.IsChecked = defaultTarget == TargetProfile.DesktopX86;
        initializingArchitectures = false;
        BuildSpeedReview.SelectedIndex = 0;
        BuildSpeedBuild.SelectedIndex = 0;
        SetSuggestedPackageVersion(LocalRecipe.PackageVersionFor(DateTimeOffset.UtcNow));
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
            await ShowBuilderUpdateDialogAsync(update);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!BuilderUpdateToggle.IsOn) return;
            BuilderUpdateStatus.Text = "Builder updates could not be checked. You can continue using the builder.";
            BuilderActivityLog.Record("Updates", $"Update check unavailable: {error.Message}");
        }
    }

    private async Task ShowBuilderUpdateDialogAsync(BuilderUpdate update)
    {
        var changelog = string.IsNullOrWhiteSpace(update.Changelog)
            ? "No release notes were provided. Open the GitHub release for details."
            : update.Changelog.Trim();
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"MCHorizonUWP Builder {update.Version} is available. Review its changes before downloading it.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock { Text = "Release notes", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = changelog, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
        });
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = "Builder update available",
            Content = content,
            PrimaryButtonText = "View release",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Windows.System.Launcher.LaunchUriAsync(update.ReleaseUri);
    }
    private IReadOnlyList<NativeModListing> nativeMods = [];
    private NativeModPreflightResult? nativeModPreflight;
    private readonly HashSet<string> selectedNativeMods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> selectedNativeModVersions = new(StringComparer.Ordinal);
    private string? observedHorizonCommit;
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
    private bool initializingArchitectures = true;
    private bool synchronizingBuildSpeed;
    private bool packageVersionUpdating;
    private bool packageVersionCustomized;
    private bool packageVersionResolvedAgainstInstalled;
    private string suggestedPackageVersion = "";
    private bool cargoCompileStarted;
    private int lastRenderedStep = -1;
    private int highestVisited;
    private int completedPages;
    private readonly HashSet<string> readyDependencies = new(StringComparer.Ordinal);
    private string? activeDependency;
    private string[]? requestedDependencyArchitectures;
    private bool minecraftOwnershipVerified;
    private readonly List<(TargetProfile Target, SignedPackageOutput Output)> builtOutputs = [];
    private readonly Dictionary<string, Task<PreparedRelease>> preparedReleaseTasks = new(StringComparer.Ordinal);
    private CancellationTokenSource? assetPrefetchCancellation;
    private Task? assetPrefetchTask;
    private int assetPrefetchAttemptGeneration;
    private bool assetPrefetchAttempted;
    private bool assetPrefetchSucceeded;
    private bool assetPrefetchRunning;
    private string? assetPrefetchReadySummary;
    private int builtResourceMode = -1;
    private string? builtExistingResourceFrameworkName;
    private ActivityLogWindow? activityLogWindow;
    private readonly Dictionary<string, BuildProgressRow> buildProgressRows = new(StringComparer.Ordinal);
    private BuildProgressRow? activeBuildProgressRow;
    private string? activeBuildArchitecture;
    private sealed record BuildProgressRow(string Key, ProgressRing Ring, TextBlock CompleteMark,
        TextBlock State, TextBlock Detail, ProgressBar Progress);
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
        if (navigationBusy || !CanAdvanceWizardStep(wizardStep)) return;
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
                var architectures = SelectedArchitectures();
                if (architectures.Count == 0) throw new InvalidOperationException("Select at least one target architecture.");
                var missingByArchitecture = await Task.Run(async () =>
                {
                    var results = new List<(string Architecture, IReadOnlyList<string> Missing)>();
                    foreach (var architecture in architectures)
                        results.Add((architecture, await DependencyProvisioner.MissingAsync(new SystemProbe(), architecture)));
                    return results;
                });
                var missing = missingByArchitecture.SelectMany(item => item.Missing.Select(name => $"{item.Architecture}: {name}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                DependencyRing.IsActive = false;
                DependencyRing.Visibility = Visibility.Collapsed;
                DependencyHeader.Text = $"Dependencies · {missing.Length} missing across {string.Join(", ", architectures)}";
                if (missing.Length > 0)
                {
                    var content = new StackPanel { Spacing = 12 };
                    content.Children.Add(new TextBlock { Text = $"These components are missing for the selected targets ({string.Join(", ", architectures)}):", TextWrapping = TextWrapping.Wrap });
                    content.Children.Add(new TextBlock { Text = string.Join("\n", missing.Select(item => "• " + item)), TextWrapping = TextWrapping.Wrap });
                    content.Children.Add(new TextBlock { Text = "Microsoft C++ files are downloaded directly from Microsoft, hash-verified, and extracted into a private per-user cache. The Visual Studio IDE and machine-wide Build Tools installer are not used.", TextWrapping = TextWrapping.Wrap });
                    content.Children.Add(new TextBlock { Text = "Only the selected architectures and shared host files are prepared. You can continue through setup while downloads run.", TextWrapping = TextWrapping.Wrap });
                    var links = new (string Label, string Url)[] {
                        ("Windows SDK", "https://aka.ms/WinSDKLicenseURL"),
                        ("Microsoft C++ components", "https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution"),
                        ("Microsoft UI XAML", "https://github.com/microsoft/microsoft-ui-xaml/blob/main/LICENSE"),
                        ("LLVM/Clang", "https://llvm.org/LICENSE.txt"),
                        ("Git", "https://git-scm.com/about/free-and-open-source"),
                        ("Python", "https://docs.python.org/3/license.html"),
                        ("Rust", "https://www.rust-lang.org/policies/licenses") };
                    var terms = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
                    for (var column = 0; column < 3; column++) terms.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    for (var row = 0; row < (links.Length + 2) / 3; row++) terms.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (var index = 0; index < links.Length; index++)
                    {
                        var (label, url) = links[index];
                        var link = new HyperlinkButton { Content = label, NavigateUri = new Uri(url), HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Left };
                        Grid.SetRow(link, index / 3);
                        Grid.SetColumn(link, index % 3);
                        terms.Children.Add(link);
                    }
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
        requestedDependencyArchitectures = SelectedArchitectures().ToArray();
        if (dependencyRunning) return;
        BuilderActivityLog.Record("Dependencies", $"Checking and preparing build tools for {string.Join(", ", requestedDependencyArchitectures)}.");
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
                var architectures = requestedDependencyArchitectures ?? SelectedArchitectures().ToArray();
                foreach (var architecture in architectures)
                {
                    await Task.Run(() => DependencyProvisioner.PrepareAsync(progress, architecture));
                }
                var latest = requestedDependencyArchitectures ?? SelectedArchitectures().ToArray();
                if (architectures.SequenceEqual(latest, StringComparer.Ordinal)) break;
                BuilderActivityLog.Record("Dependencies", $"Architecture selection changed during setup; preparing {string.Join(", ", latest)} next.");
            }
            dependencyReady = true;
            var readyFor = string.Join(", ", requestedDependencyArchitectures ?? SelectedArchitectures().ToArray());
            DependencySummary.Text = assetPrefetchRunning
                ? $"Build tools are ready for {readyFor}. Minecraft assets are still being prepared."
                : $"Build tools are ready for {readyFor}.";
            BuilderActivityLog.Record("Dependencies", $"Build tools are ready for {readyFor}.");
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
        var architectures = requestedDependencyArchitectures ?? SelectedArchitectures().ToArray();
        DependencyHeader.Text = $"Dependencies · {readyDependencies.Count}/{DependencyIds.Length} ready · {string.Join(", ", architectures)}";
        var active = dependencyRunning || assetPrefetchRunning;
        DependencyRing.IsActive = active;
        DependencyRing.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWizard()
    {
        FrameworkElement[] pages = [WelcomePage, TargetPage, AccountPage, ReleasePage, FrameworkPage, ModsPage, PackageVersionPage, ReviewPage, BuildPage, FinishedPage];
        string[] labels = ["Welcome", "Architecture", "Microsoft account", "Horizon release", "Resources", "Native mods", "Package version", "Review", "Build", "Finished"];
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
            "Follow the build and find your signed package and installation guide.",
            "Your signed packages are ready. Review the architecture folders and installation options."
        ];
        SummaryDetail.Text = summaries[wizardStep];
        updatingPips = true;
        WizardPips.SelectedPageIndex = wizardStep;
        updatingPips = false;
        PageProgress.Value = completedPages;
        BackButton.Visibility = wizardStep == FinishedStep ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Visibility = wizardStep == FinishedStep ? Visibility.Collapsed : Visibility.Visible;
        BackButton.IsEnabled = !navigationBusy && !buildActionRunning && wizardStep > 0;
        NextButton.IsEnabled = !navigationBusy && wizardStep < ReviewStep && CanAdvanceWizardStep(wizardStep);
        if (wizardStep == ReviewStep && lastRenderedStep != ReviewStep) _ = RefreshReadinessAsync();
        lastRenderedStep = wizardStep;
    }

    private bool CanAdvanceWizardStep(int step)
    {
        if (step == 0) return true;
        if (step == 1) return SelectedTargets().Count > 0;
        if (step == 2) return minecraftOwnershipVerified;
        if (step == 3) return ReleaseSelection.SelectedItem is ReleaseChoice;
        if (step == 4)
        {
            if (ResourceMode.SelectedIndex != 1) return ResourceMode.SelectedIndex is 0 or 2;
            try { return CurrentSelection(SelectedTargets().FirstOrDefault()).ValidatedExistingResourceFramework is not null; }
            catch (ArgumentException) { return false; }
        }
        if (step == 5) return NativeModSelectionReady();
        if (step == 6)
        {
            try { _ = CurrentSelection(SelectedTargets().FirstOrDefault()).ValidatedPackageVersion; return true; }
            catch (ArgumentException) { return false; }
        }
        if (step == ReviewStep) return readinessReady;
        return false;
    }

    private bool NativeModSelectionReady()
    {
        if (selectedNativeMods.Count == 0) return true;
        var commit = SelectedHorizonCommit();
        if (nativeModPreflight is null || commit is null
            || !nativeModPreflight.HorizonCommit.Equals(commit, StringComparison.OrdinalIgnoreCase)
            || selectedNativeModVersions.Count != selectedNativeMods.Count
            || selectedNativeMods.Any(id => !selectedNativeModVersions.ContainsKey(id))) return false;
        var selected = new List<(string Id, NativeModListing Listing, NativeModCandidate Version)>();
        foreach (var id in selectedNativeMods)
        {
            var listing = nativeModPreflight.Mods.SingleOrDefault(mod => mod.Id == id);
            var version = listing?.CompatibleVersions?.FirstOrDefault(candidate => candidate.Version == selectedNativeModVersions[id]);
            if (listing is null || !listing.IsCompatible || version is null) return false;
            selected.Add((id, listing, version));
        }
        return selected.Select(item => MixinsRevision(item.Version)).Distinct(StringComparer.Ordinal).Count() == 1;
    }

    private static string MixinsRevision(NativeModCandidate candidate)
    {
        var parts = candidate.Api.Split('/');
        return parts.Length == 3 ? parts[2] : "";
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

    private void Architecture_Checked(object sender, RoutedEventArgs e) => ArchitectureSelectionChanged(sender);
    private void Architecture_Unchecked(object sender, RoutedEventArgs e) => ArchitectureSelectionChanged(sender);

    private void ArchitectureSelectionChanged(object sender)
    {
        if (initializingArchitectures) return;
        var targets = SelectedTargets();
        if (targets.Count == 0 && sender is CheckBox changed)
        {
            initializingArchitectures = true;
            changed.IsChecked = true;
            initializingArchitectures = false;
            targets = SelectedTargets();
        }
        if (targets.Count == 0) return;
        Profile.SelectedIndex = (int)targets[0];
        UpdateReleaseChoices();
        UpdateBuildActions();
        UpdateWizard();
        if (dependencyStarted)
        {
            dependencyReady = false;
            BeginDependencySetup();
        }
        if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
    }
    private void ResourceMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateResourceChoices();
        UpdateWizard();
    }
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
            assetPrefetchAttemptGeneration++;
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

    private BuildSelection CurrentSelection(TargetProfile? target = null) => new(target ?? (TargetProfile)Profile.SelectedIndex,
        NativeTouchMod.IsChecked == true, PackageVersion.Text, false,
        ResourceMode.SelectedIndex == 0, NativeDeviceVibration.IsChecked == true,
        HorizonIdentity.ClientId, ReuseResourceFramework: ResourceMode.SelectedIndex == 1,
        ExistingResourceFramework: ResourceMode.SelectedIndex == 1 ? ExistingFrameworkName.Text : "",
        RegistryNativeMods: selectedNativeMods.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
        RegistryNativeModVersions: selectedNativeMods.Where(selectedNativeModVersions.ContainsKey)
            .ToDictionary(id => id, id => selectedNativeModVersions[id], StringComparer.Ordinal));

    private string? SelectedHorizonCommit()
    {
        if (ReleaseSelection?.SelectedItem is not ReleaseChoice selected || catalog is null) return null;
        return catalog.Patches.SingleOrDefault(patch => patch.Version == selected.Patch)?.UpstreamCommit;
    }

    private void ResetNativeModSelectionForHorizonChange()
    {
        nativeModPreflight = null;
        nativeMods = [];
        selectedNativeMods.Clear();
        selectedNativeModVersions.Clear();
        NativeModsStatus.Text = "Horizon release changed. Mod compatibility will be checked before browsing.";
        ClearNativeMods.Visibility = Visibility.Collapsed;
    }

    private IReadOnlyList<TargetProfile> SelectedTargets()
    {
        var targets = new List<TargetProfile>(4);
        if (ArchX64.IsChecked == true) targets.Add(TargetProfile.DesktopX64);
        if (ArchArm64.IsChecked == true) targets.Add(TargetProfile.SurfaceProX);
        if (ArchArm32.IsChecked == true) targets.Add(TargetProfile.WindowsMobile);
        if (ArchX86.IsChecked == true) targets.Add(TargetProfile.DesktopX86);
        return targets;
    }

    private IReadOnlyList<string> SelectedArchitectures() => SelectedTargets().Select(ArchitectureFor).ToArray();

    private static string ArchitectureFor(TargetProfile target) => target switch
    {
        TargetProfile.DesktopX64 => "x64", TargetProfile.DesktopX86 => "x86",
        TargetProfile.SurfaceProX => "arm64", TargetProfile.WindowsMobile => "arm", _ => "",
    };

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
            var architectures = SelectedArchitectures();
            foreach (var patch in catalog.Patches.OrderByDescending(item => new Version(item.Version)))
                foreach (var client in catalog.Clients.OrderByDescending(item => new Version(item.Version)))
                {
                    var checks = architectures.Select(targetArch => catalog.Check(patch.Version, client.Version, CatalogTrust.BuilderVersion, targetArch)).ToArray();
                    if (checks.Length > 0 && checks.All(check => check.Allowed))
                    {
                        var combined = new CompatibilityResult(true, checks.All(check => check.BuildVerified), checks.All(check => check.DeviceVerified),
                            string.Join(" ", checks.Select(check => check.Reason).Distinct(StringComparer.Ordinal)));
                        ReleaseSelection.Items.Add(new ReleaseChoice(patch.Version, client.Version, arch, combined));
                    }
                }
            ReleaseStatus.Text = ReleaseSelection.Items.Count == 0
                ? "No catalog release is declared compatible with every selected architecture and this builder."
                : "The signed catalog was verified. Choose a release that supports every selected architecture.";
        }
        var matching = previous is null ? null : ReleaseSelection.Items.OfType<ReleaseChoice>()
            .FirstOrDefault(item => item.Patch == previous.Patch && item.Client == previous.Client);
        ReleaseSelection.SelectedItem = matching ?? (ReleaseSelection.Items.Count > 0 ? ReleaseSelection.Items[0] : null);
        UpdateBuildActions();
    }

    private void UpdateBuildActions()
    {
        if (ReleaseSelection is null || RunRecipe is null) return;
        var available = !buildActionRunning && ReleaseSelection.SelectedItem is ReleaseChoice;
        RunRecipe.IsEnabled = available && minecraftOwnershipVerified && dependencyReady && readinessReady && !readinessRunning;
        if (NextButton is not null && WizardPips is not null)
            NextButton.IsEnabled = !navigationBusy && wizardStep < ReviewStep && CanAdvanceWizardStep(wizardStep);
    }

    private int SelectedCargoJobLimit()
    {
        if (BuildSpeedReview.SelectedItem is not ComboBoxItem item || item.Tag is not string tag
            || !int.TryParse(tag, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var jobs)) return 0;
        return Math.Max(0, jobs);
    }

    private void BuildSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizingBuildSpeed || BuildSpeedReview is null || BuildSpeedBuild is null) return;
        synchronizingBuildSpeed = true;
        var selected = sender is ComboBox combo ? combo.SelectedIndex : 0;
        BuildSpeedReview.SelectedIndex = selected;
        BuildSpeedBuild.SelectedIndex = selected;
        var enabled = !cargoCompileStarted;
        BuildSpeedReview.IsEnabled = enabled;
        BuildSpeedBuild.IsEnabled = enabled;
        synchronizingBuildSpeed = false;
    }

    private int CaptureCargoJobRequest()
    {
        var requested = SelectedCargoJobLimit();
        if (!cargoCompileStarted)
        {
            cargoCompileStarted = true;
            BuildSpeedReview.IsEnabled = false;
            BuildSpeedBuild.IsEnabled = false;
            BuilderActivityLog.Record("Build", $"Cargo build speed locked at {(requested == 0 ? "automatic" : requested.ToString(System.Globalization.CultureInfo.InvariantCulture) + " jobs")} for the current multi-architecture run.");
        }
        return requested;
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
        if (assetPrefetchSucceeded)
        {
            AssetPrefetchStatus.Text = assetPrefetchReadySummary ?? "Minecraft 26.3 files are ready in the verified private cache.";
            if (dependencyStarted) ShowDependencyProgress(new("minecraft-assets", "Ready", false, true, 100));
            return;
        }
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
        var attemptGeneration = ++assetPrefetchAttemptGeneration;
        var progress = new Progress<string>(message =>
        {
            // Progress<T> posts callbacks to the UI dispatcher. A final callback can arrive
            // after success (or after a later attempt has begun), so discard stale updates.
            if (attemptGeneration != assetPrefetchAttemptGeneration || assetPrefetchSucceeded) return;
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
                null, progress, cancellationToken, ConfirmAssetStorageAsync);
            assetPrefetchSucceeded = true;
            assetPrefetchReadySummary = $"Minecraft 26.3 files are ready. Reused {result.LocalFilesReused} local files and downloaded {result.NetworkFilesFetched} files ({result.BytesFetched / (1024d * 1024):0} MiB).";
            AssetPrefetchStatus.Text = assetPrefetchReadySummary;
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

    private async Task<bool> ConfirmAssetStorageAsync(long availableBytes, long requiredBytes)
    {
        static string HalfGiB(long bytes)
        {
            var rounded = Math.Ceiling(bytes / (512d * 1024 * 1024)) / 2;
            return rounded.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " GiB";
        }
        static string ExactMiB(long bytes) => (bytes / (1024d * 1024)).ToString("N1", System.Globalization.CultureInfo.CurrentCulture) + " MiB";

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = $"The verified Minecraft 26.3 index shows that the asset cache needs {ExactMiB(requiredBytes)} of additional space ({HalfGiB(requiredBytes)} rounded up).", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = $"This drive currently has {ExactMiB(availableBytes)} free. The cache is stored at {MinecraftAssetPreparation.CacheRoot}.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "You can continue anyway. If Windows runs out of space, asset preparation or the later build may fail.", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = WelcomePage.XamlRoot,
            Title = "Low storage space",
            Content = content,
            PrimaryButtonText = "Continue anyway",
            CloseButtonText = "Cancel asset preparation",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private Task<PreparedRelease> EnsurePreparedReleaseAsync(ReleaseChoice choice, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var key = ReleaseKey(choice);
        if (preparedReleaseTasks.TryGetValue(key, out var existing))
        {
            if (!existing.IsCanceled && !existing.IsFaulted) return ForArchitectureAsync(existing, choice);
            preparedReleaseTasks.Remove(key);
        }
        var task = PrepareReleaseSourceAsync(choice, progress, cancellationToken);
        preparedReleaseTasks.Add(key, task);
        return ForArchitectureAsync(task, choice);
    }

    private static async Task<PreparedRelease> ForArchitectureAsync(Task<PreparedRelease> task, ReleaseChoice choice)
    {
        var prepared = await task;
        return prepared.Architecture == choice.Architecture && prepared.Compatibility == choice.Compatibility
            ? prepared
            : prepared.ForArchitecture(choice.Architecture, choice.Compatibility);
    }

    private static string ReleaseKey(ReleaseChoice choice) => $"{choice.Patch}|{choice.Client}";

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
        MinecraftSignInCodeCard.Visibility = Visibility.Collapsed;
        MinecraftSignInCode.Text = string.Empty;
        MinecraftSignInStatus.Text = "Starting Microsoft sign-in…";
        using var signIn = new MinecraftSignIn();
        using var cancellation = new CancellationTokenSource();
        minecraftSignInCancellation = cancellation;
        try
        {
            var signInProgress = new Progress<string>(message => MinecraftSignInStatus.Text = message);
            var challenge = await signIn.BeginAsync(cancellation.Token, signInProgress);
            MinecraftSignInCode.Text = challenge.UserCode;
            MinecraftSignInCodeCard.Visibility = Visibility.Visible;
            MinecraftSignInLink.NavigateUri = challenge.VerificationUri;
            MinecraftSignInLink.Visibility = Visibility.Visible;
            MinecraftSignInStatus.Text = "Waiting for Microsoft to approve sign-in in your browser…";
            var owns = await signIn.CompleteOwnershipCheckAsync(challenge, cancellation.Token, signInProgress);
            minecraftOwnershipVerified = owns;
            UpdateBuildActions();
            UpdateWizard();
            MinecraftSignInStatus.Text = owns
                ? "Minecraft: Java Edition ownership confirmed. Your resources can now be downloaded and prepared."
                : "This account does not have Minecraft: Java Edition. Sign in with an account that owns it.";
        }
        catch (OperationCanceledException) { MinecraftSignInStatus.Text = "Sign-in cancelled."; }
        catch (Exception error) { MinecraftSignInStatus.Text = $"Sign-in could not be completed: {error.Message}"; }
        finally
        {
            minecraftSignInCancellation = null;
            MinecraftSignInCodeCard.Visibility = Visibility.Collapsed;
            MinecraftSignInCode.Text = string.Empty;
            MinecraftSignInLink.Visibility = Visibility.Collapsed;
            UpdateSignInActions(busy: false);
            MaybeStartAssetPrefetch();
            UpdateWizard();
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
            var signInProgress = new Progress<string>(message => MinecraftSignInStatus.Text = message);
            var owns = await signIn.CompleteSavedOwnershipCheckAsync(cancellation.Token, signInProgress);
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
            UpdateWizard();
            MaybeStartAssetPrefetch();
            if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
        }
    }

    private void SignOutMinecraft_Click(object sender, RoutedEventArgs e)
    {
        assetPrefetchAttemptGeneration++;
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
        UpdateWizard();
        if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
    }

    private void UpdateSignInActions(bool busy)
    {
        MinecraftSignInProgressRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        MinecraftSignInProgressRing.IsActive = busy;
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

    private void CancelAssetPrefetch_Click(object sender, RoutedEventArgs e)
    {
        assetPrefetchAttemptGeneration++;
        assetPrefetchCancellation?.Cancel();
    }
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
            if (!packageVersionResolvedAgainstInstalled)
            {
                if (!packageVersionCustomized)
                {
                    var signingIdentity = PackageSigning.GetOrCreateIdentity();
                    var installedClient = await LocalPackageInstaller.FindInstalledClientAsync(signingIdentity.Publisher);
                    SetSuggestedPackageVersion(LocalRecipe.NextPackageVersion(DateTimeOffset.UtcNow, installedClient?.Version));
                }
                packageVersionResolvedAgainstInstalled = true;
            }
            string? resources = null;
            string? minecraft = null;
            var selection = CurrentSelection();
            var targets = SelectedTargets();
            if (targets.Count == 0) throw new InvalidOperationException("Select at least one target architecture.");
            var release = ReleaseSelection.SelectedItem as ReleaseChoice ?? throw new InvalidOperationException("Choose a compatible catalog release.");
            if (!NativeModSelectionReady()) throw new InvalidOperationException("Reopen the native mod picker to check compatibility with the selected Horizon release.");
            var download = ResourceMode.SelectedIndex != 1;
            if (!download) LocalRecipe.ValidateResourceSelection(selection, resources, minecraft);
            var probe = new SystemProbe();
            SystemProbe.RefreshProcessPath();
            var checks = new List<SetupCheck>();
            var blockers = new List<string>();
            foreach (var target in targets)
            {
                var targetSelection = CurrentSelection(target);
                var architecture = ArchitectureFor(target);
                checks.AddRange(new SetupChecker(probe).Scan(target, splitRelease: true, unifiedToolchain: true, compactToolchain: true)
                    .Select(check => check with { Name = $"{architecture}: {check.Name}" }));
                blockers.AddRange(LocalRecipe.ToolchainBlockers(targetSelection, probe, await LocalRecipe.InspectRustAsync(probe),
                    splitRelease: true, unifiedToolchain: true, compactToolchain: true).Select(blocker => $"{architecture}: {blocker}"));
            }
            RenderSetupChecks(checks);
            foreach (var blocker in blockers) RecipeSteps.Children.Add(new TextBlock { Text = blocker, TextWrapping = TextWrapping.Wrap });
            if (download) RecipeSteps.Children.Add(new TextBlock { Text = "Download and prepare Minecraft resources in the private build workspace.", TextWrapping = TextWrapping.Wrap });
            foreach (var target in targets)
            {
                var architecture = ArchitectureFor(target);
                RecipeSteps.Children.Add(new TextBlock { Text = $"Build and sign the {architecture} package.", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                foreach (var step in LocalRecipe.Plan(CurrentSelection(target)).Steps)
                    RecipeSteps.Children.Add(new TextBlock { Text = step.Name == "Snapshot pinned source" ? $"{architecture}: download and verify the selected release source" : $"{architecture}: {step.Name}", TextWrapping = TextWrapping.Wrap });
            }
            if (selection.RegistryNativeMods is { Count: > 0 } mods)
                RecipeSteps.Children.Add(new TextBlock { Text = $"Selected compatible native mods: {string.Join(", ", mods.Select(id => $"{id} {selection.RegistryNativeModVersions![id]}"))}.", TextWrapping = TextWrapping.Wrap });
            RecipeSteps.Children.Add(new TextBlock { Text = "The client and any new resource framework will be signed with your local certificate. The public certificate and installation guide are included.", TextWrapping = TextWrapping.Wrap });
            var resourceChoice = ResourceMode.SelectedIndex switch { 0 => "Separate resource framework", 1 => "Existing resource framework", _ => "Resources bundled with the client" };
            var selectedMods = new List<string>();
            if (NativeTouchMod.IsChecked == true) selectedMods.Add("Touch controls");
            if (NativeDeviceVibration.IsChecked == true) selectedMods.Add("Device vibration");
            selectedMods.AddRange(selectedNativeMods.OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => $"{id} {selectedNativeModVersions.GetValueOrDefault(id, "version pending")}"));
            var modsSummary = selectedMods.Count == 0 ? "None" : string.Join(", ", selectedMods);
            var targetLabel = string.Join(", ", targets.Select(ArchitectureFor));
            BuildSummary.Text = $"Targets: {targetLabel}\nHorizon patch: {release.Patch}  ·  UWP client: {release.Client}\nResources: {resourceChoice}\nNative mods: {modsSummary}\nPackage version: {selection.PackageVersion}";
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
            ReadinessStatus.Text = "Ready to build — starting now…";
            await Task.Yield();
            if (wizardStep == ReviewStep && readinessReady && AutoBuild.IsChecked == true && !buildActionRunning)
                await StartBuildAsync();
        }
        finally { autoStartScheduled = false; }
    }

    private async void RunRecipe_Click(object sender, RoutedEventArgs e) => await StartBuildAsync();

    private async Task StartBuildAsync()
    {
        if (!readinessReady || !dependencyReady || !minecraftOwnershipVerified || buildActionRunning) return;
        builtOutputs.Clear();
        builtResourceMode = -1;
        builtExistingResourceFrameworkName = null;
        InstallLocal.Visibility = Visibility.Collapsed;
        InstallLocalFinished.Visibility = Visibility.Collapsed;
        buildActionRunning = true;
        BeginBuildProgress();
        wizardStep = BuildStep;
        completedPages = Math.Max(completedPages, BuildStep);
        highestVisited = Math.Max(highestVisited, BuildStep);
        UpdateWizard();
        UpdateBuildActions();
        try
        {
            var versionBeforeInstallCheck = PackageVersion.Text;
            var installedClient = await LocalPackageInstaller.FindInstalledClientAsync(PackageSigning.GetOrCreateIdentity().Publisher);
            if (!packageVersionCustomized)
                SetSuggestedPackageVersion(LocalRecipe.NextPackageVersion(DateTimeOffset.UtcNow, installedClient?.Version));
            else if (installedClient is not null && new Version(CurrentSelection().ValidatedPackageVersion) <= installedClient.Version)
                throw new InvalidOperationException($"The installed Horizon package is version {installedClient.Version}. Choose a higher package version to make an in-place upgrade available.");
            if (!string.Equals(versionBeforeInstallCheck, PackageVersion.Text, StringComparison.Ordinal))
                BuildSummary.Text = BuildSummary.Text.Replace($"Package version: {versionBeforeInstallCheck}", $"Package version: {PackageVersion.Text}", StringComparison.Ordinal);
            packageVersionResolvedAgainstInstalled = true;
            if (!minecraftOwnershipVerified) throw new InvalidOperationException("Sign in with an account that owns Minecraft: Java Edition before building.");
            string? resources = null;
            string? minecraft = null;
            var release = ReleaseSelection.SelectedItem as ReleaseChoice ?? throw new InvalidOperationException("Choose a compatible catalog release.");
            var download = ResourceMode.SelectedIndex != 1;
            var targets = SelectedTargets();
            if (targets.Count == 0) throw new InvalidOperationException("Select at least one architecture.");
            builtResourceMode = ResourceMode.SelectedIndex;
            builtExistingResourceFrameworkName = builtResourceMode == 1
                ? CurrentSelection(targets[0]).ValidatedExistingResourceFramework
                : null;
            var progress = new Progress<string>(message =>
            {
                ReportBuildProgress(message);
            });
            SystemProbe.RefreshProcessPath();
            var probe = new SystemProbe();
            foreach (var target in targets)
            {
                var selection = CurrentSelection(target);
                if (!download) LocalRecipe.ValidateResourceSelection(selection, resources, minecraft);
                var blockers = LocalRecipe.ToolchainBlockers(selection, probe, await LocalRecipe.InspectRustAsync(probe),
                    splitRelease: true, unifiedToolchain: true, compactToolchain: true);
                if (blockers.Count > 0) throw new InvalidOperationException($"Dependency setup is incomplete for {ArchitectureFor(target)}. " + string.Join(" ", blockers));
            }
            if (assetPrefetchTask is { IsCompleted: false } prefetchTask) await prefetchTask;
            var warnings = new List<string>();
            PreparedRelease? sharedPreparedRelease = null;
            foreach (var target in targets)
            {
                activeBuildArchitecture = ArchitectureFor(target);
                BuildProgressStatus.Text = $"Preparing {activeBuildArchitecture} package";
                BuildProgressDetail.Text = $"Build {targets.ToList().IndexOf(target) + 1} of {targets.Count}";
                var selection = CurrentSelection(target);
                var compatibility = catalog?.Check(release.Patch, release.Client, CatalogTrust.BuilderVersion, activeBuildArchitecture)
                    ?? throw new InvalidOperationException("The signed compatibility catalog is unavailable.");
                if (!compatibility.Allowed) throw new InvalidOperationException($"The selected release is not compatible with {activeBuildArchitecture}.");
                var targetRelease = release with { Architecture = activeBuildArchitecture, Compatibility = compatibility };
                var prepared = sharedPreparedRelease is null
                    ? await EnsurePreparedReleaseAsync(targetRelease, progress, CancellationToken.None)
                    : sharedPreparedRelease.ForArchitecture(activeBuildArchitecture, compatibility);
                sharedPreparedRelease ??= prepared;
                if (download && resources is null)
                    resources = await MinecraftAssetPreparation.PrepareAsync(prepared, selection, minecraftOwnershipVerified,
                        probe, progress, minecraft, confirmInsufficientSpace: ConfirmAssetStorageAsync,
                        requestedCargoJobs: CaptureCargoJobRequest);
                var result = await LocalBuildRunner.RunPreparedAsync(prepared, resources, selection, probe, progress, minecraft,
                    requestedCargoJobs: CaptureCargoJobRequest);
                var signed = result.SignedOutput ?? throw new InvalidOperationException($"The {activeBuildArchitecture} build did not produce signed output.");
                builtOutputs.Add((target, signed));
                if (result.SoundWarning is { } warning) warnings.Add($"{activeBuildArchitecture}: {warning}");
            }
            completedPages = FinishedStep;
            wizardStep = FinishedStep;
            highestVisited = Math.Max(highestVisited, FinishedStep);
            var hostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            await UpdateLocalInstallVisibilityAsync(hostArchitecture);
            RecipeStatus.Text = FormatBuiltOutputs("Signed client packages:");
            if (CurrentSelection(targets[0]).ValidatedExistingResourceFramework is { } existing)
                RecipeStatus.Text += $" Requires existing resource framework {existing} on the target.";
            if (warnings.Count > 0) RecipeStatus.Text += "\n" + string.Join("\n", warnings);
            FinishedResourceSummary.Text = builtResourceMode switch
            {
                0 => "Resource option: Minecraft 26.3 is in a separate signed HorizonResources.appx for each architecture. Install it with the client package.",
                1 => $"Resource option: the client uses existing framework {builtExistingResourceFrameworkName} on the target. No resource APPX was generated.",
                2 => "Resource option: Minecraft 26.3 is bundled inside each client APPX. No separate resource framework is needed.",
                _ => "Resource packaging follows the option selected earlier in the wizard.",
            };
            FinishedStatus.Text = "The signed packages and their installation files are ready.";
            UpdateFinishedOutputs();
            UpdateWizard();
            CompleteBuildProgress(succeeded: true, $"Signed {builtOutputs.Count} architecture package(s). The certificate and installation guides are ready.");
        }
        catch (Exception error)
        {
            var hostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            await UpdateLocalInstallVisibilityAsync(hostArchitecture);
            RecipeStatus.Text = $"Build failed{(activeBuildArchitecture is null ? "" : $" for {activeBuildArchitecture}")}: {error.Message}";
            if (builtOutputs.Count > 0) RecipeStatus.Text += "\n\n" + FormatBuiltOutputs("Completed packages (available despite the later failure):");
            CompleteBuildProgress(succeeded: false, error.Message);
            BuilderActivityLog.Record("Build", "Build failed: " + error.Message);
        }
        finally
        {
            // Prepared release inputs are immutable; each target build receives its own isolated source copy.
            activeBuildArchitecture = null;
            buildActionRunning = false;
            UpdateWizard();
            UpdateBuildActions();
        }
    }

    private void BeginBuildProgress()
    {
        cargoCompileStarted = false;
        BuildSpeedReview.IsEnabled = true;
        BuildSpeedBuild.IsEnabled = true;
        buildProgressRows.Clear();
        activeBuildProgressRow = null;
        BuildStepItems.Children.Clear();
        BuildProgressRing.Visibility = Visibility.Visible;
        BuildProgressRing.IsActive = true;
        BuildProgressStatus.Text = "Starting your build…";
        BuildProgressDetail.Text = "Preparing the selected release and local build tools.";
        RecipeStatus.Text = "";
    }

    private void ReportBuildProgress(string message)
    {
        var (stepKey, stepTitle) = ResolveBuildStep(message, activeBuildProgressRow?.Key);
        var key = activeBuildArchitecture is null ? stepKey : $"{activeBuildArchitecture}|{stepKey}";
        var title = activeBuildArchitecture is null ? stepTitle : $"{activeBuildArchitecture} · {stepTitle}";
        if (activeBuildProgressRow?.Key != key)
        {
            if (activeBuildProgressRow is not null) MarkBuildProgressRowComplete(activeBuildProgressRow);
            if (!buildProgressRows.TryGetValue(key, out var row))
            {
                row = CreateBuildProgressRow(key);
                buildProgressRows.Add(key, row);
                BuildStepItems.Children.Add(CreateBuildProgressVisual(row, title));
            }
            activeBuildProgressRow = row;
            row.Ring.Visibility = Visibility.Visible;
            row.Ring.IsActive = true;
            row.CompleteMark.Visibility = Visibility.Collapsed;
            row.State.Text = "In progress";
            BuildProgressStatus.Text = title;
        }

        var active = activeBuildProgressRow!;
        active.Detail.Text = message;
        UpdateBuildStepDeterminateProgress(active, message);
        BuildProgressDetail.Text = message;
        BuildProgressRing.Visibility = Visibility.Visible;
        BuildProgressRing.IsActive = true;
        if (!message.StartsWith("Cargo: ", StringComparison.OrdinalIgnoreCase)) BuilderActivityLog.Record("Build", message);
    }

    private static (string Key, string Title) ResolveBuildStep(string message, string? activeKey)
    {
        if (message.StartsWith("Cargo: ", StringComparison.OrdinalIgnoreCase))
        {
            var stepKey = activeKey?.Split('|').LastOrDefault();
            return (stepKey ?? "cargo", stepKey switch
            {
                "asset-helper" => "Build the resource helper",
                "compile-client" => "Compile the Horizon client",
                "cargo-dependencies" => "Resolve Cargo dependencies",
                "native-mods" => "Build the native-mod tool",
                _ => "Run Cargo build tools",
            });
        }
        if (message.StartsWith("Minecraft 26.3 assets", StringComparison.OrdinalIgnoreCase)) return ("minecraft-assets", "Prepare Minecraft assets");
        if (message.Contains("Minecraft resource preparation helper", StringComparison.OrdinalIgnoreCase)) return ("asset-helper", "Build the resource helper");
        if (message.Contains("prefetched Minecraft resources", StringComparison.OrdinalIgnoreCase)) return ("prepare-resources", "Prepare Minecraft resources");
        if (message.Contains("native mod", StringComparison.OrdinalIgnoreCase)) return ("native-mods", "Install selected native mods");
        if (message.Contains("release source", StringComparison.OrdinalIgnoreCase) || message.Contains("authenticated release source", StringComparison.OrdinalIgnoreCase)
            || message.Contains("isolated snapshot", StringComparison.OrdinalIgnoreCase)) return ("release-source", "Download and verify the Horizon release");
        if (message.Contains("Microsoft framework", StringComparison.OrdinalIgnoreCase)) return ("frameworks", "Check package frameworks");
        if (message.Contains("Cargo dependenc", StringComparison.OrdinalIgnoreCase)) return ("cargo-dependencies", "Resolve Cargo dependencies");
        if (message.Contains("game bridge", StringComparison.OrdinalIgnoreCase) || message.Contains("Strict-linking", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Linking the selected Horizon", StringComparison.OrdinalIgnoreCase)) return ("compile-client", "Compile the Horizon client");
        if (message.Contains("Packaging", StringComparison.OrdinalIgnoreCase) || message.Contains("resource framework", StringComparison.OrdinalIgnoreCase)) return ("package", "Package the client and resources");
        if (message.Contains("Signing", StringComparison.OrdinalIgnoreCase) || message.Contains("certificate", StringComparison.OrdinalIgnoreCase)) return ("sign", "Sign packages and export the certificate");
        if (message.Contains("Rust standard-library", StringComparison.OrdinalIgnoreCase) || message.Contains("ARM32 offline", StringComparison.OrdinalIgnoreCase)
            || message.Contains("compact Rust", StringComparison.OrdinalIgnoreCase) || message.Contains("Visual C++ environment", StringComparison.OrdinalIgnoreCase)
            || message.Contains("parallel Cargo jobs", StringComparison.OrdinalIgnoreCase)) return ("toolchain", "Prepare the compiler and Rust toolchain");
        if (activeKey?.EndsWith("|prepare-resources", StringComparison.Ordinal) == true)
            return ("prepare-resources", "Prepare Minecraft resources");
        return ("step:" + message, message.Length <= 72 ? message : message[..69] + "…");
    }

    private BuildProgressRow CreateBuildProgressRow(string key)
    {
        var ring = new ProgressRing { Width = 20, Height = 20, IsActive = false, Visibility = Visibility.Collapsed };
        var completeMark = new TextBlock { Text = "✓", FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var state = new TextBlock { Text = "Waiting", Opacity = 0.8 };
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        var progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0, Visibility = Visibility.Collapsed };
        return new(key, ring, completeMark, state, detail, progress);
    }

    private static UIElement CreateBuildProgressVisual(BuildProgressRow row, string title)
    {
        var icon = new Grid { Width = 22, Height = 22 };
        icon.Children.Add(row.Ring);
        icon.Children.Add(row.CompleteMark);
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(row.State, 1);
        heading.Children.Add(titleText);
        heading.Children.Add(row.State);
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(heading);
        content.Children.Add(row.Detail);
        content.Children.Add(row.Progress);
        var layout = new Grid { ColumnSpacing = 10 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(icon);
        Grid.SetColumn(content, 1);
        layout.Children.Add(content);
        return layout;
    }

    private static void UpdateBuildStepDeterminateProgress(BuildProgressRow row, string message)
    {
        var marker = " files ready";
        var end = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return;
        var start = message.LastIndexOf(' ', end);
        var count = start < 0 ? message[..end] : message[(start + 1)..end];
        var separator = count.IndexOf('/');
        if (separator <= 0 || !int.TryParse(count[..separator], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var current)
            || !int.TryParse(count[(separator + 1)..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var maximum) || maximum <= 0 || current < 0 || current > maximum) return;
        row.Progress.Maximum = maximum;
        row.Progress.Value = current;
        row.Progress.Visibility = Visibility.Visible;
        row.State.Text = $"{current:0} / {maximum:0}";
    }

    private static void MarkBuildProgressRowComplete(BuildProgressRow row)
    {
        row.Ring.IsActive = false;
        row.Ring.Visibility = Visibility.Collapsed;
        row.CompleteMark.Visibility = Visibility.Visible;
        row.State.Text = "Complete";
        if (row.Progress.Visibility == Visibility.Visible) row.Progress.Value = row.Progress.Maximum;
    }

    private void CompleteBuildProgress(bool succeeded, string detail)
    {
        BuildProgressRing.IsActive = false;
        BuildProgressRing.Visibility = Visibility.Collapsed;
        BuildProgressStatus.Text = succeeded ? "Build complete" : "Build failed";
        BuildProgressDetail.Text = detail;
        if (succeeded && activeBuildProgressRow is not null) MarkBuildProgressRowComplete(activeBuildProgressRow);
        else if (activeBuildProgressRow is { } failed)
        {
            failed.Ring.IsActive = false;
            failed.Ring.Visibility = Visibility.Collapsed;
            failed.CompleteMark.Text = "!";
            failed.CompleteMark.Visibility = Visibility.Visible;
            failed.State.Text = "Failed";
        }
    }

    private async void InstallLocal_Click(object sender, RoutedEventArgs e)
    {
        var hostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
        var eligibility = await CheckLocalInstallAvailabilityAsync(hostArchitecture);
        var installable = eligibility.Eligible;
        if (installable.Count == 0)
        {
            LocalInstallAvailability.Text = FormatLocalInstallAvailability(eligibility);
            InstallLocal.Visibility = Visibility.Collapsed;
            InstallLocalFinished.Visibility = Visibility.Collapsed;
            return;
        }
        var choices = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0 };
        foreach (var item in installable)
            choices.Items.Add(new ComboBoxItem { Content = $"{ArchitectureFor(item.Target)} — {Path.GetFileName(item.Output.Client)}", Tag = item.Output });
        var installedIndex = eligibility.InstalledClient is { } installed
            ? Array.FindIndex(installable.ToArray(), item => string.Equals(ArchitectureFor(item.Target), installed.Architecture, StringComparison.OrdinalIgnoreCase))
            : -1;
        var defaultIndex = installedIndex >= 0 ? installedIndex
            : Array.FindIndex(installable.ToArray(), item => item.Target == BuildSelection.DefaultTarget(hostArchitecture));
        if (defaultIndex >= 0) choices.SelectedIndex = defaultIndex;
        var installContent = new StackPanel { Spacing = 10 };
        var installDescription = new TextBlock { TextWrapping = TextWrapping.Wrap };
        installContent.Children.Add(installDescription);
        installContent.Children.Add(new TextBlock { Text = "Windows will trust your local signing certificate and install the client and its required frameworks for your account. Windows may ask for administrator approval.", TextWrapping = TextWrapping.Wrap });
        if (installable.Count > 1)
        {
            installContent.Children.Add(new TextBlock { Text = "Choose the package architecture to install:", TextWrapping = TextWrapping.Wrap });
            installContent.Children.Add(choices);
        }
        var dialog = new ContentDialog
        {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            Title = "Install Horizon on this PC?",
            Content = installContent,
            PrimaryButtonText = "Install", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        void UpdateInstallPrompt()
        {
            var selected = installable[Math.Clamp(choices.SelectedIndex, 0, installable.Count - 1)];
            if (eligibility.InstalledClient is not { } current)
            {
                dialog.Title = "Install Horizon on this PC?";
                dialog.PrimaryButtonText = "Install";
                installDescription.Text = "This Windows account does not have the matching MCHorizonUWP package installed.";
            }
            else if (string.Equals(ArchitectureFor(selected.Target), current.Architecture, StringComparison.OrdinalIgnoreCase))
            {
                dialog.Title = "Upgrade Horizon on this PC?";
                dialog.PrimaryButtonText = "Upgrade";
                installDescription.Text = $"This will update MCHorizonUWP {current.Version} with the same package identity and architecture. Windows normally preserves local app data during package updates; the new version is {PackageVersion.Text}.";
            }
            else
            {
                dialog.Title = "Change Horizon package architecture?";
                dialog.PrimaryButtonText = "Install";
                installDescription.Text = $"The current installation uses {DisplayInstalledArchitecture(current.Architecture)}, while this package uses {DisplayArchitecture(selected.Target)}. Windows will attempt a package update, but changing architecture can expose app-data or compatibility issues. Back up worlds before continuing.";
            }
        }
        choices.SelectionChanged += (_, _) => UpdateInstallPrompt();
        UpdateInstallPrompt();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var output = choices.SelectedItem is ComboBoxItem { Tag: SignedPackageOutput selectedOutput } ? selectedOutput : installable[0].Output;
        var latestEligibility = await CheckLocalInstallAvailabilityAsync(hostArchitecture);
        if (!latestEligibility.Eligible.Any(item => Path.GetFullPath(item.Output.Client) == Path.GetFullPath(output.Client)))
        {
            LocalInstallAvailability.Text = FormatLocalInstallAvailability(latestEligibility);
            InstallLocal.Visibility = Visibility.Collapsed;
            InstallLocalFinished.Visibility = Visibility.Collapsed;
            return;
        }
        if (eligibility.InstalledClient?.Architecture != latestEligibility.InstalledClient?.Architecture
            || eligibility.InstalledClient?.Version != latestEligibility.InstalledClient?.Version)
        {
            LocalInstallAvailability.Text = "The installed Horizon package changed while the install dialog was open. Review the current package status and try again.";
            return;
        }
        InstallLocal.IsEnabled = false;
        InstallLocalFinished.IsEnabled = false;
        try
        {
            await LocalPackageInstaller.InstallAsync(output, new Progress<string>(SetInstallStatus));
            SetInstallStatus("Horizon was installed for your Windows account.");
            await UpdateLocalInstallVisibilityAsync(hostArchitecture);
        }
        catch (Exception error) { SetInstallStatus($"Installation did not complete: {error.Message}"); }
        finally { InstallLocal.IsEnabled = true; InstallLocalFinished.IsEnabled = true; }
    }

    private void SetInstallStatus(string message)
    {
        RecipeStatus.Text = message;
        FinishedStatus.Text = message;
    }

    private string FormatBuiltOutputs(string heading) => heading + "\n" + string.Join("\n\n", builtOutputs.Select(item =>
        $"{ArchitectureFor(item.Target)}: {item.Output.Client}\nCertificate: {item.Output.Certificate}\nInstallation guide: {item.Output.InstallationGuide}"
        + (item.Output.Dependencies is { Count: > 0 } dependencies ? $"\nMicrosoft framework dependencies: {Path.GetDirectoryName(dependencies[0])}" : "")
        + (item.Output.ResourceFramework is { } framework ? $"\nSigned resource framework: {framework} (contains private Minecraft resources)" : "")));

    private async Task UpdateLocalInstallVisibilityAsync(System.Runtime.InteropServices.Architecture hostArchitecture)
    {
        var eligibility = await CheckLocalInstallAvailabilityAsync(hostArchitecture);
        var canInstall = eligibility.Eligible.Count > 0;
        InstallLocal.Content = GetLocalInstallAction(eligibility);
        InstallLocalFinished.Content = InstallLocal.Content;
        InstallLocal.Visibility = canInstall ? Visibility.Visible : Visibility.Collapsed;
        InstallLocalFinished.Visibility = canInstall ? Visibility.Visible : Visibility.Collapsed;
        LocalInstallAvailability.Text = FormatLocalInstallAvailability(eligibility);
    }
    private async void BrowseNativeMods_Click(object sender, RoutedEventArgs e)
    {
        BrowseNativeMods.IsEnabled = false;
        NativeModsCompatibilityRing.Visibility = Visibility.Visible;
        NativeModsCompatibilityRing.IsActive = true;
        try
        {
            var targetCommit = SelectedHorizonCommit() ?? throw new InvalidOperationException("Choose a Horizon release before browsing native mods.");
            NativeModsStatus.Text = "Checking every native-mod release for this Horizon commit…";
            UpdateBuildActions();
            var progress = new Progress<string>(message => NativeModsStatus.Text = message);
            nativeModPreflight = await NativeModPreflight.ResolveAsync(targetCommit, progress);
            nativeMods = nativeModPreflight.Mods;
            var compatibleCount = nativeMods.Count(mod => mod.IsCompatible);
            var incompatibleCount = nativeMods.Count - compatibleCount;
            NativeModsStatus.Text = $"Compatibility checked for {nativeMods.Count} mods: {compatibleCount} have a compatible release; {incompatibleCount} will be shown disabled.";
            if (selectedNativeMods.Count > 0 && !NativeModSelectionReady())
                NativeModsStatus.Text += " Review the selections for this Horizon commit.";
            var picker = new NativeModsDialog(nativeMods, selectedNativeMods, targetCommit)
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
            dialog.IsPrimaryButtonEnabled = picker.HasCompatibleSelection;
            picker.SelectionCompatibilityChanged += (_, _) => dialog.IsPrimaryButtonEnabled = picker.HasCompatibleSelection;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                selectedNativeMods.Clear();
                selectedNativeMods.UnionWith(picker.SelectedIds);
                selectedNativeModVersions.Clear();
                foreach (var (id, version) in picker.SelectedVersions) selectedNativeModVersions[id] = version;
                UpdateNativeModCount();
                UpdateBuildActions();
                UpdateWizard();
                if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
            }
        }
        catch (Exception error) { NativeModsStatus.Text = $"Could not check native-mod compatibility: {error.Message}"; }
        finally
        {
            NativeModsCompatibilityRing.IsActive = false;
            NativeModsCompatibilityRing.Visibility = Visibility.Collapsed;
            BrowseNativeMods.IsEnabled = true;
            UpdateBuildActions();
            UpdateWizard();
        }
    }

    private void ClearNativeMods_Click(object sender, RoutedEventArgs e)
    {
        selectedNativeMods.Clear();
        selectedNativeModVersions.Clear();
        UpdateNativeModCount();
        UpdateBuildActions();
        UpdateWizard();
        if (wizardStep == ReviewStep) _ = RefreshReadinessAsync();
    }

    private void UpdateNativeModCount()
    {
        var names = nativeMods.Where(mod => selectedNativeMods.Contains(mod.Id)).OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(mod => $"{mod.Name} {selectedNativeModVersions.GetValueOrDefault(mod.Id, "version pending")}").ToArray();
        NativeModsStatus.Text = names.Length == 0 ? "No Horizon repository mods selected."
            : $"{names.Length} compatible Horizon native mod(s) selected: {string.Join(", ", names)}";
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
