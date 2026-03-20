using System.Windows;
using SafeSeal.App.Services;

namespace SafeSeal.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LocalizationService.Instance.Initialize();
        ThemeService.Instance.Initialize(this);

        base.OnStartup(e);

        MainWindow mainWindow = new();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
