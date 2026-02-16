using System.Windows;
using DataLens.Models;

namespace DataLens;

public partial class WorkspaceEditWindow : Window
{
    public Workspace? Result { get; private set; }
    private readonly Workspace? _existing;

    public WorkspaceEditWindow(Workspace? existing = null)
    {
        _existing = existing;
        InitializeComponent();
        if (existing != null)
        {
            TxtName.Text = existing.Name;
            TxtHost.Text = existing.Host;
            TxtPort.Text = existing.Port.ToString();
            TxtDatabase.Text = existing.Database;
            TxtUserName.Text = existing.UserName;
            TxtPassword.Password = existing.Password;
        }
    }

    private void BtnSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Please enter a workspace name.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(TxtHost.Text))
        {
            MessageBox.Show("Please enter the server host.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(TxtPort.Text, out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Please enter a valid port (1-65535).", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(TxtDatabase.Text))
        {
            MessageBox.Show("Please enter the database name.", "DataLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new Workspace
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString("N"),
            Name = TxtName.Text.Trim(),
            Host = TxtHost.Text.Trim(),
            Port = port,
            Database = TxtDatabase.Text.Trim(),
            UserName = TxtUserName.Text?.Trim() ?? "",
            Password = TxtPassword.Password
        };
        DialogResult = true;
        Close();
    }
}
