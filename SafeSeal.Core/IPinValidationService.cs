namespace SafeSeal.Core;

public interface IPinValidationService
{
    void ValidatePinFormat(string pin);

    byte[] DeriveKey(string pin, byte[] salt, int iterations);
}
