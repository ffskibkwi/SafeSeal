using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace SafeSeal.App.Dialogs;

public partial class FluentMessageDialog : Window, INotifyPropertyChanged
{
    public FluentMessageDialog(string titleText, string messageText, string closeText)
    {
        TitleText = titleText;
        MessageText = messageText;
        CloseText = closeText;

        CloseCommand = new RelayCommand(() => DialogResult = true);

        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public string TitleText { get; }

    public string MessageText { get; }

    public string CloseText { get; }

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
