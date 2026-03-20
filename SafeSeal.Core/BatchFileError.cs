namespace SafeSeal.Core;

public sealed record BatchFileError(string FilePath, string Code, string Message);
