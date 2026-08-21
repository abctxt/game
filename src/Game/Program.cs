using ImGuiNET;
using SDL3;


namespace Game;

internal static class Program
{
    private const float CursorSize = 50f;
    private const float FontSize = 80f;
    private const float LegendFontSize = 22f;
    private const float LegendColumnGap = 28f;
    private const float HudMargin = 12f;

    private static readonly (string Key, string Action)[] LegendEntries = [
        ("Esc/Q", "Quit"),
        ("M", "Mute"),
    ];

    private static readonly SDL.Color TextColor = new() { R = 255, G = 255, B = 255, A = 255 };

    private static void Main(string[] args)
    {
        var settings = LaunchSettings.Parse(args);

        SDL.SetHint(SDL.Hints.ForceRaiseWindow, "1");
        SDL.SetHint(SDL.Hints.WindowActivateWhenShown, "1");
        SDL.SetHint(SDL.Hints.WindowActivateWhenRaised, "1");

        if (!SDL.Init(SDL.InitFlags.Video)) {
            Console.Error.WriteLine($"SDL init failed: {SDL.GetError()}");
            return;
        }

        if (!TTF.Init()) {
            Console.Error.WriteLine($"SDL_ttf init failed: {SDL.GetError()}");
            SDL.Quit();
            return;
        }

        var mixerReady = Mixer.Init();
        if (!mixerReady) {
            Console.Error.WriteLine($"SDL_mixer init failed: {SDL.GetError()}");
        }

        var windowFlags = SDL.WindowFlags.Resizable;
        if (settings.Fullscreen) {
            windowFlags |= SDL.WindowFlags.Fullscreen;
        }

        if (!SDL.CreateWindowAndRenderer(
                "Game - SDL3 demo",
                settings.Width,
                settings.Height,
                windowFlags,
                out var window,
                out var renderer)) {
            Console.Error.WriteLine($"Create window/renderer failed: {SDL.GetError()}");
            if (mixerReady) {
                Mixer.Quit();
            }

            TTF.Quit();
            SDL.Quit();
            return;
        }

        SDL.RaiseWindow(window);
        SDL.SetRenderVSync(renderer, settings.VSync ? 1 : 0);
        SDL.HideCursor();

        var cursor = Image.LoadTexture(renderer, AssetPath("cursor.png"));
        if (cursor == IntPtr.Zero) {
            Console.Error.WriteLine($"Load cursor failed: {SDL.GetError()}");
            SDL.DestroyRenderer(renderer);
            SDL.DestroyWindow(window);
            if (mixerReady) {
                Mixer.Quit();
            }

            TTF.Quit();
            SDL.Quit();
            return;
        }

        SDL.SetTextureBlendMode(cursor, SDL.BlendMode.Blend);
        SDL.SetTextureScaleMode(cursor, SDL.ScaleMode.Linear);
        SDL.GetTextureSize(cursor, out var cursorWidth, out var cursorHeight);

        var map = Image.LoadTexture(renderer, AssetPath("map.png"));
        if (map == IntPtr.Zero) {
            Console.Error.WriteLine($"Load map failed: {SDL.GetError()}");
            SDL.DestroyTexture(cursor);
            SDL.DestroyRenderer(renderer);
            SDL.DestroyWindow(window);
            if (mixerReady) {
                Mixer.Quit();
            }

            TTF.Quit();
            SDL.Quit();
            return;
        }

        SDL.SetTextureScaleMode(map, SDL.ScaleMode.Linear);
        SDL.GetTextureSize(map, out var mapWidth, out var mapHeight);

        var font = TTF.OpenFont(AssetPath("miserable.ttf"), FontSize);
        if (font == IntPtr.Zero) {
            Console.Error.WriteLine($"Open font failed: {SDL.GetError()}");
            SDL.DestroyTexture(map);
            SDL.DestroyTexture(cursor);
            SDL.DestroyRenderer(renderer);
            SDL.DestroyWindow(window);
            if (mixerReady) {
                Mixer.Quit();
            }

            TTF.Quit();
            SDL.Quit();
            return;
        }

        var legendFont = TTF.OpenFont(AssetPath("miserable.ttf"), LegendFontSize);
        if (legendFont == IntPtr.Zero) {
            Console.Error.WriteLine($"Open legend font failed: {SDL.GetError()}");
            TTF.CloseFont(font);
            SDL.DestroyTexture(map);
            SDL.DestroyTexture(cursor);
            SDL.DestroyRenderer(renderer);
            SDL.DestroyWindow(window);
            if (mixerReady) {
                Mixer.Quit();
            }

            TTF.Quit();
            SDL.Quit();
            return;
        }

        var mixer = IntPtr.Zero;
        var ambient = IntPtr.Zero;
        var ambientTrack = IntPtr.Zero;
        if (mixerReady) {
            TryStartAmbient(out mixer, out ambient, out ambientTrack);
        }

        var imguiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(imguiContext);
        unsafe {
            ImGui.GetIO().NativePtr->IniFilename = null;
        }

        var imguiIo = ImGui.GetIO();
        imguiIo.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        imguiIo.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;

        var imguiPlatform = new ImGuiSdl3Platform(window);
        var imguiRenderer = new ImGuiSdl3Renderer(renderer);
        var showDemo = false;

        var lastTicks = SDL.GetTicks();
        var fpsUpdated = lastTicks;
        var fpsTexture = IntPtr.Zero;
        var fpsDst = new SDL.FRect();
        var legend = CreateLegend(renderer, legendFont);
        var running = true;

        while (running) {
            var io = ImGui.GetIO();
            if (io.WantTextInput && !SDL.TextInputActive(window)) {
                SDL.StartTextInput(window);
            } else if (!io.WantTextInput && SDL.TextInputActive(window)) {
                SDL.StopTextInput(window);
            }

            while (SDL.PollEvent(out var ev)) {
                imguiPlatform.ProcessEvent(ev);

                switch ((SDL.EventType)ev.Type) {
                case SDL.EventType.Quit:
                    running = false;
                    break;

                case SDL.EventType.KeyDown when !ev.Key.Repeat && !io.WantCaptureKeyboard:
                    switch (ev.Key.Key) {
                    case SDL.Keycode.Escape:
                    case SDL.Keycode.Q:
                        running = false;
                        break;

                    case SDL.Keycode.M:
                        ToggleAmbientMute(ambientTrack);
                        break;
                    }

                    break;
                }
            }

            var now = SDL.GetTicks();
            var dt = Math.Clamp((now - lastTicks) / 1000f, 0f, 0.05f);
            lastTicks = now;
            io.DeltaTime = dt > 0f ? dt : 1f / 60f;

            SDL.GetRenderOutputSize(renderer, out var outputWidth, out var outputHeight);

            if (now - fpsUpdated >= 500 || fpsTexture == IntPtr.Zero) {
                var fps = dt > 0f ? (int)Math.Round(1f / dt) : 0;
                UpdateTextTexture(renderer, font, $"{fps} FPS", wrapWidth: 0, ref fpsTexture, ref fpsDst);
                fpsUpdated = now;
            }

            fpsDst.X = outputWidth - fpsDst.W - HudMargin;
            fpsDst.Y = 8f;

            SDL.SetRenderDrawColor(renderer, 0, 0, 0, 255);
            SDL.RenderClear(renderer);

            var mapDst = FitRect(outputWidth, outputHeight, mapWidth, mapHeight);
            SDL.RenderTexture(renderer, map, IntPtr.Zero, in mapDst);

            if (fpsTexture != IntPtr.Zero) {
                SDL.RenderTexture(renderer, fpsTexture, IntPtr.Zero, in fpsDst);
            }

            DrawLegend(renderer, legend, outputWidth, outputHeight);

            imguiPlatform.NewFrame();
            imguiRenderer.NewFrame();
            ImGui.NewFrame();

            if (ImGui.Begin("Debug")) {
                if (ambientTrack != IntPtr.Zero) {
                    var muted = Mixer.TrackPaused(ambientTrack);
                    if (ImGui.Checkbox("Mute ambient", ref muted)) {
                        if (muted) {
                            Mixer.PauseTrack(ambientTrack);
                        } else {
                            Mixer.ResumeTrack(ambientTrack);
                        }
                    }
                }

                ImGui.Checkbox("Show ImGui demo", ref showDemo);
            }

            ImGui.End();

            if (showDemo) {
                ImGui.ShowDemoWindow(ref showDemo);
            }

            ImGui.Render();
            imguiRenderer.RenderDrawData(ImGui.GetDrawData());

            SDL.GetMouseState(out var mouseX, out var mouseY);
            var cursorScale = CursorSize / Math.Max(cursorWidth, cursorHeight);
            var cursorDst = new SDL.FRect {
                X = mouseX,
                Y = mouseY,
                W = cursorWidth * cursorScale,
                H = cursorHeight * cursorScale,
            };
            SDL.RenderTexture(renderer, cursor, IntPtr.Zero, in cursorDst);

            SDL.RenderPresent(renderer);
        }

        imguiRenderer.Dispose();
        imguiPlatform.Dispose();
        ImGui.DestroyContext(imguiContext);

        if (fpsTexture != IntPtr.Zero) {
            SDL.DestroyTexture(fpsTexture);
        }

        DestroyLegend(legend);

        TTF.CloseFont(legendFont);
        TTF.CloseFont(font);
        SDL.DestroyTexture(map);
        SDL.DestroyTexture(cursor);
        SDL.DestroyRenderer(renderer);
        SDL.DestroyWindow(window);

        if (mixer != IntPtr.Zero) {
            Mixer.DestroyMixer(mixer);
        }

        if (ambient != IntPtr.Zero) {
            Mixer.DestroyAudio(ambient);
        }

        if (mixerReady) {
            Mixer.Quit();
        }

        TTF.Quit();
        SDL.Quit();
    }

    private static string AssetPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Assets", fileName);

    private static SDL.FRect FitRect(float viewportWidth, float viewportHeight, float contentWidth, float contentHeight)
    {
        var scale = Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight);
        var width = contentWidth * scale;
        var height = contentHeight * scale;
        return new SDL.FRect {
            X = (viewportWidth - width) * 0.5f,
            Y = (viewportHeight - height) * 0.5f,
            W = width,
            H = height,
        };
    }

    private static void TryStartAmbient(out nint mixer, out nint audio, out nint track)
    {
        track = IntPtr.Zero;
        mixer = Mixer.CreateMixerDevice(SDL.AudioDeviceDefaultPlayback, IntPtr.Zero);
        if (mixer == IntPtr.Zero) {
            Console.Error.WriteLine($"Create mixer failed: {SDL.GetError()}");
            audio = IntPtr.Zero;
            return;
        }

        audio = Mixer.LoadAudio(mixer, AssetPath("ambient.wav"), predecode: true);
        if (audio == IntPtr.Zero) {
            Console.Error.WriteLine($"Load ambient.wav failed: {SDL.GetError()}");
            Mixer.DestroyMixer(mixer);
            mixer = IntPtr.Zero;
            return;
        }

        track = Mixer.CreateTrack(mixer);
        if (track == IntPtr.Zero || !Mixer.SetTrackAudio(track, audio)) {
            Console.Error.WriteLine($"Create ambient track failed: {SDL.GetError()}");
            Mixer.DestroyMixer(mixer);
            Mixer.DestroyAudio(audio);
            mixer = IntPtr.Zero;
            audio = IntPtr.Zero;
            track = IntPtr.Zero;
            return;
        }

        var options = SDL.CreateProperties();
        SDL.SetNumberProperty(options, Mixer.Props.PlayLoopsNumber, -1);
        if (!Mixer.PlayTrack(track, options)) {
            Console.Error.WriteLine($"Play ambient.wav failed: {SDL.GetError()}");
        }

        SDL.DestroyProperties(options);
    }

    private static void ToggleAmbientMute(nint track)
    {
        if (track == IntPtr.Zero) {
            return;
        }

        if (Mixer.TrackPaused(track)) {
            Mixer.ResumeTrack(track);
        } else {
            Mixer.PauseTrack(track);
        }
    }

    private static void UpdateTextTexture(
        nint renderer,
        nint font,
        string text,
        int wrapWidth,
        ref nint texture,
        ref SDL.FRect destination)
    {
        if (texture != IntPtr.Zero) {
            SDL.DestroyTexture(texture);
            texture = IntPtr.Zero;
        }

        var surface = TTF.RenderTextBlendedWrapped(font, text, UIntPtr.Zero, TextColor, wrapWidth);
        if (surface == IntPtr.Zero) {
            Console.Error.WriteLine($"Render text failed: {SDL.GetError()}");
            return;
        }

        texture = SDL.CreateTextureFromSurface(renderer, surface);
        SDL.DestroySurface(surface);

        if (texture == IntPtr.Zero) {
            Console.Error.WriteLine($"Create text texture failed: {SDL.GetError()}");
            return;
        }

        SDL.GetTextureSize(texture, out destination.W, out destination.H);
    }

    private readonly record struct TextSprite(nint Texture, float Width, float Height);

    private readonly record struct LegendRow(TextSprite Key, TextSprite Action);

    private static LegendRow[] CreateLegend(nint renderer, nint font)
    {
        var rows = new LegendRow[LegendEntries.Length];
        for (var i = 0; i < LegendEntries.Length; i++) {
            var (key, action) = LegendEntries[i];
            rows[i] = new LegendRow(
                CreateTextSprite(renderer, font, key),
                CreateTextSprite(renderer, font, action));
        }

        return rows;
    }

    private static TextSprite CreateTextSprite(nint renderer, nint font, string text)
    {
        var texture = IntPtr.Zero;
        var destination = new SDL.FRect();
        UpdateTextTexture(renderer, font, text, wrapWidth: 0, ref texture, ref destination);
        return new TextSprite(texture, destination.W, destination.H);
    }

    private static void DrawLegend(nint renderer, LegendRow[] rows, int outputWidth, int outputHeight)
    {
        if (rows.Length == 0) {
            return;
        }

        var maxKeyWidth = 0f;
        var maxActionWidth = 0f;
        var lineHeight = 0f;
        foreach (var row in rows) {
            maxKeyWidth = Math.Max(maxKeyWidth, row.Key.Width);
            maxActionWidth = Math.Max(maxActionWidth, row.Action.Width);
            lineHeight = Math.Max(lineHeight, Math.Max(row.Key.Height, row.Action.Height));
        }

        var blockWidth = maxKeyWidth + LegendColumnGap + maxActionWidth;
        var blockHeight = lineHeight * rows.Length;
        var originX = outputWidth - blockWidth - HudMargin;
        var originY = outputHeight - blockHeight - HudMargin;

        for (var i = 0; i < rows.Length; i++) {
            var y = originY + i * lineHeight;
            var key = rows[i].Key;
            if (key.Texture != IntPtr.Zero) {
                var keyDst = new SDL.FRect { X = originX, Y = y, W = key.Width, H = key.Height };
                SDL.RenderTexture(renderer, key.Texture, IntPtr.Zero, in keyDst);
            }

            var action = rows[i].Action;
            if (action.Texture != IntPtr.Zero) {
                var actionDst = new SDL.FRect {
                    X = originX + blockWidth - action.Width,
                    Y = y,
                    W = action.Width,
                    H = action.Height,
                };
                SDL.RenderTexture(renderer, action.Texture, IntPtr.Zero, in actionDst);
            }
        }
    }

    private static void DestroyLegend(LegendRow[] rows)
    {
        foreach (var row in rows) {
            if (row.Key.Texture != IntPtr.Zero) {
                SDL.DestroyTexture(row.Key.Texture);
            }

            if (row.Action.Texture != IntPtr.Zero) {
                SDL.DestroyTexture(row.Action.Texture);
            }
        }
    }
}
