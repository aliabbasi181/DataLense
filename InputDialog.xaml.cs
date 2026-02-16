using System.Windows;

namespace DataLens;

public partial class InputDialog : Window
{
    public string Value => TxtValue.Text?.Trim() ?? "";

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        TxtPrompt.Text = prompt;
        TxtValue.Text = defaultValue;
        TxtValue.SelectAll();
    }

    private void BtnOk_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
