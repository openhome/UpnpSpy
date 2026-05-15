using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class SubscriptionPopup : Window
{
    private readonly SubscriptionPopupViewModel _viewModel;

    public SubscriptionPopup(SubscriptionPopupViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        Title = $"Subscribe · {viewModel.Service.Label}";
        TitleText.Text = viewModel.Title;
        EventsList.ItemsSource = viewModel.Events;
        LatestList.ItemsSource = viewModel.LatestProperties;

        ApplyStatus();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    public Task StartAsync() => _viewModel.StartAsync();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyStatus);
    }

    private void ApplyStatus()
    {
        CallbackText.Text = _viewModel.State?.CallbackUrl.ToString() ?? string.Empty;

        switch (_viewModel.Status)
        {
            case SubscriptionStatus.Pending:
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.Title = "Subscription pending";
                StatusBar.Message = "Waiting for the device to confirm…";
                break;

            case SubscriptionStatus.Active:
                StatusBar.Severity = InfoBarSeverity.Success;
                StatusBar.Title = "Subscribed";
                StatusBar.Message = _viewModel.State?.Sid is null
                    ? "Active"
                    : $"SID {_viewModel.State.Sid} · granted {_viewModel.State.GrantedTimeout}";
                break;

            case SubscriptionStatus.Lapsed:
                StatusBar.Severity = InfoBarSeverity.Warning;
                StatusBar.Title = "Subscription lapsed";
                StatusBar.Message = _viewModel.FailureReason ?? "Renewal failed.";
                break;

            case SubscriptionStatus.Failed:
                StatusBar.Severity = InfoBarSeverity.Error;
                StatusBar.Title = "Subscribe failed";
                StatusBar.Message = _viewModel.FailureReason ?? "SUBSCRIBE failed.";
                break;

            case SubscriptionStatus.Closed:
                StatusBar.Severity = InfoBarSeverity.Warning;
                StatusBar.Title = _viewModel.IsDeviceUnreachable
                    ? "Device no longer reachable"
                    : "Subscription closed";
                StatusBar.Message = _viewModel.IsDeviceUnreachable
                    ? "The device has gone away. Close this window."
                    : "Subscription has been closed.";
                break;
        }
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        try
        {
            await _viewModel.CloseAsync();
        }
        finally
        {
            await _viewModel.DisposeAsync();
        }
    }
}
