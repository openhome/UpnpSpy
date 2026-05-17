using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

/// <summary>
/// Right-pane SSDP advertisement viewer. Auto-scrolls to newest entries while the
/// user is parked near the bottom; once they scroll up, the auto-follow disables
/// so they can read history without the list jumping under them (acceptance #3).
/// </summary>
public sealed partial class SsdpLogView : UserControl
{
    private const double StickyBottomThresholdPx = 24;

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
        var distanceFromBottom = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        _autoScrollEnabled = distanceFromBottom <= StickyBottomThresholdPx;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_autoScrollEnabled) return;
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.Entries.Count == 0) return;
            EntriesList.ScrollIntoView(ViewModel.Entries[^1]);
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
