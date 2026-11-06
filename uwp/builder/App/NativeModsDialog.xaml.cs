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

    public NativeModsDialog(IReadOnlyList<NativeModListing> mods, IEnumerable<string> selectedIds)
    {
        InitializeComponent();
        var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        choices = mods.OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(mod => mod.Id, StringComparer.Ordinal)
            .Select(mod => new NativeModChoice(mod, selected.Contains(mod.Id))).ToArray();
        foreach (var choice in choices) choice.PropertyChanged += Choice_PropertyChanged;
        ApplyFilter();
    }

    public IReadOnlyList<string> SelectedIds => choices.Where(choice => choice.IsSelected)
        .Select(choice => choice.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

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
        if (e.PropertyName == nameof(NativeModChoice.IsSelected))
            CountLabel.Text = $"{visibleCount} of {choices.Count} mods shown · {choices.Count(choice => choice.IsSelected)} selected";
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
    internal NativeModChoice(NativeModListing listing, bool selected)
    {
        Id = listing.Id;
        Name = listing.Name;
        Summary = listing.Summary;
        Version = listing.Version;
        AuthorsText = listing.Authors.Length == 0 ? "" : "By " + string.Join(", ", listing.Authors);
        isSelected = selected;
    }

    public string Id { get; }
    public string Name { get; }
    public string Summary { get; }
    public string Version { get; }
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
}

public sealed class NativeModGroup(string key, IReadOnlyList<NativeModChoice> items)
{
    public string Key { get; } = key;
    public IReadOnlyList<NativeModChoice> Items { get; } = items;
}
