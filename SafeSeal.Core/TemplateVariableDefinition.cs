namespace SafeSeal.Core;

public sealed record TemplateVariableDefinition(
    string Key,
    TemplateValueType ValueType,
    bool Required,
    string? RegexPattern = null,
    double? Min = null,
    double? Max = null,
    IReadOnlyList<string>? EnumValues = null,
    string? DefaultValue = null);
