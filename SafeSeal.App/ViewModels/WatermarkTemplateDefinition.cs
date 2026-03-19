namespace SafeSeal.App.ViewModels;

public sealed record WatermarkTemplateDefinition(
    string TemplateId,
    string Name,
    int Version,
    string Template,
    IReadOnlyList<WatermarkTemplateFieldDefinition> Fields,
    bool IsCustomMultiline = false)
{
    public override string ToString() => Name;
}
