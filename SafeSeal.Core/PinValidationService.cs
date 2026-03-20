using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SafeSeal.Core;

public sealed class PinValidationService : IPinValidationService
{
    private static readonly Regex PinRegex = new("^\\d{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public void ValidatePinFormat(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || !PinRegex.IsMatch(pin))
        {
            throw new ArgumentException("PIN must be exactly 6 digits.", nameof(pin));
        }
    }

    public byte[] DeriveKey(string pin, byte[] salt, int iterations)
    {
        ValidatePinFormat(pin);

        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length == 0)
        {
            throw new ArgumentException("Salt cannot be empty.", nameof(salt));
        }

        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be positive.");
        }

        return Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }
}
