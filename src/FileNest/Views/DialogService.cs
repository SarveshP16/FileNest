using System.IO;
using System.Windows;
using FileNest.Core.Models;
using FileNest.ViewModels;
using Microsoft.Win32;

namespace FileNest.Views;

public sealed class DialogService(Func<Window?> owner) : IDialogService
{
    public bool Confirm(string title, string message, string confirmText, bool danger = false) =>
        DialogWindow.Ask(owner(), title, message, confirmText, danger);

    public void Info(string title, string message) => DialogWindow.Tell(owner(), title, message);

    public NestRule? EditNest(NestRule? existing, IReadOnlyList<NestRule> allRules) =>
        NestEditorWindow.Edit(owner(), existing, allRules);

    public string? PickFolder(string title, string initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = Directory.Exists(initialFolder) ? initialFolder : null,
        };
        return dialog.ShowDialog(owner()) == true ? dialog.FolderName : null;
    }
}
