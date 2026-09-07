# LLM2PCG

[English](README.md) | [한국어](README.ko.md)

LLM2PCG is a deterministic procedural world-generation system driven by natural language or structured requests. A validated JSON request selects one of seven generators and produces reproducible world data that can be rendered in a browser or inside Unity 6.

The Node.js middleware resolves and validates requests. A shared C# core generates world data for Unity and an independent Three.js browser renderer.

## Architecture

```text
Natural-language prompt (optional OpenAI API integration)
                         |
                         v
Browser UI ---> validated shared JSON contract
                         |
              +----------+----------+
              |                     |
              v                     v
       .NET CoreHost         Unity loopback Bridge
       pure generators       main-thread dispatcher
              |                     |
              v                     v
       Three.js renderer     Unity mesh/instancing renderers
              |                     |
              +----------+----------+
                         v
        deterministic world hash + reproducible result
```

Supported worlds: Dungeon, Cave, Forest, City, Swamp, Snowfield, and Desert.

## Generation examples

Actual Unity development captures from 2026-09-06. The visual assets, materials, and authored scene shown here are not included in this repository; the cloneable demo uses primitive fallbacks. These images illustrate generated environments, not complete fulfillment of every natural-language constraint.

| Woodland | Cherry-blossom forest |
| --- | --- |
| ![Woodland](Media/Showcase/01-woodland-detail.png) | ![Cherry-blossom forest](Media/Showcase/02-cherry-detail.png) |
| Desert | Snowfield |
| ![Desert](Media/Showcase/03-desert-detail.png) | ![Snowfield](Media/Showcase/04-snowfield-detail.png) |
| Swamp | Cave |
| ![Swamp](Media/Showcase/05-swamp-detail.png) | ![Cave](Media/Showcase/06-cave-detail.png) |
| City | Dungeon |
| ![City](Media/Showcase/07-city-detail.png) | ![Dungeon](Media/Showcase/08-dungeon-detail.png) |

Seed / size: woodland 234 / 96×96; cherry forest 234 / 64×64; desert 311 / 96×96; snowfield 412 / 96×96; swamp 513 / 64×64; cave 614 / 64×64; city 715 / 96×96; dungeon 816 / 64×64. A seed alone is insufficient for replay: retain the resolved request and matching generator implementation.

## Current capabilities

- Natural-language translation and direct JSON input, seven world types, and visual-category type, density, and count-cap controls.
- Visual results depend on the renderer and available profile. The browser demo implements a subset of Unity's visual-placement controls; identical world data does not imply identical prop layouts or appearance.
- Spatial overrides such as a central mountain, positioned river, or crossing road are currently disabled. layoutSettings accepts neutral compatibility values only.
- The Core validates numeric and type constraints and reports selected incomplete-generation conditions through world.diagnostics.
- Replay uses the same resolved request and generator implementation. Translating the same prompt again may produce different parameters.

## Requirements

- Git
- .NET 8 SDK
- Node.js 20 or newer
- PowerShell 7 recommended
- Unity `6000.5.10f1` for the Unity demo (the standalone browser demo does not require Unity)

## Clone and verify

```powershell
git clone https://github.com/heparidayo/llm2pcg.git
cd llm2pcg
pwsh -File ./Scripts/Test-Clone.ps1
```

The verification script installs locked Web dependencies, builds the .NET host, runs deterministic CoreHost checks, runs Web tests, and performs a seven-world browser/API smoke test.

## Run the browser demo

```powershell
pwsh -File ./Scripts/Start-StandaloneWeb.ps1
```

Open `http://127.0.0.1:3000`. The default preset/direct generation flow is local and does not require an API key. Press `Ctrl+C` in the terminal to stop both owned processes.

For optional natural-language request translation, copy `Web/.env.example` to `Web/.env`, add your own server-side key, and never commit that file.

## Run the Unity demo

1. Add the cloned `UnityDemo` folder in Unity Hub.
2. Open `Assets/PCGPublicDemo/Scenes/PrimitiveDemo.unity`.
3. Enter Play Mode.
4. Choose a world type and seed from the overlay. Press `F1` for the first-person test.

The sample creates its camera, light, generation pipeline, and visual fallback materials at runtime. It needs no external art assets. Its HTTP bridge is opt-in in package code, loopback-only, and explicitly enabled by this local demo scene on port `8088`.

## Reuse as a Unity package

After a public tag exists, Unity Package Manager can install the package subfolder with a Git URL shaped like:

```text
https://github.com/heparidayo/llm2pcg.git?path=/Packages/com.heparidayo.llm2pcg#<tag>
```

Until then, add `Packages/com.heparidayo.llm2pcg` from the clone as a local package.

## Repository boundaries

- `Packages/com.heparidayo.llm2pcg`: reusable Unity runtime and EditMode tests
- `UnityDemo`: primitive-only Unity demonstration project
- `Standalone`: .NET 8 host for Unity-independent generation
- `Web`: Three.js browser renderer and optional prompt adapter
- `Shared`: versioned JSON Schema and JavaScript validation/defaults
- `Scripts`: clone verification and local launcher

A project-wide open-source license has not been added. The included screenshots do not grant rights to the depicted source assets.
