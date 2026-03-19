using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Data.Sqlite;

namespace SafeSeal.App.Services;

public sealed class UserFacingErrorHandler
{
    public void Show(Exception exception)
    {
        string message = exception switch
        {
            CryptographicException => "This file is locked to another user or device. Security policy prevents access.",
            InvalidDataException => "This file is not a valid SafeSeal vault item.",
            NotSupportedException => "This vault item was created with a newer version of SafeSeal and cannot be opened.",
            IOException => "SafeSeal could not access an internal vault file. Please try again.",
            SqliteException => "SafeSeal could not update the local catalog. Please try again.",
            InvalidOperationException => "A document with this name already exists. Please overwrite or choose a different name.",
            _ => "An unexpected error occurred. No sensitive data was written to disk.",
        };

        MessageBox.Show(
            message,
            "SafeSeal",
            MessageBoxButton.OK,
            MessageBoxImage.Warning,
            MessageBoxResult.OK,
            MessageBoxOptions.DefaultDesktopOnly);
    }
}