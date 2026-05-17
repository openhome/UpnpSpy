using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using UpnpSpy.App.Platform;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class DevicePropertiesWindow : Window
{
    public DevicePropertiesViewModel ViewModel { get; }

    public DevicePropertiesWindow(DevicePropertiesViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        Title = ViewModel.Title;
        WindowChrome.TryApplyMica(this);

        Root.DataContext = ViewModel;
        Closed += OnClosed;
    }

    private void OnCopyAllClicked(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.ToClipboardText());
        Clipboard.SetContent(package);
    }

    private void OnClosed(object sender, WindowEventArgs args) => ViewModel.Dispose();
}
