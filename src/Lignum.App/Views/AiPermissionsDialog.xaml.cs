using System.Windows;
using Lignum.App.Assistant.Permissions;

namespace Lignum.App.Views;

/// <summary>Lists the session and always grants of a <see cref="PermissionStore"/> and revokes them.</summary>
public partial class AiPermissionsDialog : Window
{
    private readonly PermissionStore _store;

    public AiPermissionsDialog(PermissionStore store)
    {
        _store = store;
        InitializeComponent();
        store.Changed += OnChanged;
        Closed += (_, _) => store.Changed -= OnChanged;
        Refresh();
    }

    /// <summary>One row: a grant and how long it lasts.</summary>
    public sealed record Row(PermissionGrant Grant, string Lifetime);

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        var rows = _store.AlwaysGrants.Select(g => new Row(g, "Always"))
            .Concat(_store.SessionGrants.Select(g => new Row(g, "Session"))).ToList();
        GrantList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRevokeClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in GrantList.SelectedItems.Cast<Row>().ToList()) _store.Revoke(row.Grant);
    }

    private void OnRevokeAllClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Revoke every AI permission? The AI will ask again next time.", "AI permissions",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            _store.RevokeAll();
    }
}
