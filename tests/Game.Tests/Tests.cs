using System.Runtime.InteropServices;
using Game.Start;
using Game.Start.Models;
using Game.Start.ViewModels;

namespace Game.Tests;

public class RenderOptionViewModelTests
{
    [Test]
    public async Task Display_Includes_Name_And_Value()
    {
        var option = new RenderOptionViewModel("Colour Depth", "32", ["16", "32"]);

        await Assert.That(option.Name).IsEqualTo("Colour Depth");
        await Assert.That(option.Value).IsEqualTo("32");
        await Assert.That(option.Display).IsEqualTo("Colour Depth: 32");
        await Assert.That(option.Choices.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Changing_Value_Updates_Display()
    {
        var option = new RenderOptionViewModel("VSync", "No", ["Yes", "No"]);

        option.Value = "Yes";

        await Assert.That(option.Display).IsEqualTo("VSync: Yes");
    }
}


public class RenderingSubsystemTests
{
    [Test]
    public async Task Stores_Name_And_Options()
    {
        var option = new RenderOptionViewModel("Full Screen", "Yes", ["Yes", "No"]);
        var subsystem = new RenderingSubsystem("Metal Rendering Subsystem", [option]);

        await Assert.That(subsystem.Name).IsEqualTo("Metal Rendering Subsystem");
        await Assert.That(subsystem.Options.Count).IsEqualTo(1);
        await Assert.That(subsystem.Options[0]).IsEqualTo(option);
    }
}


public class MainWindowViewModelTests
{
    [Test]
    public async Task Defaults_To_OpenGL_And_Full_Screen()
    {
        var vm = new MainWindowViewModel();

        await Assert.That(vm.Subsystems.Count).IsEqualTo(4);
        await Assert.That(vm.SelectedSubsystem.Name).IsEqualTo("OpenGL Rendering Subsystem");
        await Assert.That(vm.SelectedOption?.Name).IsEqualTo("Full Screen");
        await Assert.That(vm.Options).IsSameReferenceAs(vm.SelectedSubsystem.Options);
    }

    [Test]
    public async Task Changing_Subsystem_Selects_First_Option()
    {
        var vm = new MainWindowViewModel();

        vm.SelectedSubsystem = vm.Subsystems[2];

        await Assert.That(vm.SelectedSubsystem.Name).IsEqualTo("Metal Rendering Subsystem");
        await Assert.That(vm.SelectedOption?.Name).IsEqualTo("Colour Depth");
    }

    [Test]
    public async Task Accept_Requests_Close_With_True()
    {
        var vm = new MainWindowViewModel();
        bool? accepted = null;
        vm.CloseRequested += (_, value) => accepted = value;

        vm.AcceptCommand.Execute(null);

        await Assert.That(accepted).IsTrue();
    }

    [Test]
    public async Task Cancel_Requests_Close_With_False()
    {
        var vm = new MainWindowViewModel();
        bool? accepted = null;
        vm.CloseRequested += (_, value) => accepted = value;

        vm.CancelCommand.Execute(null);

        await Assert.That(accepted).IsFalse();
    }

    [Test]
    public async Task BuildArguments_Uses_Dialog_Defaults()
    {
        var vm = new MainWindowViewModel();

        await Assert.That(GameLauncher.BuildArguments(vm)).IsEqualTo("--width 1024 --height 768 --fullscreen --vsync 0");
    }

    [Test]
    public async Task BuildArguments_Applies_Video_Mode_Windowed_And_VSync()
    {
        var vm = new MainWindowViewModel();
        SetOption(vm, "Video Mode", "1280 x 720");
        SetOption(vm, "Full Screen", "No");
        SetOption(vm, "VSync", "Yes");

        await Assert.That(GameLauncher.BuildArguments(vm)).IsEqualTo("--width 1280 --height 720 --windowed --vsync 1");
    }

    private static void SetOption(MainWindowViewModel viewModel, string name, string value)
    {
        var option = viewModel.SelectedSubsystem.Options.First(item => item.Name == name);
        option.Value = value;
    }
}


public class GameLauncherTests
{
    [Test]
    public async Task FindGamePath_Prefers_Sibling_Executable()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Game.Start", "bin", "Debug", "net10.0");
        var sibling = Path.GetFullPath(Path.Combine(baseDirectory, GameLauncher.GameFileName));
        var fallback = RidHostPath(baseDirectory, "Debug");

        var path = GameLauncher.FindGamePath(baseDirectory, file => file == sibling || file == fallback);

        await Assert.That(path).IsEqualTo(sibling);
    }

    [Test]
    public async Task FindGamePath_Uses_Debug_Output_When_Sibling_Is_Missing()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Game.Start", "bin", "Debug", "net10.0");
        var fallback = FallbackPath(baseDirectory, "Debug");

        var path = GameLauncher.FindGamePath(baseDirectory, file => file == fallback);

        await Assert.That(path).IsEqualTo(fallback);
    }

    [Test]
    public async Task FindGamePath_Uses_Rid_Output()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Game.Start", "bin", "Debug", "net10.0");
        var fallback = RidHostPath(baseDirectory, "Debug");

        var path = GameLauncher.FindGamePath(baseDirectory, file => file == fallback);

        await Assert.That(path).IsEqualTo(fallback);
    }

    [Test]
    public async Task FindGamePath_From_Start_Rid_Folder_Finds_Game_Host()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");
        var rid = RuntimeInformation.RuntimeIdentifier;
        var baseDirectory = Path.Combine(root, "src", "Game.Start", "bin", "Debug", "net10.0", rid);
        var expected = Path.GetFullPath(Path.Combine(root, "src", "Game", "bin", "Debug", "net10.0", rid, GameLauncher.GameFileName));

        var path = GameLauncher.FindGamePath(baseDirectory, file => file == expected);

        await Assert.That(path).IsEqualTo(expected);
    }

    [Test]
    public async Task FindGamePath_Uses_Release_Output_When_Debug_Is_Missing()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Game.Start", "bin", "Debug", "net10.0");
        var fallback = RidHostPath(baseDirectory, "Release");

        var path = GameLauncher.FindGamePath(baseDirectory, file => file == fallback);

        await Assert.That(path).IsEqualTo(fallback);
    }

    [Test]
    public async Task FindGamePath_Returns_Null_When_Missing()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Game.Start", "bin", "Debug", "net10.0");

        var path = GameLauncher.FindGamePath(baseDirectory, _ => false);

        await Assert.That(path).IsNull();
    }

    private static string FallbackPath(string baseDirectory, string configuration)
        => Path.Combine(FallbackDirectory(baseDirectory, configuration), GameLauncher.GameFileName);

    private static string RidHostPath(string baseDirectory, string configuration)
        => Path.Combine(
            FallbackDirectory(baseDirectory, configuration),
            RuntimeInformation.RuntimeIdentifier,
            GameLauncher.GameFileName);

    private static string FallbackDirectory(string baseDirectory, string configuration)
        => Path.GetFullPath(Path.Combine(
            baseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Game",
            "bin",
            configuration,
            "net10.0"));
}
