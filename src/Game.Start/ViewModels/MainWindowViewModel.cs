using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Game.Start.Models;

namespace Game.Start.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        Subsystems = [
            CreateSubsystem("OpenGL Rendering Subsystem"),
            CreateSubsystem("Vulkan Rendering Subsystem"),
            CreateSubsystem("Metal Rendering Subsystem"),
            CreateSubsystem("Direct3D 12 Rendering Subsystem"),
        ];

        _selectedSubsystem = Subsystems[0];
        _selectedOption = _selectedSubsystem.Options[3];
    }

    public IReadOnlyList<RenderingSubsystem> Subsystems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Options))]
    private RenderingSubsystem _selectedSubsystem;

    [ObservableProperty]
    private RenderOptionViewModel? _selectedOption;

    [ObservableProperty]
    private string? _errorMessage;

    public IReadOnlyList<RenderOptionViewModel> Options => SelectedSubsystem.Options;

    public event EventHandler<bool>? CloseRequested;

    partial void OnSelectedSubsystemChanged(RenderingSubsystem value)
    {
        SelectedOption = value.Options.Count > 0 ? value.Options[0] : null;
    }

    [RelayCommand]
    private void Accept() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private static RenderingSubsystem CreateSubsystem(string name)
        => new(name,
        [
            new RenderOptionViewModel("Colour Depth", "32", ["16", "32"]),
            new RenderOptionViewModel("Display Frequency", "60", ["50", "60", "75", "120", "144"]),
            new RenderOptionViewModel("FSAA", "0", ["0", "2", "4", "8"]),
            new RenderOptionViewModel("Full Screen", "Yes", ["Yes", "No"]),
            new RenderOptionViewModel("RTT Preferred Mode", "FBO", ["FBO", "PBuffer", "Copy"]),
            new RenderOptionViewModel("VSync", "No", ["Yes", "No"]),
            new RenderOptionViewModel("VSync Interval", "1", ["1", "2", "3", "4"]),
            new RenderOptionViewModel("Video Mode", "1024 x 768",
            [
                "800 x 600",
                "1024 x 768",
                "1280 x 720",
                "1920 x 1080",
                "2560 x 1440",
            ]),
        ]);
}
