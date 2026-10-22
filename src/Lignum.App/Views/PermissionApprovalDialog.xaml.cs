using System.Windows;
using Lignum.App.Assistant.Permissions;

namespace Lignum.App.Views;

/// <summary>Asks the user about one <see cref="PermissionRequest"/>. Deny (Esc, Enter, closing) is the default.</summary>
public partial class PermissionApprovalDialog : Window
{
    public PermissionApprovalDialog(PermissionRequest request, IReadOnlyList<ScopeOption> options)
    {
        InitializeComponent();
        Heading.Text = request.Access switch
        {
            AiAccess.Execute => "The AI wants to run this command:",
            AiAccess.Write => request.IsFolder ? "The AI wants to write in this folder:" : "The AI wants to write this file:",
            _ => request.IsFolder ? "The AI wants to read this folder:" : "The AI wants to read this file:",
        };
        TargetBox.Text = request.Access == AiAccess.Execute ? request.CommandLine ?? request.Target : request.Target;
        if (!string.IsNullOrEmpty(request.Detail))
        {
            DetailBox.Text = request.Detail;
            DetailBox.Visibility = Visibility.Visible;
        }

        ScopeList.ItemsSource = options;
        ScopeList.SelectedIndex = 0;
    }

    /// <summary>The user's answer once the dialog closed; null = denied.</summary>
    public ApprovalAnswer? Answer { get; private set; }

    private void OnOnceClick(object sender, RoutedEventArgs e) => Finish(GrantLifetime.Once);

    private void OnSessionClick(object sender, RoutedEventArgs e) => Finish(GrantLifetime.Session);

    private void OnAlwaysClick(object sender, RoutedEventArgs e) => Finish(GrantLifetime.Always);

    private void Finish(GrantLifetime lifetime)
    {
        if (ScopeList.SelectedItem is not ScopeOption scope) return;
        Answer = new ApprovalAnswer(lifetime, scope);
        DialogResult = true;
    }
}

/// <summary>The app's <see cref="IPermissionApprover"/>: a modal <see cref="PermissionApprovalDialog"/> over the main window.</summary>
public sealed class DialogPermissionApprover : IPermissionApprover
{
    public Task<ApprovalAnswer?> AskAsync(PermissionRequest request, IReadOnlyList<ScopeOption> options, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return Task.FromResult<ApprovalAnswer?>(null);
        return dispatcher.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested) return null;
            var dialog = new PermissionApprovalDialog(request, options) { Owner = Application.Current?.MainWindow };
            using var closeOnCancel = cancellationToken.Register(() => dispatcher.InvokeAsync(dialog.Close));
            return dialog.ShowDialog() == true ? dialog.Answer : null;
        }).Task;
    }
}
