using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Game.Start.ViewModels;

namespace Game.Start;

public static class GameLauncher
{
    public static string GameFileName => OperatingSystem.IsWindows() ? "game.exe" : "game";

    public static string? FindGamePath(string baseDirectory, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        foreach (var candidate in Candidates(baseDirectory)) {
            if (fileExists(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> BuildArgumentList(MainWindowViewModel viewModel)
    {
        var width = 1024;
        var height = 768;
        var fullscreen = true;
        var vsync = false;

        foreach (var option in viewModel.SelectedSubsystem.Options) {
            switch (option.Name) {
            case "Video Mode":
                ParseVideoMode(option.Value, ref width, ref height);
                break;
            case "Full Screen":
                fullscreen = option.Value == "Yes";
                break;
            case "VSync":
                vsync = option.Value == "Yes";
                break;
            }
        }

        return [
            "--width",
            width.ToString(),
            "--height",
            height.ToString(),
            fullscreen ? "--fullscreen" : "--windowed",
            "--vsync",
            vsync ? "1" : "0",
        ];
    }

    public static string BuildArguments(MainWindowViewModel viewModel)
        => string.Join(' ', BuildArgumentList(viewModel));

    public static bool TryStart(MainWindowViewModel viewModel, out string? error, out int processId)
    {
        processId = 0;
        var path = FindGamePath(AppContext.BaseDirectory);
        if (path is null) {
            error = $"Could not find {GameFileName} next to start or under src/Game/bin.{Environment.NewLine}Looked from {AppContext.BaseDirectory}";
            return false;
        }

        var workingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
        var arguments = BuildArgumentList(viewModel);

        try {
            return OperatingSystem.IsWindows()
                ? StartWindows(path, arguments, workingDirectory, out error, out processId)
                : StartUnixDetached(path, arguments, workingDirectory, out error, out processId);
        }
        catch (Exception ex) {
            error = $"Failed to start game: {ex.Message}";
            return false;
        }
    }

    public static void BringToForeground(int processId)
    {
        if (processId <= 0) {
            return;
        }

        if (OperatingSystem.IsMacOS()) {
            BringToForegroundMac(processId);
        }
    }

    private static bool StartWindows(
        string path,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        out string? error,
        out int processId)
    {
        var startInfo = new ProcessStartInfo {
            FileName = path,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo);
        processId = process?.Id ?? 0;
        return ConfirmRunning(process, path, out error);
    }

    private static bool StartUnixDetached(
        string path,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        out string? error,
        out int processId)
    {
        processId = 0;
        var logPath = Path.Combine(Path.GetTempPath(), "game-launch.log");
        var startInfo = new ProcessStartInfo {
            FileName = "/bin/sh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("cd \"$1\" || exit 1; game=\"$2\"; log=\"$3\"; shift 3; nohup \"$game\" \"$@\" >\"$log\" 2>&1 & echo $!");
        startInfo.ArgumentList.Add("_");
        startInfo.ArgumentList.Add(workingDirectory);
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add(logPath);
        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        using var shell = Process.Start(startInfo);
        if (shell is null) {
            error = $"Failed to start {path}";
            return false;
        }

        var pidText = shell.StandardOutput.ReadLine();
        shell.WaitForExit(2000);
        if (!int.TryParse(pidText, out processId)) {
            error = ReadLaunchLog(logPath, $"Failed to start {path}");
            return false;
        }

        Thread.Sleep(400);
        try {
            var process = Process.GetProcessById(processId);
            if (!process.HasExited) {
                error = null;
                return true;
            }
        }
        catch (ArgumentException) {
        }

        error = ReadLaunchLog(logPath, $"Game exited immediately ({path})");
        return false;
    }

    private static bool ConfirmRunning(Process? process, string path, out string? error)
    {
        if (process is null) {
            error = $"Failed to start {path}";
            return false;
        }

        Thread.Sleep(400);
        if (!process.HasExited) {
            error = null;
            return true;
        }

        error = $"Game exited immediately ({path})";
        return false;
    }

    private static string ReadLaunchLog(string logPath, string fallback)
    {
        try {
            if (File.Exists(logPath)) {
                var text = File.ReadAllText(logPath).Trim();
                if (text.Length > 0) {
                    return text;
                }
            }
        }
        catch (IOException) {
        }

        return fallback;
    }

    [SupportedOSPlatform("macos")]
    private static void BringToForegroundMac(int processId)
    {
        var nsRunningApplication = objc_getClass("NSRunningApplication");
        if (nsRunningApplication != 0) {
            var app = objc_msgSend_int(
                nsRunningApplication,
                sel_registerName("runningApplicationWithProcessIdentifier:"),
                processId);
            if (app != 0 && RespondsTo(app, "activateWithOptions:")) {
                // NSApplicationActivateAllWindows | NSApplicationActivateIgnoringOtherApps
                objc_msgSend_nuint(app, sel_registerName("activateWithOptions:"), 3);
                return;
            }
        }

        try {
            Process.Start(new ProcessStartInfo {
                FileName = "/usr/bin/osascript",
                ArgumentList = {
                    "-e",
                    $"tell application \"System Events\" to set frontmost of (first process whose unix id is {processId}) to true",
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })?.WaitForExit(1000);
        }
        catch (Exception) {
        }
    }

    [SupportedOSPlatform("macos")]
    private static bool RespondsTo(nint target, string selectorName)
        => objc_msgSend_nint(target, sel_registerName("respondsToSelector:"), sel_registerName(selectorName)) != 0;

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [SupportedOSPlatform("macos")]
    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern nint objc_getClass(string name);

    [SupportedOSPlatform("macos")]
    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(string name);

    [SupportedOSPlatform("macos")]
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_int(nint receiver, nint selector, int arg1);

    [SupportedOSPlatform("macos")]
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nint(nint receiver, nint selector, nint arg1);

    [SupportedOSPlatform("macos")]
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nuint(nint receiver, nint selector, nuint arg1);

    private static IEnumerable<string> Candidates(string baseDirectory)
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var tfm = FindTfm(baseDirectory);

        foreach (var ancestor in SelfAndAncestors(baseDirectory)) {
            yield return Path.GetFullPath(Path.Combine(ancestor, GameFileName));
            yield return Path.GetFullPath(Path.Combine(ancestor, rid, GameFileName));

            foreach (var root in new[] { ancestor, Path.Combine(ancestor, "src") }) {
                foreach (var configuration in new[] { "Debug", "Release" }) {
                    var tfmDirectory = Path.GetFullPath(Path.Combine(root, "Game", "bin", configuration, tfm));
                    yield return Path.Combine(tfmDirectory, GameFileName);
                    yield return Path.Combine(tfmDirectory, rid, GameFileName);

                    if (!Directory.Exists(tfmDirectory)) {
                        continue;
                    }

                    foreach (var directory in Directory.EnumerateDirectories(tfmDirectory)) {
                        yield return Path.Combine(directory, GameFileName);
                    }
                }
            }
        }
    }

    private static string FindTfm(string path)
    {
        foreach (var directory in SelfAndAncestors(path)) {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            if (name.StartsWith("net", StringComparison.Ordinal) && name.Length > 3 && char.IsDigit(name[3])) {
                return name;
            }
        }

        return "net10.0";
    }

    private static IEnumerable<string> SelfAndAncestors(string path)
    {
        var directory = Path.GetFullPath(path);
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(directory); i++) {
            yield return directory;
            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent) || parent == directory) {
                yield break;
            }

            directory = parent;
        }
    }

    private static void ParseVideoMode(string value, ref int width, ref int height)
    {
        var parts = value.Split('x', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], out var parsedWidth)
            && int.TryParse(parts[1], out var parsedHeight)
            && parsedWidth > 0
            && parsedHeight > 0) {
            width = parsedWidth;
            height = parsedHeight;
        }
    }
}
