using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Data.Sqlite;

namespace SafeSeal.App.Services;

public sealed class UserFacingErrorHandler
{
    private readonly LocalizationService _localization;

    public UserFacingErrorHandler()
    {
        _localization = LocalizationService.Instance;
    }

    public void Show(Exception exception)
    {
        string message = exception switch
        {
            CryptographicException => _localization["ErrorCryptographic"],
            InvalidDataException => _localization["ErrorInvalidData"],
            NotSupportedException => _localization["ErrorNotSupported"],
            IOException => _localization["ErrorIo"],
            SqliteException => _localization["ErrorSqlite"],
            InvalidOperationException => _localization["ErrorDuplicateName"],
            _ => _localization["ErrorUnexpected"],
        };

        MessageBox.Show(
            message,
            _localization["ErrorTitle"],
            MessageBoxButton.OK,
            MessageBoxImage.Warning,
            MessageBoxResult.OK,
            MessageBoxOptions.DefaultDesktopOnly);
    }
}
