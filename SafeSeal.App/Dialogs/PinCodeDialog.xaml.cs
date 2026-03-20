using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SafeSeal.App.Services;

namespace SafeSeal.App.Dialogs;

public partial class PinCodeDialog : Window
{
    private readonly LocalizationService _localization;
    private TextBox[] _pinBoxes = [];

    public PinCodeDialog()
    {
        _localization = LocalizationService.Instance;
        InitializeComponent();
        DataContext = this;

        _pinBoxes = [PinBox1, PinBox2, PinBox3, PinBox4, PinBox5, PinBox6];

        Loaded += (_, _) =>
        {
            PinBox1.Focus();
            PinBox1.SelectAll();
        };
    }

    public string DialogTitle => _localization["TransferPinTitle"];

    public string PinLabel => _localization["TransferPinLabel"];

    public string PinHint => _localization["TransferPinHint"];

    public string ConfirmText => _localization["Save"];

    public string CancelText => _localization["Cancel"];

    public string Pin => string.Concat(_pinBoxes.Select(static box => box.Text));

    private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Length != 1 || !char.IsDigit(e.Text[0]);
    }

    private void PinBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        string digits = new(box.Text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            box.Text = string.Empty;
            return;
        }

        if (digits.Length > 1)
        {
            digits = digits[^1].ToString();
        }

        if (!string.Equals(box.Text, digits, StringComparison.Ordinal))
        {
            box.Text = digits;
            box.SelectionStart = box.Text.Length;
        }

        if (box.Tag is not string tagText || !int.TryParse(tagText, out int index))
        {
            return;
        }

        if (box.Text.Length == 1 && index < _pinBoxes.Length - 1)
        {
            _pinBoxes[index + 1].Focus();
            _pinBoxes[index + 1].SelectAll();
        }

        HintText.Text = PinHint;
        HintText.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
    }

    private void PinBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        if (e.Key == Key.Back && box.Tag is string tagText && int.TryParse(tagText, out int index))
        {
            if (!string.IsNullOrEmpty(box.Text))
            {
                box.Clear();
                e.Handled = true;
                return;
            }

            if (index > 0)
            {
                _pinBoxes[index - 1].Clear();
                _pinBoxes[index - 1].Focus();
                e.Handled = true;
            }
        }
    }

    private void PinBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        string text = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        string digits = new(text.Where(char.IsDigit).ToArray());

        if (digits.Length != 6)
        {
            e.CancelCommand();
            return;
        }

        for (int i = 0; i < _pinBoxes.Length; i++)
        {
            _pinBoxes[i].Text = digits[i].ToString();
        }

        _pinBoxes[^1].Focus();
        _pinBoxes[^1].SelectAll();
        e.CancelCommand();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pinBoxes.Any(static box => box.Text.Length != 1))
        {
            HintText.Text = _localization["TransferPinRequired"];
            HintText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            return;
        }

        DialogResult = true;
    }
}
