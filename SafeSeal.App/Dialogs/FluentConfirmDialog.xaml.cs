using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace SafeSeal.App.Dialogs;

public partial class FluentConfirmDialog : Window, INotifyPropertyChanged
{
    public FluentConfirmDialog(string titleText, string messageText, string confirmText, string cancelText)
    {
        TitleText = titleText;
        MessageText = messageText;
        ConfirmText = confirmText;
        CancelText = cancelText;

        ConfirmCommand = new RelayCommand(() =>
        {
            IsConfirmed = true;
            DialogResult = true;
        });

        CancelCommand = new RelayCommand(() =>
        {
            IsConfirmed = false;
            DialogResult = false;
        });

        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public bool IsConfirmed { get; private set; }

    public string TitleText { get; }

    public string MessageText { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }
}


