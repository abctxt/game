using System.Buffers;
using System.Numerics;
using ImGuiNET;
using SDL3;


namespace Game;

/// <summary>
/// SDL_Renderer backend for ImGui.NET, adapted from
/// https://github.com/behindcurtain3/SDL3-ImGui
/// </summary>
internal sealed class ImGuiSdl3Renderer : IDisposable
{
    private readonly nint _renderer;
    private nint _fontTexture;

    public ImGuiSdl3Renderer(nint renderer)
    {
        _renderer = renderer;

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
    }

    public void Dispose() => DestroyFontsTexture();

    public void NewFrame()
    {
        if (_fontTexture == IntPtr.Zero) {
            CreateFontsTexture();
        }
    }

    public void RenderDrawData(ImDrawDataPtr drawData)
    {
        unsafe {
            if (drawData.NativePtr == null || drawData.CmdListsCount == 0) {
                return;
            }
        }

        var renderScale = drawData.FramebufferScale;
        var fbWidth = (int)(drawData.DisplaySize.X * renderScale.X);
        var fbHeight = (int)(drawData.DisplaySize.Y * renderScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0) {
            return;
        }

        var oldState = BackupRendererState();
        SetupRenderState(renderScale);

        var clipOffset = drawData.DisplayPos;
        for (var n = 0; n < drawData.CmdListsCount; n++) {
            var cmdList = drawData.CmdLists[n];
            for (var cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++) {
                var cmd = cmdList.CmdBuffer[cmdIndex];
                if (cmd.UserCallback != IntPtr.Zero) {
                    continue;
                }

                var clip = CalculateClipRect(cmd.ClipRect, clipOffset, renderScale, fbWidth, fbHeight);
                if (clip.W <= 0 || clip.H <= 0) {
                    continue;
                }

                SDL.SetRenderClipRect(_renderer, in clip);
                if (!RenderDrawCommand(cmdList, cmd, cmd.GetTexID())) {
                    Console.Error.WriteLine($"ImGui geometry failed: {SDL.GetError()}");
                }
            }
        }

        RestoreRendererState(oldState);
    }

    private unsafe bool RenderDrawCommand(ImDrawListPtr drawList, ImDrawCmdPtr cmd, nint texture)
    {
        var indexOffset = (int)cmd.IdxOffset;
        var vertexOffset = (int)cmd.VtxOffset;
        var elemCount = (int)cmd.ElemCount;
        if (elemCount <= 0) {
            return true;
        }

        ushort minVertexIdx = ushort.MaxValue;
        ushort maxVertexIdx = 0;
        for (var i = 0; i < elemCount; i++) {
            var idx = drawList.IdxBuffer[indexOffset + i];
            minVertexIdx = Math.Min(minVertexIdx, idx);
            maxVertexIdx = Math.Max(maxVertexIdx, idx);
        }

        minVertexIdx = (ushort)(minVertexIdx + vertexOffset);
        maxVertexIdx = (ushort)(maxVertexIdx + vertexOffset);
        var numVertices = maxVertexIdx - minVertexIdx + 1;

        var vertices = ArrayPool<SDL.Vertex>.Shared.Rent(numVertices);
        var indices = ArrayPool<int>.Shared.Rent(elemCount);
        try {
            for (var i = 0; i < numVertices; i++) {
                var src = drawList.VtxBuffer[minVertexIdx + i];
                var col = src.col;
                vertices[i] = new SDL.Vertex {
                    Position = new SDL.FPoint { X = src.pos.X, Y = src.pos.Y },
                    Color = new SDL.FColor {
                        R = ((col >> 0) & 0xFF) / 255f,
                        G = ((col >> 8) & 0xFF) / 255f,
                        B = ((col >> 16) & 0xFF) / 255f,
                        A = ((col >> 24) & 0xFF) / 255f,
                    },
                    TexCoord = new SDL.FPoint { X = src.uv.X, Y = src.uv.Y },
                };
            }

            var vertexBase = minVertexIdx - vertexOffset;
            for (var i = 0; i < elemCount; i++) {
                indices[i] = drawList.IdxBuffer[indexOffset + i] - vertexBase;
            }

            return SDL.RenderGeometry(_renderer, texture, vertices, numVertices, indices, elemCount);
        } finally {
            ArrayPool<int>.Shared.Return(indices);
            ArrayPool<SDL.Vertex>.Shared.Return(vertices);
        }
    }

    private void SetupRenderState(Vector2 scale)
    {
        SDL.SetRenderViewport(_renderer, IntPtr.Zero);
        SDL.SetRenderClipRect(_renderer, IntPtr.Zero);
        SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);
        SDL.SetRenderScale(_renderer, scale.X, scale.Y);
    }

    private BackupState BackupRendererState()
    {
        SDL.GetRenderScale(_renderer, out var scaleX, out var scaleY);
        return new BackupState {
            ViewportEnabled = SDL.RenderViewportSet(_renderer),
            ClipEnabled = SDL.RenderClipEnabled(_renderer),
            ScaleX = scaleX,
            ScaleY = scaleY,
            Viewport = SDL.GetRenderViewport(_renderer, out var viewport) ? viewport : default,
            ClipRect = SDL.GetRenderClipRect(_renderer, out var clip) ? clip : default,
        };
    }

    private void RestoreRendererState(BackupState state)
    {
        if (state.ViewportEnabled) {
            SDL.SetRenderViewport(_renderer, in state.Viewport);
        } else {
            SDL.SetRenderViewport(_renderer, IntPtr.Zero);
        }

        if (state.ClipEnabled) {
            SDL.SetRenderClipRect(_renderer, in state.ClipRect);
        } else {
            SDL.SetRenderClipRect(_renderer, IntPtr.Zero);
        }

        SDL.SetRenderScale(_renderer, state.ScaleX, state.ScaleY);
    }

    private static SDL.Rect CalculateClipRect(Vector4 clipRect, Vector2 clipOffset, Vector2 scale, int fbWidth, int fbHeight)
    {
        var clipMin = new Vector2((clipRect.X - clipOffset.X) * scale.X, (clipRect.Y - clipOffset.Y) * scale.Y);
        var clipMax = new Vector2((clipRect.Z - clipOffset.X) * scale.X, (clipRect.W - clipOffset.Y) * scale.Y);

        clipMin.X = Math.Max(0, clipMin.X);
        clipMin.Y = Math.Max(0, clipMin.Y);
        clipMax.X = Math.Min(fbWidth, clipMax.X);
        clipMax.Y = Math.Min(fbHeight, clipMax.Y);

        return new SDL.Rect {
            X = (int)clipMin.X,
            Y = (int)clipMin.Y,
            W = (int)(clipMax.X - clipMin.X),
            H = (int)(clipMax.Y - clipMin.Y),
        };
    }

    private unsafe bool CreateFontsTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out var width, out var height);

        // ImGui RGBA32 bytes match SDL_PIXELFORMAT_RGBA32, which is ABGR8888 on little-endian.
        var surface = SDL.CreateSurfaceFrom(width, height, SDL.PixelFormat.ABGR8888, (nint)pixels, width * 4);
        if (surface == IntPtr.Zero) {
            Console.Error.WriteLine($"ImGui font surface failed: {SDL.GetError()}");
            return false;
        }

        _fontTexture = SDL.CreateTextureFromSurface(_renderer, surface);
        SDL.DestroySurface(surface);
        if (_fontTexture == IntPtr.Zero) {
            Console.Error.WriteLine($"ImGui font texture failed: {SDL.GetError()}");
            return false;
        }

        SDL.SetTextureBlendMode(_fontTexture, SDL.BlendMode.Blend);
        SDL.SetTextureScaleMode(_fontTexture, SDL.ScaleMode.Linear);
        io.Fonts.SetTexID(_fontTexture);
        io.Fonts.ClearTexData();
        return true;
    }

    private void DestroyFontsTexture()
    {
        if (_fontTexture == IntPtr.Zero) {
            return;
        }

        ImGui.GetIO().Fonts.SetTexID(IntPtr.Zero);
        SDL.DestroyTexture(_fontTexture);
        _fontTexture = IntPtr.Zero;
    }


    private struct BackupState
    {
        public bool ViewportEnabled;
        public bool ClipEnabled;
        public float ScaleX;
        public float ScaleY;
        public SDL.Rect Viewport;
        public SDL.Rect ClipRect;
    }
}
