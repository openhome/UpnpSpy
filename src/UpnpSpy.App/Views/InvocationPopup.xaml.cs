using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class InvocationPopup : Window
{
    private readonly InvocationPopupViewModel _viewModel;

    public InvocationPopup(InvocationPopupViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        Title = $"Invoke · {viewModel.Action.Name}";
        TitleText.Text = viewModel.Title;
        InputsList.ItemsSource = viewModel.Inputs;
        OutputsList.ItemsSource = viewModel.Outputs;
        InvokeButton.Command = viewModel.InvokeCommand;

        ApplyResultState();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyResultState);
    }

    private void ApplyResultState()
    {
        BusyRing.IsActive = _viewModel.IsInvoking;
        DeviceUnreachableBar.IsOpen = _viewModel.IsDeviceUnreachable;
        InvokeButton.IsEnabled = _viewModel.InvokeCommand.CanExecute(null);

        ResultPanel.Visibility = _viewModel.HasResult ? Visibility.Visible : Visibility.Collapsed;

        SuccessPanel.Visibility = _viewModel.IsSuccess ? Visibility.Visible : Visibility.Collapsed;
        SuccessMessageText.Text = _viewModel.SuccessMessage;

        FaultPanel.Visibility = _viewModel.IsFault ? Visibility.Visible : Visibility.Collapsed;
        FaultStatusText.Text = $"HTTP status: {_viewModel.FaultHttpStatus.ToString(CultureInfo.InvariantCulture)}";
        FaultCodeText.Text = $"UPnP error code: {_viewModel.FaultErrorCode.ToString(CultureInfo.InvariantCulture)}";
        FaultDescriptionText.Text = _viewModel.FaultErrorDescription;
        FaultRawXmlText.Text = _viewModel.FaultRawXml;

        TransportPanel.Visibility = _viewModel.IsTransportError ? Visibility.Visible : Visibility.Collapsed;
        TransportMessageText.Text = _viewModel.TransportErrorMessage;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }
}
