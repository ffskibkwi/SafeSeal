using System.Windows;
using System.Windows.Input;
using SafeSeal.App.Services;

namespace SafeSeal.App.Dialogs;

public partial class NicknameDialog : Window
{
    private readonly LocalizationService _localization;

    public NicknameDialog(string title, string initialName)
        : this(title, LocalizationService.Instance["DialogImageNameLabel"], initialName)
    {
    }

    public NicknameDialog(string title, string inputLabel, string initialName)
    {
        _localization = LocalizationService.Instance;
        DialogTitle = string.IsNullOrWhiteSpace(title) ? _localization["DialogImportTitle"] : title;
        InputLabel = string.IsNullOrWhiteSpace(inputLabel) ? _localization["DialogImageNameLabel"] : inputLabel;
        ShowImportHint = string.Equals(DialogTitle, _localization["DialogImportTitle"], StringComparison.Ordinal);

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

    public bool ShowImportHint { get; }

    public string SaveText => _localization["Save"];

    public string CancelText => _localization["Cancel"];

    public string ImportHintTitle => _localization["ImportHintTitle"];

    public string ImportHintLine1 => _localization["ImportHintLine1"];

    public string ImportHintLine2 => _localization["ImportHintLine2"];

    public string ImportHintLine3 => _localization["ImportHintLine3"];

    public string ImportHintLine4 => _localization["ImportHintLine4"];

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
                _localization["DialogNameRequired"],
                _localization["ErrorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
