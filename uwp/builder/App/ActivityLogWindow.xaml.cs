using Horizon.Builder.Core;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace Horizon.Builder.App;

public sealed partial class ActivityLogWindow : Window
{
    private const int MaximumVisibleEntries = 12_000;
    private readonly DispatcherQueueTimer refreshTimer;
    private string lastLog = "";
    public ObservableCollection<string> LogEntries { get; } = [];

    public ActivityLogWindow()
    {
        InitializeComponent();
        Title = "Setup and build log";
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Horizon.ico"));
        appWindow.Resize(new Windows.Graphics.SizeInt32(1100, 760));
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

        RefreshLog();
        refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(1);
        refreshTimer.Tick += (_, _) => RefreshLog();
        refreshTimer.Start();
        Closed += (_, _) => refreshTimer.Stop();
    }

    private void RefreshLog()
    {
        var current = BuilderActivityLog.ReadAll();
        if (current == lastLog) return;

        var follow = IsAtBottom();
        if (current.StartsWith(lastLog, StringComparison.Ordinal))
        {
            AppendEntries(current[lastLog.Length..]);
        }
        else
        {
            LogEntries.Clear();
            AppendEntries(current);
        }

        lastLog = current;
        if (follow && LogEntries.Count > 0) LogList.ScrollIntoView(LogEntries[^1]);
    }

    private void AppendEntries(string text)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            LogEntries.Add(line);

        while (LogEntries.Count > MaximumVisibleEntries) LogEntries.RemoveAt(0);
    }

    private bool IsAtBottom()
    {
        var scrollViewer = FindScrollViewer(LogList);
        return scrollViewer is null || scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 2;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer scrollViewer) return scrollViewer;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(BuilderActivityLog.ReadAll());
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }
}
