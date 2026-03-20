namespace SafeSeal.App.Services;

public sealed record AppPreferences
{
    public string Language { get; init; } = "en-US";

    public AppTheme Theme { get; init; } = AppTheme.System;

    public WorkspacePreferences Workspace { get; init; } = WorkspacePreferences.CreateDefault();
}
