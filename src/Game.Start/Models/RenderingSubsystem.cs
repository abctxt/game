using Game.Start.ViewModels;

namespace Game.Start.Models;

public sealed class RenderingSubsystem
{
    public RenderingSubsystem(string name, IReadOnlyList<RenderOptionViewModel> options)
    {
        Name = name;
        Options = options;
    }

    public string Name { get; }

    public IReadOnlyList<RenderOptionViewModel> Options { get; }
}
