using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Game.Start.ViewModels;
using Game.Start.Views;

namespace Game.Start;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var viewModel = new MainWindowViewModel();
            var window = new MainWindow {
                DataContext = viewModel,
            };
            viewModel.CloseRequested += (_, accepted) => {
                if (accepted) {
                    if (!GameLauncher.TryStart(viewModel, out var error, out var processId)) {
                        viewModel.ErrorMessage = error;
                        return;
                    }

                    window.Closed += (_, _) => GameLauncher.BringToForeground(processId);
                    window.Hide();
                    GameLauncher.BringToForeground(processId);
                }

                window.Close();
            };
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
