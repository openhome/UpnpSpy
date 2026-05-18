using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

/// <summary>
/// Right-pane SSDP advertisement viewer. Newest rows arrive at index 0 (FR-055).
/// Auto-scrolls to keep the newest entry visible while the user is parked near
/// the top; once they scroll down to read history, the auto-follow disables so
/// the list does not jump under them (acceptance #3).
/// </summary>
public sealed partial class SsdpLogView : UserControl
{
    private const double StickyTopThresholdPx = 24;

    public SsdpLogViewModel ViewModel { get; }

    private ScrollViewer? _scrollViewer;
    private bool _autoScrollEnabled = true;

    public SsdpLogView(SsdpLogViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }

    private void OnListViewLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindDescendant<ScrollViewer>(EntriesList);
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += OnScrollViewChanged;
        ViewModel.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnListViewUnloaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged -= OnScrollViewChanged;
            _scrollViewer = null;
        }
        ViewModel.Entries.CollectionChanged -= OnEntriesChanged;
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate || _scrollViewer is null) return;
        _autoScrollEnabled = _scrollViewer.VerticalOffset <= StickyTopThresholdPx;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_autoScrollEnabled) return;
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.Entries.Count == 0) return;
            EntriesList.ScrollIntoView(ViewModel.Entries[0]);
        });
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
