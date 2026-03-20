using System.IO;
using System.Text.RegularExpressions;
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
    public void AppStartup_RegistersGlobalCrashCaptureAndFlushesLogger()
    {
        string root = FindRepoRoot();
        string appCode = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "App.xaml.cs"));

        Assert.Contains("DispatcherUnhandledException += OnDispatcherUnhandledException;", appCode, StringComparison.Ordinal);
        Assert.Contains("AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;", appCode, StringComparison.Ordinal);
        Assert.Contains("_logger.Critical(", appCode, StringComparison.Ordinal);
        Assert.Contains("_logger.Flush();", appCode, StringComparison.Ordinal);
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


    [Fact]
    public void EnglishAndChineseTintLabels_DoNotContainJapaneseKana()
    {
        string root = FindRepoRoot();
        string en = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Resources", "Strings.resx"));
        string zh = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Resources", "Strings.zh-CN.resx"));

        Regex kanaRegex = new("[\\u3040-\\u30FF]", RegexOptions.CultureInvariant);
        string[] tintKeys = ["Tint", "TintBlue", "TintSlate", "TintCrimson", "TintForest"];

        foreach (string key in tintKeys)
        {
            string enValue = ExtractResxValue(en, key);
            string zhValue = ExtractResxValue(zh, key);

            Assert.DoesNotMatch(kanaRegex, enValue);
            Assert.DoesNotMatch(kanaRegex, zhValue);
        }
    }

    private static string ExtractResxValue(string xml, string key)
    {
        Match match = Regex.Match(
            xml,
            $"<data\\s+name=\\\"{Regex.Escape(key)}\\\"[^>]*>\\s*<value>(.*?)</value>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value : string.Empty;
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



