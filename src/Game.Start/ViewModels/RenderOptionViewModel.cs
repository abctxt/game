using CommunityToolkit.Mvvm.ComponentModel;

namespace Game.Start.ViewModels;

public sealed partial class RenderOptionViewModel : ViewModelBase
{
    public RenderOptionViewModel(string name, string value, IReadOnlyList<string> choices)
    {
        Name = name;
        Choices = choices;
        _value = value;
    }

    public string Name { get; }

    public IReadOnlyList<string> Choices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _value;

    public string Display => $"{Name}: {Value}";
}
