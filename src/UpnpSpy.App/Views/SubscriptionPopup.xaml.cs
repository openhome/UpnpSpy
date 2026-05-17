using Microsoft.UI.Xaml;
using UpnpSpy.App.Platform;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class SubscriptionPopup : Window
{
    public SubscriptionPopupViewModel ViewModel { get; }

    public SubscriptionPopup(SubscriptionPopupViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        Title = $"Subscribe · {viewModel.Service.Label}";
        WindowChrome.TryApplyMica(this);

        Root.DataContext = ViewModel;
        Closed += OnClosed;
    }

    public Task StartAsync() => ViewModel.StartAsync();

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        try
        {
            await ViewModel.CloseAsync();
        }
        finally
        {
            await ViewModel.DisposeAsync();
        }
    }
}
