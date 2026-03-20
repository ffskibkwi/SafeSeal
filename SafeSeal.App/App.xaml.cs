using System.Windows;
using System.Windows.Threading;
using SafeSeal.App.Services;
using SafeSeal.Core;

namespace SafeSeal.App;

public partial class App : Application
{
    private readonly ILoggingService _logger = LogManager.SharedLogger;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;

        _logger.Info(
            nameof(App),
            "app_startup",
            fields: new Dictionary<string, object?>
            {
                ["argsCount"] = e.Args.Length,
            });

        LocalizationService.Instance.Initialize();
        ThemeService.Instance.Initialize(this);
        WorkspaceStateService.Instance.Initialize();

        base.OnStartup(e);

        MainWindow mainWindow = new();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;

        _logger.Info(
            nameof(App),
            "app_exit",
            fields: new Dictionary<string, object?>
            {
                ["exitCode"] = e.ApplicationExitCode,
            });

        _logger.Flush();

        WorkspaceStateService.Instance.Persist();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string crashId = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);

        _logger.Critical(
            nameof(App),
            "dispatcher_unhandled_exception",
            operationId: crashId,
            fields: new Dictionary<string, object?>
            {
                ["crashId"] = crashId,
                ["threadId"] = Environment.CurrentManagedThreadId,
                ["isTerminating"] = false,
                ["isDispatcherThread"] = true,
            },
            exception: e.Exception);

        _logger.Flush();

        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        string crashId = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        Exception exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException("Unhandled non-exception object reached AppDomain handler.");

        _logger.Critical(
            nameof(App),
            "appdomain_unhandled_exception",
            operationId: crashId,
            fields: new Dictionary<string, object?>
            {
                ["crashId"] = crashId,
                ["threadId"] = Environment.CurrentManagedThreadId,
                ["isTerminating"] = e.IsTerminating,
                ["isDispatcherThread"] = false,
            },
            exception: exception);

        _logger.Flush();
    }
}
