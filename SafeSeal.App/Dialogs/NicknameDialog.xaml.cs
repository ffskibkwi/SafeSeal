using System.Windows;
using System.Windows.Input;

namespace SafeSeal.App.Dialogs;

public partial class NicknameDialog : Window
{
    public NicknameDialog(string title, string initialName)
        : this(title, "Image Name", initialName)
    {
    }

    public NicknameDialog(string title, string inputLabel, string initialName)
    {
        DialogTitle = string.IsNullOrWhiteSpace(title) ? "Import Image" : title;
        InputLabel = string.IsNullOrWhiteSpace(inputLabel) ? "Image Name" : inputLabel;

        InitializeComponent();

        Title = DialogTitle;
        NicknameTextBox.Text = initialName;
        Loaded += (_, _) =>
        {
            NicknameTextBox.Focus();
            NicknameTextBox.SelectAll();
        };
    }

    public string DialogTitle { get; }

    public string InputLabel { get; }

    public string Nickname => NicknameTextBox.Text.Trim();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NicknameTextBox.Text))
        {
            MessageBox.Show(
                "Please enter image name.",
                "SafeSeal",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}