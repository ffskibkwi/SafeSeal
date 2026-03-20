namespace SafeSeal.Core;

public static class LogManager
{
    private static readonly Lazy<ILoggingService> Shared =
        new(static () => new FileLoggingService(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static ILoggingService SharedLogger => Shared.Value;
}
