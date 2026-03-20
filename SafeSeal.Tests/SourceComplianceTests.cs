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

    [Fact]
    public void TemplateFieldSyncPath_RaisesWorkspaceChangedOnPerKeystrokeValueUpdates()
    {
        string root = FindRepoRoot();
        string vm = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "ViewModels", "MainViewModel.cs"));
        string workspace = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Services", "WorkspaceStateService.cs"));
        string handler = ExtractMethodBlock(workspace, "private void OnTemplateFieldChanged");

        Assert.Contains("public void UpdateTemplateFieldValue(string key, string? value, bool notify = true)", workspace, StringComparison.Ordinal);
        Assert.Contains("TemplateFieldValues = SnapshotTemplateFieldValuesLocked()", handler, StringComparison.Ordinal);
        Assert.Contains("OnWorkspaceChanged();", handler, StringComparison.Ordinal);
        Assert.Contains("_workspaceState.UpdateTemplateFieldValue(field.Key, field.Value, notify: true);", vm, StringComparison.Ordinal);
        Assert.Contains("_ = RefreshPreviewAsync(PreviewDocument);", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void PresetTemplateRender_UsesWorkspaceSnapshotAsSingleAuthoritativeSource()
    {
        string root = FindRepoRoot();
        string vm = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "ViewModels", "MainViewModel.cs"));
        string method = ExtractMethodBlock(vm, "private List<string> BuildTemplateLines");

        Assert.Contains("foreach ((string key, string value) in workspace.TemplateFieldValues)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_workspaceState.CurrentTemplateFields", method, StringComparison.Ordinal);
    }

    [Fact]
    public void WatermarkPane_AndScrollViewerTemplate_UseDedicatedScrollbarLayout()
    {
        string root = FindRepoRoot();
        string mainWindow = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "MainWindow.xaml"));
        string appXaml = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "App.xaml"));

        int batchDeleteIndex = mainWindow.IndexOf("Command=\"{Binding BatchDeleteCommand}\"", StringComparison.Ordinal);
        int batchExportIndex = mainWindow.IndexOf("Command=\"{Binding BatchExportCommand}\"", StringComparison.Ordinal);

        Assert.True(batchDeleteIndex >= 0 && batchExportIndex > batchDeleteIndex);
        Assert.DoesNotContain("Grid.ColumnSpan=\"2\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\" MinWidth=\"22\" />", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<Button Grid.Column=\"2\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\" MinWidth=\"22\" />", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"22\" />", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", appXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{TemplateBinding VerticalOffset}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{TemplateBinding HorizontalOffset}\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogContracts_AreThemeSafe_AndKeepCompactSizing()
    {
        string root = FindRepoRoot();
        string pinDialog = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Dialogs", "PinCodeDialog.xaml"));
        string nicknameDialog = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Dialogs", "NicknameDialog.xaml"));
        string confirmDialog = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Dialogs", "FluentConfirmDialog.xaml"));
        string messageDialog = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Dialogs", "FluentMessageDialog.xaml"));

        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource PrimaryTextBrush}\" />", pinDialog, StringComparison.Ordinal);
        Assert.Contains("SizeToContent=\"WidthAndHeight\"", nicknameDialog, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"450\"", nicknameDialog, StringComparison.Ordinal);
        Assert.Contains("AllowsTransparency=\"True\"", confirmDialog, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", confirmDialog, StringComparison.Ordinal);
        Assert.Contains("AllowsTransparency=\"True\"", messageDialog, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", messageDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchDelete_Resources_ExistInAllLanguages()
    {
        string root = FindRepoRoot();
        string en = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Resources", "Strings.resx"));
        string zh = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Resources", "Strings.zh-CN.resx"));
        string ja = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Resources", "Strings.ja-JP.resx"));

        string[] keys =
        [
            "BatchDelete",
            "BatchDeleteTitle",
            "BatchDeletePromptFormat",
            "StatusBatchDeletePreparing",
            "StatusBatchDeleteCompletedFormat",
            "StatusBatchDeleteCompletedWithErrorsFormat",
        ];

        foreach (string key in keys)
        {
            Assert.Contains($"name=\"{key}\"", en, StringComparison.Ordinal);
            Assert.Contains($"name=\"{key}\"", zh, StringComparison.Ordinal);
            Assert.Contains($"name=\"{key}\"", ja, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VersionContract_UsesCentralProjectVersionSource()
    {
        string root = FindRepoRoot();
        string props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        string provider = File.ReadAllText(Path.Combine(root, "SafeSeal.App", "Services", "VersionInfoProvider.cs"));

        Assert.Contains("<Version>", props, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>", props, StringComparison.Ordinal);
        Assert.Contains("AssemblyInformationalVersionAttribute", provider, StringComparison.Ordinal);
    }

    private static string ExtractResxValue(string xml, string key)
    {
        Match match = Regex.Match(
            xml,
            $"<data\\s+name=\\\"{Regex.Escape(key)}\\\"[^>]*>\\s*<value>(.*?)</value>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ExtractMethodBlock(string source, string signaturePrefix)
    {
        int signatureIndex = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Method signature not found: {signaturePrefix}");

        int openBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openBrace >= 0, $"Opening brace not found: {signaturePrefix}");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(signatureIndex, i - signatureIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Method block not closed: {signaturePrefix}");
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





