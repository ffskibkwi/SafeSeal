using System.Windows.Media;

namespace SafeSeal.App.ViewModels;

public sealed record WatermarkTintOption(string Key, string Name, Color Color)
{
    public override string ToString() => Name;
}
