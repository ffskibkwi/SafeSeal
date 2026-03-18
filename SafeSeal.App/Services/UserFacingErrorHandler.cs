using System.IO;
using System.Security.Cryptography;
using System.Windows;

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
