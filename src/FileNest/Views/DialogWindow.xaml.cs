using System.Windows;
using FileNest.Services;

namespace FileNest.Views;

/// <summary>A themed confirm/info box. Use <see cref="Ask"/> or <see cref="Tell"/>.</summary>
public partial class DialogWindow : Window
{
    private DialogWindow(string title, string message, string confirmText, string? cancelText, bool danger)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (danger)
            ConfirmButton.Style = (Style)FindResource("DangerButton");
        if (cancelText is null)
            CancelButton.Visibility = Visibility.Collapsed;
        else
            CancelButton.Content = cancelText;
        SourceInitialized += (_, _) =>
        {
            WindowsIntegration.UseRoundedCorners(this);
            WindowsIntegration.ApplyBackdrop(this);
        };
    }

    public static bool Ask(Window? owner, string title, string message, string confirmText, bool danger = false)
    {
        var dialog = new DialogWindow(title, message, confirmText, "Cancel", danger);
        Place(dialog, owner);
        return dialog.ShowDialog() == true;
    }

    public static void Tell(Window? owner, string title, string message)
    {
        var dialog = new DialogWindow(title, message, "OK", null, false);
        Place(dialog, owner);
        dialog.ShowDialog();
    }

    internal static void Place(Window dialog, Window? owner)
    {
        if (owner is { IsVisible: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
