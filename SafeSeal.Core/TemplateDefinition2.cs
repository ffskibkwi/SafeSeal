namespace SafeSeal.Core;

public sealed record TemplateDefinition2(
    string Id,
    string Name,
    string Scenario,
    int Version,
    string Content,
    IReadOnlyList<TemplateVariableDefinition> Variables);
