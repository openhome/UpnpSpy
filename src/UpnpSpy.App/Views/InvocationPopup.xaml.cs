using Microsoft.UI.Xaml;
using UpnpSpy.App.Platform;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class InvocationPopup : Window
{
    public InvocationPopupViewModel ViewModel { get; }

    public InvocationPopup(InvocationPopupViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        Title = $"Invoke · {viewModel.Action.Name}";
        WindowChrome.TryApplyMica(this);

        Root.DataContext = ViewModel;
        Closed += OnClosed;
    }

    private void OnClosed(object sender, WindowEventArgs args) => ViewModel.Dispose();
}
