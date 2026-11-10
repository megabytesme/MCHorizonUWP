using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Horizon.Builder.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace Horizon.Builder.App;

public sealed partial class NativeModsDialog : UserControl
{
    private readonly IReadOnlyList<NativeModChoice> choices;
    private string search = "";
    private int visibleCount;
    private Dictionary<string, string> selectedVersions = new(StringComparer.Ordinal);
    private bool updatingSelectedPlan;
    private IReadOnlyList<string> skippedModNames = [];
    public event EventHandler? SelectionCompatibilityChanged;

    public NativeModsDialog(IReadOnlyList<NativeModListing> mods, IEnumerable<string> selectedIds, string horizonCommit)
    {
        InitializeComponent();
        CompatibilityLabel.Text = $"Target Horizon commit: {horizonCommit}. The builder checks each release and chooses the newest compatible version; exact versions appear in build details.";
        var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        choices = mods.OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(mod => mod.Id, StringComparer.Ordinal)
            .Select(mod => new NativeModChoice(mod, selected.Contains(mod.Id) && mod.IsCompatible)).ToArray();
        foreach (var choice in choices) choice.PropertyChanged += Choice_PropertyChanged;
        ApplyFilter();
        UpdateSelectedPlan();
    }

    public IReadOnlyList<string> SelectedIds => choices.Where(choice => choice.IsSelected)
        .Select(choice => choice.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    public IReadOnlyDictionary<string, string> SelectedVersions => selectedVersions;
    public IReadOnlyList<string> SkippedModNames => skippedModNames;
    public bool HasCompatibleSelection { get; private set; } = true;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        search = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var matches = choices.Where(choice => search.Length == 0
            || choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || choice.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
            || choice.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
            || choice.AuthorsText.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        visibleCount = matches.Length;
        var groups = matches.GroupBy(choice => Letter(choice.Name))
            .OrderBy(group => group.Key == "#" ? "{" : group.Key, StringComparer.Ordinal)
            .Select(group => new NativeModGroup(group.Key, group.ToArray())).ToArray();
        ModsCollectionView.Source = groups;
        ModsZoomedOut.ItemsSource = ModsCollectionView.View.CollectionGroups;
        CountLabel.Text = $"{matches.Length} of {choices.Count} mods shown · {choices.Count(choice => choice.IsSelected)} selected";
        EmptyLabel.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModsSemanticZoom.Visibility = matches.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ModsSemanticZoom.IsZoomedInViewActive = true;
    }

    private void Choice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NativeModChoice.IsSelected) && !updatingSelectedPlan)
        {
            CountLabel.Text = $"{visibleCount} of {choices.Count} mods shown · {choices.Count(choice => choice.IsSelected)} selected";
            UpdateSelectedPlan();
        }
    }

    private void UpdateSelectedPlan()
    {
        var selected = choices.Where(choice => choice.IsSelected).ToArray();
        selectedVersions = new(StringComparer.Ordinal);
        skippedModNames = [];
        if (selected.Length == 0)
        {
            HasCompatibleSelection = true;
            SelectionCompatibilityLabel.Text = "Selected mods will use the newest versions compatible with this Horizon commit.";
            foreach (var choice in choices) choice.SetCompatibleVersion();
            SelectionCompatibilityChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var common = selected[0].CompatibleVersions.Select(MixinsRevision).ToHashSet(StringComparer.Ordinal);
        foreach (var choice in selected.Skip(1)) common.IntersectWith(choice.CompatibleVersions.Select(MixinsRevision));
        NativeModChoice[] skipped = [];
        if (common.Count == 0)
        {
            var bestRevision = selected.SelectMany(choice => choice.CompatibleVersions.Select(MixinsRevision))
                .Distinct(StringComparer.Ordinal)
                .Select(revision => (Revision: revision,
                    Coverage: selected.Count(choice => choice.CompatibleVersions.Any(candidate => MixinsRevision(candidate) == revision)),
                    Score: selected.Sum(choice => IndexOfMixins(choice.CompatibleVersions, revision)),
                    FirstSelectedIndex: IndexOfMixins(selected[0].CompatibleVersions, revision)))
                .OrderByDescending(item => item.Coverage).ThenBy(item => item.Score).ThenBy(item => item.FirstSelectedIndex)
                .First();
            common.Add(bestRevision.Revision);
            skipped = selected.Where(choice => !choice.CompatibleVersions.Any(candidate => MixinsRevision(candidate) == bestRevision.Revision)).ToArray();
            skippedModNames = skipped.Select(choice => choice.Name).ToArray();
            updatingSelectedPlan = true;
            foreach (var choice in skipped) choice.IsSelected = false;
            updatingSelectedPlan = false;
            selected = selected.Except(skipped).ToArray();
        }

        HasCompatibleSelection = true;
        var sharedMixins = common.Select(revision => (Revision: revision,
                Score: selected.Sum(choice => IndexOfMixins(choice.CompatibleVersions, revision))))
            .OrderBy(item => item.Score).ThenBy(item => item.Revision, StringComparer.Ordinal).First().Revision;
        foreach (var choice in selected)
        {
            var version = choice.CompatibleVersions.First(candidate => MixinsRevision(candidate) == sharedMixins);
            selectedVersions[choice.Id] = version.Version;
            choice.SetBuildVersion(version);
        }
        foreach (var choice in choices.Where(item => !item.IsSelected))
        {
            if (skipped.Contains(choice)) choice.SetSkippedBuildVersion();
            else choice.SetCompatibleVersion();
        }
        SelectionCompatibilityLabel.Text = skipped.Length == 0
            ? $"Selected versions share Horizon mixin tool {sharedMixins}. Build details will list the exact releases."
            : $"Using mixin tool {sharedMixins} for {selected.Length} mods. Skipped {string.Join(", ", skippedModNames)} because their releases require a different tool version.";
        CountLabel.Text = $"{visibleCount} of {choices.Count} mods shown · {choices.Count(choice => choice.IsSelected)} selected";
        SelectionCompatibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int IndexOfMixins(IReadOnlyList<NativeModCandidate> versions, string revision)
    {
        for (var index = 0; index < versions.Count; index++)
            if (MixinsRevision(versions[index]) == revision) return index;
        return int.MaxValue / 2;
    }

    private static string MixinsRevision(NativeModCandidate candidate)
    {
        var parts = candidate.Api.Split('/');
        return parts.Length == 3 ? parts[2] : "";
    }

    private static string Letter(string name)
    {
        var first = name.TrimStart().FirstOrDefault();
        return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
    }
}

public sealed class NativeModChoice : INotifyPropertyChanged
{
    private bool isSelected;
    private string versionLabel;
    private readonly string latestCompatibleVersionLabel;
    internal NativeModChoice(NativeModListing listing, bool selected)
    {
        Id = listing.Id;
        Name = listing.Name;
        Summary = listing.Summary;
        CompatibleVersions = listing.CompatibleVersions ?? [];
        IsCompatible = CompatibleVersions.Count > 0;
        CompatibilityMessage = listing.CompatibilityMessage;
        latestCompatibleVersionLabel = IsCompatible ? $"Compatible · {CompatibleVersions[0].Version}" : "Not compatible";
        versionLabel = latestCompatibleVersionLabel;
        AuthorsText = listing.Authors.Length == 0 ? "" : "By " + string.Join(", ", listing.Authors);
        isSelected = selected && IsCompatible;
    }

    public string Id { get; }
    public string Name { get; }
    public string Summary { get; }
    public IReadOnlyList<NativeModCandidate> CompatibleVersions { get; }
    public bool IsCompatible { get; }
    public string CompatibilityMessage { get; }
    public string VersionLabel => versionLabel;
    public string LatestVersionLabel => IsCompatible ? "Latest " + CompatibleVersions[0].Version : "Not compatible";
    public string AuthorsText { get; }
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void SetBuildVersion(NativeModCandidate? candidate)
    {
        var next = candidate is null ? latestCompatibleVersionLabel : $"Build · {candidate.Version}";
        SetVersionLabel(next);
    }

    internal void SetCompatibleVersion() => SetVersionLabel(latestCompatibleVersionLabel);
    internal void SetSkippedBuildVersion() => SetVersionLabel("Skipped · different mixin tool");

    private void SetVersionLabel(string next)
    {
        if (versionLabel == next) return;
        versionLabel = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VersionLabel)));
    }
}

public sealed class NativeModGroup(string key, IReadOnlyList<NativeModChoice> items)
{
    public string Key { get; } = key;
    public IReadOnlyList<NativeModChoice> Items { get; } = items;
}
