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

### Spatial and visual control (v4)

Actual primitive-profile Unity captures: 128×128, Seed 234. No external art assets are required. Download a resolved request from [the gallery examples](Shared/Examples/spatial-v4-gallery.json) and load it at `/spatial-v4` to replay it.

| Central mountain | Exclusive central lake |
| --- | --- |
| ![Central mountain](Media/Showcase/v4-forest-mountain-234-128.png) | ![Exclusive lake](Media/Showcase/v4-forest-exclusive-lake-234-128.png) |
| River and separate bridge deck | Desert lake |
| ![River and bridge](Media/Showcase/v4-forest-river-bridge-234-128.png) | ![Desert lake](Media/Showcase/v4-desert-exclusive-lake-234-128.png) |
| Snowfield mountain | Swamp river and bridge |
| ![Snowfield mountain](Media/Showcase/v4-snowfield-mountain-234-128.png) | ![Swamp river and bridge](Media/Showcase/v4-swamp-river-bridge-234-128.png) |
| Cherry trees across eligible ground | Cherry trees restricted to the riverbank |
| ![Whole-map cherry](Media/Showcase/v4-wholemap-cherry-234-128.png) | ![Riverbank cherry](Media/Showcase/v4-riverbank-cherry-234-128.png) |

The last pair changes the tree region while retaining the seed, river and other parameters. These are controlled structured-request examples, not a claim that every natural-language prompt is interpreted correctly.

## Current capabilities

- Natural-language translation and direct JSON input, seven world types, and visual-category type, density, and count-cap controls.
- Legacy v3 supports seven world types; its `layoutSettings` still accepts neutral compatibility values only. Existing v3 snapshots are not silently upgraded.
- Experimental v4 supports Forest, Desert, Snowfield and Swamp: one mountain/lake/river per kind, positioned landmarks, central crossing rivers, exclusive water, no paths, and straight paths with separate bridge decks.
- v4 includes shared semantic placements, type/region restrictions, Off, relative density and maximum counts. Unity and Web consume the same placement coordinates/transforms; their materials and appearance differ. Legacy v3 browser placement remains a subset of Unity controls.
- v4 does not support City/Cave/Dungeon spatial control, exact counts, arbitrary relations, clustering, cactus/palm models or guaranteed walkability. Snow water has an ice-colored visual surface, not a tested traversable ice system.
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

Open `http://127.0.0.1:3000/spatial-v4` for spatial control. Resolve the example intent, then generate Web 3D. Save the final JSON to replay; “new Seed” changes only the seed without calling the LLM. Same request-ID retries are deduplicated during the current server process; retain the final JSON across restarts.

For optional natural-language request translation, copy `Web/.env.example` to `Web/.env`, add your own server-side key, and never commit that file.

## Run the Unity demo

1. Add the cloned `UnityDemo` folder in Unity Hub.
2. Open `Assets/PCGPublicDemo/Scenes/PrimitiveDemo.unity`.
3. Enter Play Mode.
4. Choose a world type and seed from the overlay. Press `F1` for the first-person test.

The sample creates its camera, light, generation pipeline, and visual fallback materials at runtime. It needs no external art assets. Its HTTP bridge is opt-in in package code, loopback-only, and explicitly enabled by this local demo scene on port `8088`.

For v4, stay outside Play Mode and choose **Tools → LLM2PCG → Experimental v4 → Start HTTP Preview**. This separate primitive-only preview listens on loopback port `8089`, accepts up to 256×256, and does not modify your working scene. Use the Unity generation button at `/spatial-v4`; close the preview window to stop it. Web/Core data generation supports up to 500×500.

An explicit v3 migration proposal can be printed with `node Scripts/Propose-V4Migration.mjs saved-v3-request.json`. Review its warnings and proposed `request`: it creates a different v4 world and never overwrites or automatically runs the source snapshot.

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
