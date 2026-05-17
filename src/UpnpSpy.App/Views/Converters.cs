using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using UpnpSpy.Core.Models;

namespace UpnpSpy.App.Views;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility v && v == Visibility.Collapsed;
}

/// <summary>
/// True (Visible) when the bound integer count is zero — for empty-state panels
/// that should appear *under* an otherwise-empty list.
/// </summary>
public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DiagnosticSeverity sev) return new SolidColorBrush(Colors.Transparent);
        return sev switch
        {
            DiagnosticSeverity.Error => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            DiagnosticSeverity.Warning => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            DiagnosticSeverity.Information => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
            _ => (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class SeverityToGlyphConverter : IValueConverter
{
    // Segoe Fluent Icons codepoints (Microsoft-documented PUA).
    private const string ErrorGlyph = "";    // ErrorBadge
    private const string WarningGlyph = "";  // Warning
    private const string InfoGlyph = "";     // Info
    private const string TraceGlyph = "";    // Trackers

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DiagnosticSeverity sev) return string.Empty;
        return sev switch
        {
            DiagnosticSeverity.Error => ErrorGlyph,
            DiagnosticSeverity.Warning => WarningGlyph,
            DiagnosticSeverity.Information => InfoGlyph,
            _ => TraceGlyph,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Formats the keys of an <see cref="System.Collections.Generic.IReadOnlyDictionary{TKey,TValue}"/>
/// (the property bag on an event notification) as a comma-separated list so the
/// subscription popup event row can show *which* properties changed in that event.
/// </summary>
public sealed class PropertiesDictionaryToChangedListConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is System.Collections.Generic.IReadOnlyDictionary<string, string> dict)
            return dict.Count == 0 ? "(no properties)" : string.Join(", ", dict.Keys);
        if (value is System.Collections.Generic.IDictionary<string, string> mut)
            return mut.Count == 0 ? "(no properties)" : string.Join(", ", mut.Keys);
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class SubscriptionStatusToInfoBarSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not SubscriptionStatus s) return InfoBarSeverity.Informational;
        return s switch
        {
            SubscriptionStatus.Pending => InfoBarSeverity.Informational,
            SubscriptionStatus.Active => InfoBarSeverity.Success,
            SubscriptionStatus.Lapsed => InfoBarSeverity.Warning,
            SubscriptionStatus.Failed => InfoBarSeverity.Error,
            SubscriptionStatus.Closed => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Informational,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
