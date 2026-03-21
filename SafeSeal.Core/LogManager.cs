namespace SafeSeal.Core;

public static class LogManager
{
    private static readonly Lazy<ILoggingService> Shared =
        new(CreateSharedLogger, LazyThreadSafetyMode.ExecutionAndPublication);

    public static ILoggingService SharedLogger => Shared.Value;

    private static ILoggingService CreateSharedLogger()
    {
        try
        {
            return new FileLoggingService();
        }
        catch
        {
            // Logging must not break app startup.
            return new NullLoggingService();
        }
    }
}