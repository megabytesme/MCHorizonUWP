using Horizon.Builder.Core;
using Microsoft.UI.Xaml.Controls;

namespace Horizon.Builder.App;

public sealed partial class MainWindow
{
    private void ReleaseSelection_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectedReleaseDetails();

    private void UpdateSelectedReleaseDetails()
    {
        if (SelectedReleaseDetails is null) return;
        if (ReleaseSelection.SelectedItem is not ReleaseChoice selected || catalog is null)
        {
            SelectedReleaseDetails.Text = "The Horizon revision will appear when the signed catalog is loaded.";
            return;
        }

        var patch = catalog.Patches.SingleOrDefault(item => item.Version == selected.Patch);
        if (patch is null)
        {
            SelectedReleaseDetails.Text = "The selected Horizon patch is missing from the signed catalog.";
            return;
        }

        var nightly = patch.UpstreamCommit.Equals(LocalRecipe.UpstreamNightlyCommit, StringComparison.OrdinalIgnoreCase)
            ? $"Horizon nightly: {LocalRecipe.UpstreamReleaseVersion}\n"
            : string.Empty;
        SelectedReleaseDetails.Text = $"{nightly}Horizon commit: {patch.UpstreamCommit}";
    }
}
