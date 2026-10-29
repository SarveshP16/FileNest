using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FileNest.Core.Models;
using FileNest.Services;
using FileNest.Ui;

namespace FileNest.Views;

/// <summary>Add or edit a nest. <see cref="Edit"/> returns the edited copy, or null when cancelled.</summary>
public partial class NestEditorWindow : Window
{
    private readonly NestRule _rule;
    private readonly IReadOnlyList<NestRule> _others;
    private bool _touched;

    private NestEditorWindow(NestRule? existing, IReadOnlyList<NestRule> allRules)
    {
        InitializeComponent();
        _rule = existing?.Clone() ?? new NestRule { Icon = "folder", Color = "Teal" };
        _others = allRules.Where(r => r.Id != _rule.Id).ToList();

        var title = existing is null ? "New nest" : $"Edit {existing.Name}";
        Title = title;
        HeaderText.Text = title;
        SaveButton.Content = existing is null ? "Add nest" : "Save";

        NameBox.Text = _rule.Name;
        FolderBox.Text = string.Equals(_rule.FolderName, _rule.Name, StringComparison.Ordinal) ? "" : _rule.FolderName;
        ExtensionsBox.Text = string.Join(" ", _rule.Extensions);

        foreach (var key in Icons.NestKeys)
        {
            var option = new RadioButton
            {
                Style = (Style)FindResource("PickerOption"),
                GroupName = "Icon",
                Tag = key,
                IsChecked = key == _rule.Icon,
                ToolTip = key,
                Content = new TextBlock { Style = (Style)FindResource("Icon"), FontSize = 18, Text = Icons.ForKey(key) },
            };
            option.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
            AutomationProperties.SetName(option, $"{key} icon");
            option.Checked += (_, _) => { _rule.Icon = key; UpdatePreview(); };
            IconPanel.Children.Add(option);
        }

        foreach (var key in ThemeManager.NestColors.Keys)
        {
            var option = new RadioButton
            {
                Style = (Style)FindResource("PickerOption"),
                GroupName = "Color",
                IsChecked = key == _rule.Color,
                ToolTip = key,
                Content = new System.Windows.Shapes.Ellipse { Width = 18, Height = 18, Fill = ThemeManager.NestBar(key) },
            };
            AutomationProperties.SetName(option, $"{key} color");
            option.Checked += (_, _) => { _rule.Color = key; UpdatePreview(); };
            ColorPanel.Children.Add(option);
        }

        UpdatePreview();
        Validate();
        SourceInitialized += (_, _) =>
        {
            WindowsIntegration.UseRoundedCorners(this);
            WindowsIntegration.ApplyBackdrop(this);
        };
        Loaded += (_, _) => NameBox.Focus();
    }

    public static NestRule? Edit(Window? owner, NestRule? existing, IReadOnlyList<NestRule> allRules)
    {
        var editor = new NestEditorWindow(existing, allRules);
        DialogWindow.Place(editor, owner);
        return editor.ShowDialog() == true ? editor._rule : null;
    }

    private void UpdatePreview()
    {
        PreviewTile.Background = ThemeManager.NestBackground(_rule.Color);
        PreviewGlyph.Foreground = ThemeManager.NestForeground(_rule.Color);
        PreviewGlyph.Text = Icons.ForKey(_rule.Icon);
    }

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        _touched = true;
        Validate();
    }

    /// <summary>Returns the problem to show, or null when the form is valid.</summary>
    private string? Validate()
    {
        var name = NameBox.Text.Trim();
        var folder = FolderBox.Text.Trim();
        var effectiveFolder = folder.Length > 0 ? folder : name;
        FolderHint.Text = effectiveFolder.Length > 0 ? $"Files go to a folder called “{effectiveFolder}”. Leave empty to use the name." : "Leave empty to use the name.";

        var extensions = NestRule.ParseExtensions(ExtensionsBox.Text);
        string? error = null;

        if (name.Length == 0)
            error = "Give the nest a name.";
        else if (name.Length > 40)
            error = "Keep the name under 40 characters.";
        else if (effectiveFolder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || effectiveFolder.Trim('.').Length == 0)
            error = "The folder name can't contain \\ / : * ? \" < > |";
        else if (_others.Any(r => string.Equals(r.EffectiveFolderName, effectiveFolder, StringComparison.OrdinalIgnoreCase)))
            error = $"Another nest already uses the “{effectiveFolder}” folder.";
        else if (extensions.Count == 0)
            error = "Add at least one file extension.";
        else if (extensions.FirstOrDefault(e => e.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0) is { } bad)
            error = $"“{bad}” isn't a valid extension.";
        else if (extensions.Select(e => (Ext: e, Owner: _others.FirstOrDefault(r => r.Extensions.Contains(e))))
                     .FirstOrDefault(x => x.Owner is not null) is { Owner: { } owner } clash)
            error = $"“{clash.Ext}” is already in {owner.Name}. Remove it there first.";

        ErrorText.Text = error ?? "";
        ErrorText.Visibility = error is not null && _touched ? Visibility.Visible : Visibility.Collapsed;
        return error;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _touched = true;
        if (Validate() is not null)
        {
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _rule.Name = NameBox.Text.Trim();
        _rule.FolderName = FolderBox.Text.Trim();
        _rule.Extensions = NestRule.ParseExtensions(ExtensionsBox.Text);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
