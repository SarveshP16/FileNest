using FileNest.Ui;
using Forms = System.Windows.Forms;

namespace FileNest.Services;

/// <summary>The notification-area icon and its right-click menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private AppIcon.TrayIconImage? _image;

    public event Action? OpenRequested;
    public event Action? OrganizeRequested;
    public event Action? PauseToggleRequested;
    public event Action? OpenFolderRequested;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        var open = new Forms.ToolStripMenuItem("Open FileNest", null, (_, _) => OpenRequested?.Invoke());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripMenuItem("Organize now", null, (_, _) => OrganizeRequested?.Invoke()));
        _pauseItem = new Forms.ToolStripMenuItem("Pause watching", null, (_, _) => PauseToggleRequested?.Invoke());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Open Downloads", null, (_, _) => OpenFolderRequested?.Invoke()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Quit", null, (_, _) => QuitRequested?.Invoke()));

        _icon = new Forms.NotifyIcon
        {
            Text = "FileNest",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenRequested?.Invoke();
        };

        UpdateIcon();
        ThemeManager.Changed += UpdateIcon;
    }

    public void SetPaused(bool paused)
    {
        _pauseItem.Text = paused ? "Resume watching" : "Pause watching";
        _icon.Text = paused ? "FileNest (paused)" : "FileNest";
    }

    private void UpdateIcon()
    {
        var next = AppIcon.RenderTray(ThemeManager.AccentColor);
        _icon.Icon = next.Icon;
        _image?.Dispose();
        _image = next;
    }

    public void Dispose()
    {
        ThemeManager.Changed -= UpdateIcon;
        _icon.Visible = false;
        _icon.Dispose();
        _image?.Dispose();
    }
}
