using Microsoft.UI.Xaml;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class DiagnosticsWindow : Window
{
    private readonly DiagnosticsViewerViewModel _viewModel;

    public DiagnosticsWindow(DiagnosticsViewerViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        EntriesList.ItemsSource = _viewModel.Entries;
        _viewModel.Start();
        Closed += OnClosed;
    }

    private void OnClosed(object sender, WindowEventArgs args) => _viewModel.Dispose();
}
