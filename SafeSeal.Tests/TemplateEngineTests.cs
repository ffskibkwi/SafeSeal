using SafeSeal.Core;
using Xunit;

namespace SafeSeal.Tests;

public sealed class TemplateEngineTests
{
    [Fact]
    public void Render_WithValidValues_ReplacesVariables()
    {
        TemplateDefinition2 template = new(
            "t1",
            "General",
            "general",
            1,
            "FOR {{purpose}} - {{date}}",
            [
                new TemplateVariableDefinition("purpose", TemplateValueType.String, true, Min: 2, Max: 50),
                new TemplateVariableDefinition("date", TemplateValueType.Date, true),
            ]);

        var engine = new WatermarkTemplateEngine();
        string result = engine.Render(template, new Dictionary<string, string?>
        {
            ["purpose"] = "Review",
            ["date"] = "2026/03/20",
        });

        Assert.Contains("Review", result, StringComparison.Ordinal);
        Assert.Contains("2026/03/20", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithInvalidValues_ThrowsTemplateValidationException()
    {
        TemplateDefinition2 template = new(
            "t2",
            "Visa",
            "visa",
            1,
            "{{passport.no}}",
            [new TemplateVariableDefinition("passport.no", TemplateValueType.String, true, RegexPattern: "^[0-9]{8}$")]);

        var engine = new WatermarkTemplateEngine();

        TemplateValidationException ex = Assert.Throws<TemplateValidationException>(() =>
            engine.Render(template, new Dictionary<string, string?>
            {
                ["passport.no"] = "ABC123",
            }));

        Assert.NotEmpty(ex.ValidationErrors);
    }
}
