namespace SafeSeal.App.ViewModels;

public sealed record WatermarkTemplateDefinition(
    string Name,
    string Template,
    IReadOnlyList<WatermarkTemplateFieldDefinition> Fields)
{
    public override string ToString() => Name;
}