using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using UpnpSpy.App.Platform;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.App.Views;

public sealed partial class DiagnosticsWindow : Window
{
    public DiagnosticsViewerViewModel ViewModel { get; }

    public DiagnosticsWindow(DiagnosticsViewerViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        WindowChrome.TryApplyMica(this);

        Root.DataContext = ViewModel;
        ViewModel.Start();
        Closed += OnClosed;
    }

    private void OnSeverityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not string tag) return;
        if (Enum.TryParse<DiagnosticSeverity>(tag, out var sev))
            ViewModel.MinSeverity = sev;
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        var rows = EntriesList.SelectedItems.OfType<DiagnosticEntryRow>().ToList();
        if (rows.Count == 0)
            rows = ViewModel.FilteredEntries.ToList();
        if (rows.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Append(row.Timestamp.LocalDateTime.ToString("u", CultureInfo.InvariantCulture))
              .Append('\t').Append(row.Severity)
              .Append('\t').Append(row.Category)
              .Append('\t').Append(row.Identity)
              .Append('\t').Append(row.Endpoint)
              .Append('\t').Append(row.Message)
              .Append('\n');
        }

        var package = new DataPackage();
        package.SetText(sb.ToString());
        Clipboard.SetContent(package);
    }

    private void OnOpenLogFolderClicked(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.LogFilePath;
        if (string.IsNullOrEmpty(path)) return;

        // explorer.exe /select,<path> opens Explorer with that file highlighted.
        // Fall back to opening just the folder if the file doesn't yet exist
        // (the rolling sink creates it on the first write).
        try
        {
            var startInfo = System.IO.File.Exists(path)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }
                : new ProcessStartInfo("explorer.exe", $"\"{System.IO.Path.GetDirectoryName(path)}\"") { UseShellExecute = true };
            Process.Start(startInfo);
        }
        catch
        {
            // Best-effort — failing to launch Explorer should not crash the app.
        }
    }

    private void OnClosed(object sender, WindowEventArgs args) => ViewModel.Dispose();
}
