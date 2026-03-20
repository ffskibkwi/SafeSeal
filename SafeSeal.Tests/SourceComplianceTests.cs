using System.IO;
using Xunit;

namespace SafeSeal.Tests;

public sealed class SourceComplianceTests
{
    [Fact]
    public void MainViewModel_AndDialogs_NoHardcodedOperationFeedbackStringsRemain()
    {
        string root = FindRepoRoot();

        string vm = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "ViewModels", "MainViewModel.cs"));
        string err = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Services", "UserFacingErrorHandler.cs"));
        string dialog = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Dialogs", "NicknameDialog.xaml.cs"));

        Assert.DoesNotContain("Importing document...", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("Exporting image...", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("Renaming document...", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("Deleting document...", vm, StringComparison.Ordinal);

        Assert.DoesNotContain("This file is locked to another user or device", err, StringComparison.Ordinal);
        Assert.DoesNotContain("Please enter image name.", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeStartup_InitializesWithoutStartupUri()
    {
        string root = FindRepoRoot();
        string appXaml = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "App.xaml"));
        string appCode = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "App.xaml.cs"));

        Assert.DoesNotContain("StartupUri=", appXaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ThemeService.Instance.Initialize(this);", appCode, StringComparison.Ordinal);
        Assert.Contains("MainWindow mainWindow = new();", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void JapaneseResourceFile_ExistsAndContainsCoreKeys()
    {
        string root = FindRepoRoot();
        string ja = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Resources", "Strings.ja-JP.resx"));

        Assert.Contains("name=\"Import\"", ja, StringComparison.Ordinal);
        Assert.Contains("name=\"Export\"", ja, StringComparison.Ordinal);
        Assert.Contains("name=\"Purpose\"", ja, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SafeSeal.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("SafeSeal.sln not found from test base directory.");
    }
}
