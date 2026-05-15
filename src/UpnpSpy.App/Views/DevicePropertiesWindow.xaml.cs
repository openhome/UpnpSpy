using System.ComponentModel;
using Microsoft.UI.Xaml;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class DevicePropertiesWindow : Window
{
    private readonly DevicePropertiesViewModel _viewModel;

    public DevicePropertiesWindow(DevicePropertiesViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        Title = _viewModel.Title;
        TitleText.Text = _viewModel.Title;

        FriendlyNameText.Text = _viewModel.FriendlyName;
        DeviceTypeText.Text = _viewModel.DeviceType;
        UuidText.Text = _viewModel.Uuid;
        BindHyperlink(PresentationUrlLink, _viewModel.PresentationUrl, _viewModel.PresentationUrlText);

        ManufacturerText.Text = _viewModel.Manufacturer;
        BindHyperlink(ManufacturerUrlLink, _viewModel.ManufacturerUrl, _viewModel.ManufacturerUrlText);
        ModelNameText.Text = _viewModel.ModelName;
        ModelNumberText.Text = _viewModel.ModelNumber;
        ModelDescriptionText.Text = _viewModel.ModelDescription;
        BindHyperlink(ModelUrlLink, _viewModel.ModelUrl, _viewModel.ModelUrlText);
        SerialNumberText.Text = _viewModel.SerialNumber;
        UpcText.Text = _viewModel.Upc;

        BindHyperlink(LocationUrlLink, _viewModel.LocationUrl, _viewModel.LocationUrlText);
        EndpointText.Text = _viewModel.Endpoint;
        ServerHeaderText.Text = _viewModel.ServerHeader;
        CacheControlText.Text = _viewModel.CacheControlMaxAge;

        FirstSeenText.Text = _viewModel.FirstSeenUtc;
        LastSeenText.Text = _viewModel.LastSeenUtc;
        AliveCountText.Text = _viewModel.AliveCount;
        BootIdText.Text = _viewModel.BootId;
        ConfigIdText.Text = _viewModel.ConfigId;

        if (_viewModel.EmbeddedDevices.Count == 0)
        {
            NoEmbeddedText.Visibility = Visibility.Visible;
            EmbeddedDevicesList.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmbeddedDevicesList.ItemsSource = _viewModel.EmbeddedDevices;
        }

        UnreachableBar.IsOpen = _viewModel.IsDeviceUnreachable;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevicePropertiesViewModel.IsDeviceUnreachable))
            DispatcherQueue.TryEnqueue(() => UnreachableBar.IsOpen = _viewModel.IsDeviceUnreachable);
    }

    private static void BindHyperlink(Microsoft.UI.Xaml.Controls.HyperlinkButton link, Uri? uri, string text)
    {
        link.Content = text;
        if (uri is not null)
        {
            link.NavigateUri = uri;
            link.IsEnabled = true;
        }
        else
        {
            link.NavigateUri = null;
            link.IsEnabled = false;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }
}
