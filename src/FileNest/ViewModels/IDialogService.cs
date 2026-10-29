using FileNest.Core.Models;

namespace FileNest.ViewModels;

/// <summary>Windows the view model needs to open, implemented by the views.</summary>
public interface IDialogService
{
    bool Confirm(string title, string message, string confirmText, bool danger = false);

    void Info(string title, string message);

    /// <summary>Opens the nest editor. Returns the edited copy, or null if cancelled.</summary>
    NestRule? EditNest(NestRule? existing, IReadOnlyList<NestRule> allRules);

    string? PickFolder(string title, string initialFolder);
}

/// <summary>A bottom-right pop-up: a message and an optional link button such as "Undo" or "Restart".</summary>
public sealed record ToastRequest(string Message, string? ActionText = null, Action? Action = null);
