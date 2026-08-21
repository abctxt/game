namespace Game;

internal readonly record struct LaunchSettings(int Width, int Height, bool Fullscreen, bool VSync)
{
    public static LaunchSettings Default { get; } = new(1024, 768, Fullscreen: false, VSync: true);

    public static LaunchSettings Parse(string[] args)
    {
        var settings = Default;
        for (var i = 0; i < args.Length; i++) {
            switch (args[i]) {
            case "--width" when i + 1 < args.Length && int.TryParse(args[i + 1], out var width) && width > 0:
                settings = settings with { Width = width };
                i++;
                break;
            case "--height" when i + 1 < args.Length && int.TryParse(args[i + 1], out var height) && height > 0:
                settings = settings with { Height = height };
                i++;
                break;
            case "--fullscreen":
                settings = settings with { Fullscreen = true };
                break;
            case "--windowed":
                settings = settings with { Fullscreen = false };
                break;
            case "--vsync" when i + 1 < args.Length:
                settings = settings with { VSync = args[++i] != "0" };
                break;
            }
        }

        return settings;
    }
}
