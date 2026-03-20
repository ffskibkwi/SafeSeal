namespace SafeSeal.Core;

public sealed class TemplateValidationException : Exception
{
    public TemplateValidationException(IReadOnlyList<string> validationErrors)
        : base("Template validation failed.")
    {
        ValidationErrors = validationErrors;
    }

    public IReadOnlyList<string> ValidationErrors { get; }
}
