# Game Simulator

A 2D game simulation, experimentation, and visualization tool.

The runtime is built on [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS) for windows, rendering, input, audio, and fonts, with [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) for in-view debug and experiment UI. The long-term aim is a place to run scenarios, tweak parameters, and watch the result.

## Layout

- `src/Game` — SDL3 + ImGui application (`game`)
- `src/Game.Start` — Avalonia launch dialog (`start`)
- `src/Game.FSharp` — F# library for simulation logic
- `tests/Game.Tests` — TUnit tests

Requires .NET 10. Native AOT publish is enabled on the executables.

Demo media under `src/Game/Assets/` is not in the repository. See that folder’s readme for what to supply.

License: GPL-3.0-or-later (see `License.md`).
