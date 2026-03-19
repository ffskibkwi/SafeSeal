using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using SafeSeal.App.Services;

namespace SafeSeal.App.Dialogs;

public partial class AboutDialog : Window, INotifyPropertyChanged
{
    private readonly LocalizationService _localization;

    public AboutDialog()
    {
        _localization = LocalizationService.Instance;
        _localization.LanguageChanged += OnLanguageChanged;

        CloseCommand = new RelayCommand(() => DialogResult = false);

        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public string AboutTitleText => _localization["AboutTitle"];

    public string VersionText => _localization["AboutVersion"];

    public string LicenseText => _localization["AboutLicense"];

    public string GithubText => _localization["AboutOpenRepo"];

    public string CloseText => _localization["Close"];

    protected override void OnClosed(EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });

        e.Handled = true;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AboutTitleText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(LicenseText));
        OnPropertyChanged(nameof(GithubText));
        OnPropertyChanged(nameof(CloseText));
    }

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


