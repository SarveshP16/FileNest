using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FileNest.Views;

/// <summary>
/// The bottom-right pop-up from the mockup. One instance is reused: a new message replaces the old one
/// and restarts the timer. Hovering keeps it open.
/// </summary>
public partial class ToastWindow : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
    private readonly DispatcherTimer _timer = new() { Interval = Lifetime };
    private Action? _action;

    public ToastWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => FadeOut();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => _timer.Start();
        SizeChanged += (_, _) => PlaceInCorner();
    }

    /// <summary>Shows a message with an optional link button ("Undo", "Review", "Restart").</summary>
    public void ShowMessage(string message, string? actionText, Action? action)
    {
        MessageText.Text = message;
        MessageText.ToolTip = message;
        _action = action;
        ActionButton.Content = actionText;
        ActionButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;

        BeginAnimation(OpacityProperty, null);
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        }
        else
        {
            Opacity = 1;
        }

        PlaceInCorner();
        _timer.Stop();
        if (!IsMouseOver)
            _timer.Start();
    }

    private void PlaceInCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 4;
        Top = area.Bottom - ActualHeight - 4;
    }

    private void FadeOut()
    {
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (Opacity == 0)
                Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        var action = _action;
        _action = null;
        FadeOut();
        action?.Invoke();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => FadeOut();

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsInsideButton(e.OriginalSource as DependencyObject))
            BodyClicked?.Invoke();
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        for (; element is not null; element = System.Windows.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is System.Windows.Controls.Primitives.ButtonBase)
                return true;
        }

        return false;
    }

    /// <summary>Clicking the toast (not its buttons) opens the main window.</summary>
    public event Action? BodyClicked;
}
