using System.ComponentModel;
using System.Windows;
using FileNest.Services;
using FileNest.Ui;
using FileNest.ViewModels;

namespace FileNest;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    /// <summary>Raised when the user closes the window and it should hide to the tray instead.</summary>
    public event Action? HiddenToTray;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        SourceInitialized += (_, _) =>
        {
            WindowsIntegration.UseRoundedCorners(this);
            WindowsIntegration.ApplyBackdrop(this);
        };
        StateChanged += (_, _) => OnStateChanged();
        UpdateIcon();
        ThemeManager.Changed += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        UpdateIcon();
        WindowsIntegration.ApplyBackdrop(this);
    }

    /// <summary>Set by the app when quitting for real, so closing isn't turned into hiding.</summary>
    public bool AllowClose { get; set; }

    public void ShowAndActivate()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void UpdateIcon() => Icon = AppIcon.Render(64, ThemeManager.AccentColor);

    private void OnStateChanged()
    {
        // A borderless window overhangs the screen by the resize border when maximized; pad it back in.
        var maximized = WindowState == WindowState.Maximized;
        var b = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = maximized ? new Thickness(b.Left + 4, b.Top + 4, b.Right + 4, b.Bottom + 4) : new Thickness(0);
        MaximizeButton.Content = maximized ? Icons.Squares : Icons.Square;
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose && _vm.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }

        if (!AllowClose)
        {
            // Close-to-tray is off: closing the window quits the app.
            e.Cancel = true;
            ((App)Application.Current).Quit();
            return;
        }

        ThemeManager.Changed -= OnThemeChanged;
        base.OnClosing(e);
    }
}
