<p align="center"><h1>Zenith Engine</h1></p>

<p align="center">
  <strong>An open-source, Unity-like game engine in pure C# on .NET 10</strong>
</p>

<p align="center">
  <a href="https://github.com/zzRinexD/ZenithEngine/actions/workflows/ci.yml"><img src="https://github.com/zzRinexD/ZenithEngine/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/github/languages/top/zzRinexD/ZenithEngine" alt="Top language">
  <a href="https://dotnet.microsoft.com/en-us/download/dotnet/10.0"><img src="https://img.shields.io/badge/.NET-10.0-512BD4" alt=".NET 10"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/zzRinexD/ZenithEngine" alt="License"></a>
  <a href="https://github.com/zzRinexD/ZenithEngine/releases"><img src="https://img.shields.io/github/v/release/zzRinexD/ZenithEngine?include_prereleases" alt="Release"></a>
  <a href="https://github.com/zzRinexD/ZenithEngine/issues"><img src="https://img.shields.io/github/issues/zzRinexD/ZenithEngine" alt="Issues"></a>
</p>

---

Zenith is a fork of **[Prowl Engine](https://github.com/ProwlEngine/Prowl)** — an MIT-licensed,
Unity-like game engine originally created by **Michael Sakharov (Wulferis)** — reborn under a new
name with its own identity, dark theme, and editor UX.

It keeps what makes Prowl great: a familiar Unity-style API (GameObject & MonoBehaviour), a
KISS-oriented design, and a runtime that works fully standalone from the editor. Ideally, Unity
projects can port over with as little resistance as possible.

> **Status:** early alpha (`v0.1.0-alpha.x`). Breaking changes are still possible between
> alpha releases.

1. [Features](#-features-)
2. [Getting Started](#-getting-started-)
3. [Testing](#-testing-)
4. [Repository Layout](#-repository-layout-)
5. [Credits](#-credits-)
6. [License](#-license-)

# ✨ Features

- **General:**
    - Cross-Platform! Windows, Linux & Mac, for both the Editor and exported builds
    - Unity-like Editor & Scripting API
    - C# Scripting with .NET 10
    - GameObject & MonoBehaviour Component Architecture
    - **Zenith.Runtime works fully standalone from the Editor** — reference it directly and
      ship a game with zero Editor dependency
    - Custom Immediate Mode UI ([Paper](https://github.com/ProwlEngine/Anthology)), Editor built
      on top of [Origami](https://github.com/ProwlEngine/Anthology)
    - Vector Graphics & Text Rendering via [Quill](https://github.com/ProwlEngine/Anthology)
    - Full-Featured Editor
        - Scene View, Hierarchy, Inspector, Project Browser, Console, Game View
        - Custom Component Editors, Property Editors, and Scene View Editors
        - Transform Gizmos (Move, Rotate, Scale)
        - Undo/Redo System
        - Dockable & Resizable Panels with Layout Persistence
        - Drag & Drop (Assets, GameObjects, Components)
        - Multi-Select & Search/Filtering in Editor Panels
        - Asset Thumbnail Generation & 3D Previews
        - Animation Curve & Gradient Editors
        - Rebindable Shortcut/Hotkey System
        - Editor Theming with Customizable Color Palettes and sizing
        - Playtest directly in the Editor
        - Hot-Reloading Scripts
        - Localization — English, German, Spanish, French, Italian, Japanese, Korean, Polish,
          Portuguese, Russian, Turkish & Chinese
        - Managed & Native Plugins with Assembly Definitions
    - Physics using [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2)
        - Colliders: Box, Sphere, Capsule, Cylinder, Cone, Convex Hull, Mesh, Model, Terrain
        - Wheel Collider (raycast-based vehicle wheel, with suspension & slip-based grip)
        - Joints & Constraints: Ball Socket, Hinge, Fixed Angle, Cone/Twist/Distance Limits,
          Prismatic, Universal, Point On Line/Plane, Angular & Linear Motors
        - Character Controller
        - Trigger Volumes (Box, Sphere, Capsule)
        - Collision Layers & Filtering (LayerMask)
        - Raycasting & Shape Query API
    - Audio via MiniAudio
        - Spatial 3D Audio with Attenuation & Doppler
        - Supports WAV, MP3, OGG, FLAC
        - Effect chain (Delay, Distortion, Biquad Filter, Reverb, Phaser) + custom `IAudioEffect`
    - Serialization via [Prowl.Echo](https://github.com/ProwlEngine/Anthology)
    - Tags & Layers System
    - Scene System with Fog & Ambient Lighting
    - Prefabs with Nested Prefab Support (Apply, Revert, Break Instance & Override Tracking)
    - Projects & Project Settings (project files use the `.zenith` extension)
    - Script Compilation via dotnet build (Game & Editor Assemblies)
    - Input Action System with Composites & Processors
        - `.inputactions` assets with a dedicated editor
        - Action phases (Disabled / Started / Performed / Cancelled)
        - Composite bindings (WASD → Float2, D-pad, etc.) for keyboard, mouse & gamepad
    - GameObject-Based UI, including World Space UI
        - `RectTransform`-driven layout, Buttons, Sliders, layout groups, drag & drop handlers
    - Zenith Actions — persistent, inspector-configurable event callbacks
    - Math via [Prowl.Vector](https://github.com/ProwlEngine/Anthology)
        - Matrices (`Float4x4`), Quaternions, Transform2D
        - Shapes: AABB, Bounds, Frustum, Cone, Ray, Plane, LineSegment, Rect
    - Build System — build to a standalone application
        - Packed Asset Files (`.prowlpak`), only exports used assets
        - Per-platform build profiles (Windows, Mac & Linux)
    - **Unit Tested — ~1,500 tests across the Runtime and Editor**

- **Graphics Rendering:**
    - OpenGL Backend via [Silk.NET](https://github.com/dotnet/Silk.NET)
    - Dedicated Render Thread
    - Extensible Render Pipeline (Custom Pipelines Supported)
    - Forward-Lit Pipeline with Thin G-Buffer Pre-Pass (Depth, Normals, Motion, Roughness, Metallic)
    - UV-Unwrapping via [Prowl.Unwrapper](https://github.com/ProwlEngine/Anthology), Progressive
      Lightmapper via [Prowl.Photonic](https://github.com/ProwlEngine/Anthology)
    - Baked Light Probes
    - Custom Shader Language with #include Support, Multi-Pass, and Shader Keywords/Variants
    - HDR & PBR (Metallic Workflow): Albedo, Normal, Surface (AO / Roughness / Metallic), Emission
    - Mesh Renderer & Skinned Mesh Renderer with Bone Animation and Blendshapes
    - Line Renderer
    - Sprites, with Sprite Sheet slicing and a dedicated Sprite Editor
    - Render Textures & Texture3D
    - GPU Instancing & Frustum Culling
    - Point, Spot, and Directional Lights, all with Shadow Mapping
        - Cascaded Shadow Maps (up to 4 cascades), Cubemap Shadows, Shadow Atlas with Dynamic Packing
    - Post Processing
        - HDR Tonemapping (ACES / Reinhard / Uncharted / Filmic / Melon / AgX)
        - Bloom, FXAA, TAA, SMAA, GTAO, Stochastic SSR, Bokeh DoF, Volumetric Fog
        - Cinematic Effects (grain, vignette, chromatic aberration)
    - Transparency + depth-aware Grab Pass (refraction / heat-haze / frosted glass)
    - Procedural / Cubemap / Gradient Skybox
    - Terrain System
        - Quadtree LOD, Heightmap & Splatmap Painting, Holes
        - GPU-Instanced Grass Rendering, Tree Rendering with LOD Distance
        - Dedicated Terrain Editor (Height, Paint, Grass, Trees, Settings)
    - Particle System
        - GPU-Instanced Rendering
        - Modules: Emission, Size/Color/Rotation/Velocity Over Lifetime, Collision, UV Animation
        - Local & World Simulation Spaces

- **Asset Pipeline:**
    - GUID-Based Asset References with Meta Files
    - Import Caching & File Watching for Auto-Reimport
    - Custom Importers via Attributes
    - Sub-Assets with Deterministic GUIDs
    - Forward & Reverse Dependency Tracking
    - Threaded Asset Loading
    - Supported Formats:
        - Models: GLTF, GLB, OBJ, FBX (via [Prowl.Clay](https://github.com/ProwlEngine/Anthology))
        - Textures: PNG, JPG, BMP, TGA, PSD, HDR, DDS, EXR (via Magick.NET)
        - Audio: WAV, MP3, OGG, FLAC

# 🚀 Getting Started

### Prerequisites

* [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

### Build from source

```bash
git clone https://github.com/zzRinexD/ZenithEngine.git
cd ZenithEngine
dotnet build Zenith.sln
```

Or open `Zenith.sln` in [Visual Studio 17.8+](https://visualstudio.microsoft.com/),
[VS Code](https://code.visualstudio.com/), or [Rider](https://www.jetbrains.com/rider/).

### Run the Editor

```bash
dotnet run --project Zenith.Editor/Zenith.Editor.csproj
```

In VS Code you can also use the tasks `run: Zenith.Editor`, `test: Zenith.Runtime`
and `test: Zenith.Editor`.

# 🧪 Testing

The engine ships with ~1,500 xUnit tests (863 Runtime + 635 Editor):

```bash
dotnet test Zenith.Runtime.Test/Zenith.Runtime.Test.csproj
dotnet test Zenith.Editor.Test/Zenith.Editor.Test.csproj
```

Every push and pull request is verified by [CI](https://github.com/zzRinexD/ZenithEngine/actions/workflows/ci.yml).

# 📁 Repository Layout

| Project | Description |
|---|---|
| `Zenith.Runtime` | The engine itself — runs standalone, zero Editor dependency |
| `Zenith.Editor` | The editor (Scene View, Hierarchy, Inspector, Project Browser, Console…) |
| `Zenith.Analyzers` | Roslyn analyzers that guard engine-specific pitfalls |
| `Zenith.Runtime.Test` / `Zenith.Editor.Test` | xUnit test suites |
| `Players/Desktop` | Standalone player for exported games |
| `Samples/` | SimpleCube, PhysicsCubes, VoxelEngine, CarPhysicsDemo, AudioDemo, FlyCamera… |
| `docs/` | Workflow and architecture documentation |

# 🙏 Credits

**Zenith is a fork of [Prowl Engine](https://github.com/ProwlEngine/Prowl), created originally
by [Michael Sakharov (Wulferis)](https://twitter.com/Wulferis).** Without Prowl there would be
no Zenith — all credit for the original engine belongs to its author and its contributors:
[Michael (Wulferis)](https://twitter.com/Wulferis), [Abdiel Lopez (PaperPrototype)](https://github.com/PaperPrototype),
[Josh Davis](https://github.com/10xJosh), [ReCore67](https://github.com/recore67),
[Isaac Marovitz](https://github.com/IsaacMarovitz), [Kuvrot](https://github.com/Kuvrot),
[JaggerJo](https://github.com/JaggerJo), [Jihad Khawaja](https://github.com/jihadkhawaja),
[Jasper Honkasalo](https://github.com/japsuu), [Kai Angulo (k0t)](https://github.com/sinnwrig),
[Bruno Massa (brmassa)](https://github.com/brmassa), [Mark Saba (ZeppelinGames)](https://github.com/ZeppelinGames),
[Chandler Cox (Tryibion)](https://github.com/Tryibion), [EJTP (Unified)](https://github.com/EJTP),
[Paolo (xZekro51)](https://github.com/xZekro51),
[Kouame Benoit Junior Augustin (ZedDevStuff)](https://github.com/ZedDevStuff).

- Hat tip to the creators of [Raylib](https://github.com/raysan5/raylib) — while Prowl is no
  longer based upon it, it shaved off hours of development time getting the engine usable.
- Upstream community: [Prowl's Discord](https://discord.gg/BqnJ9Rn4sn).

### Dependencies 📦

- [Silk.NET](https://github.com/dotnet/Silk.NET) — Windowing, Input, OpenGL & Audio Bindings
- [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2) — Physics Engine
- [Magick.NET](https://github.com/dlemstra/Magick.NET) — Image Processing
- [Prowl.Echo](https://github.com/ProwlEngine/Anthology) — Serialization
- [Prowl.Paper](https://github.com/ProwlEngine/Anthology) — UI Framework
- [Prowl.Origami](https://github.com/ProwlEngine/Anthology) — Component Library for Paper
- [Prowl.Quill](https://github.com/ProwlEngine/Anthology) — Vector Graphics & Text Rendering
- [Prowl.Scribe](https://github.com/ProwlEngine/Anthology) — TrueType parsing & markdown layout
- [Prowl.Rosetta](https://github.com/ProwlEngine/Anthology) — Editor Localisation
- [Prowl.Vector](https://github.com/ProwlEngine/Anthology) — 64-bit Math Library
- [Prowl.Unwrapper](https://github.com/ProwlEngine/Anthology) — UV Unwrapper
- [Prowl.Photonic](https://github.com/ProwlEngine/Anthology) — Progressive Lightmapper
- [Prowl.Clay](https://github.com/ProwlEngine/Anthology) — Model Importing (GLTF, GLB, OBJ, FBX)

# 📜 License

Distributed under the **[MIT License](LICENSE)**.

Copyright (c) 2023 Michael Sakharov (Prowl Engine) and contributors.
Zenith is a derivative work of Prowl, used under the MIT License — see [`LICENSE`](LICENSE)
for the full text.

---

<p align="center">Zenith is an independent fork and is not affiliated with the Prowl Engine project.</p>
