# LLM2PCG

[English](README.md) | [한국어](README.ko.md)

LLM2PCG is a deterministic procedural content generation (PCG) system that creates 3D virtual worlds from natural language or directly configured parameters. An LLM translates the user's intent into compact generation parameters, while a pure C# core generates forests, deserts, snowfields, swamps, caves, cities and dungeons. The same resolved request, seed and generator version reproduce the same world.

Results can be viewed in a **browser (Three.js) or Unity 6**. **Unreal Engine integration is planned**; an Unreal adapter is not yet implemented.

![LLM2PCG Forest with commercial assets — Seed 234, Unity first-person showcase](Media/Showcase/hero-seed234-forest.png)

*A Unity showcase of an LLM2PCG-generated forest rendered with commercial assets, illustrating how generated terrain and object placements can be presented as a game environment.*

Reproduction settings — **historical development capture**: seed `234` · `128×128` · `DefaultForest` · water threshold `.24` · noise octaves `4` · clearing radius `14` · vegetation distance/max `4/600` · elevation scale/frequency `6/.035` · default visual settings · F1 starting position · F2 UI hidden · world hash `E839AE91` · [Exact request JSON](Shared/Examples/legacy-seed234-forest.json)

> The commercial assets and development scene shown above are not included in this repository. The public demo runs with primitive geometry and requires no external art assets. This historical request targets `forest-biome@1`, not the current v4 examples. Reproducing the same appearance requires the original generator implementation, assets and rendering settings. A matching world hash does not imply an identical image.

## Architecture

```text
Natural language --> LLM --> Node.js resolver
                                  |
Direct JSON / MCP --> Validated JSON + Seed
                                  |
               +------------------+------------------+
               v                                     v
        .NET CoreHost                       Unity loopback Bridge
        Shared C# PCG Core                  Shared C# PCG Core
               |                                     |
        Generated world data                Unity mesh / instancing
               |
        +------+--------------------+
        v                           : HTTP / world-data contract
 Three.js renderer                  v
                             Unreal C++ adapter [PLANNED]
                                    :
                                    v
                             Unreal mesh / instancing [PLANNED]

 Solid paths: implemented. Dotted paths: planned integration.
 Replay: resolved request + Seed + matching generator implementation.
```

The planned Unreal adapter will consume world data from .NET CoreHost rather than run C# inside Unreal or generate a second layout. This is the intended integration design, not a currently available feature or verified Unreal replay result.

Supported worlds: Dungeon, Cave, Forest, City, Swamp, Snowfield, and Desert.

## Generation examples

Actual Unity development captures; the cherry-blossom image was supplied on 2026-09-10, and the other images are from 2026-09-06. The visual assets, materials, and authored scene shown here are not included in this repository; the cloneable demo uses primitive fallbacks. These images illustrate generated environments, not complete fulfillment of every natural-language constraint.

| Woodland | Cherry-blossom forest |
| --- | --- |
| ![Woodland](Media/Showcase/01-woodland-detail.png) | ![Cherry-blossom forest](Media/Showcase/02-cherry-detail.png) |
| Desert | Snowfield |
| ![Desert](Media/Showcase/03-desert-detail.png) | ![Snowfield](Media/Showcase/04-snowfield-detail.png) |
| Swamp | Cave |
| ![Swamp](Media/Showcase/05-swamp-detail.png) | ![Cave](Media/Showcase/06-cave-detail.png) |
| City | Dungeon |
| ![City](Media/Showcase/07-city-detail.png) | ![Dungeon](Media/Showcase/08-dungeon-detail.png) |

Seed / size: woodland 234 / 96×96; desert 311 / 96×96; snowfield 412 / 96×96; swamp 513 / 64×64; cave 614 / 64×64; city 715 / 96×96; dungeon 816 / 64×64. The replacement cherry-blossom image has no supplied request metadata; the previous image's seed and size do not apply. A seed alone is insufficient for replay: retain the resolved request and matching generator implementation.

### Spatial and visual control (v4)

Actual primitive-profile Unity captures: 128×128, Seed 234. No external art assets are required. At `/spatial-v4`, select a README gallery example, load it and generate Web 3D. For file-based replay, save one entry's `request` object from [the gallery examples](Shared/Examples/spatial-v4-gallery.json) as JSON; do not upload the whole gallery array.

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

The verification script installs locked Web dependencies, runs Web/MCP tests, builds the .NET host, checks deterministic replays and performs seven-world legacy plus eight-example v4 API smoke tests. Development-only fixture tests are explicitly skipped in this curated checkout.

## Run the browser demo

```powershell
pwsh -File ./Scripts/Start-StandaloneWeb.ps1
```

Open `http://127.0.0.1:3000`. The default preset/direct generation flow is local and does not require an API key. Press `Ctrl+C` in the terminal to stop both owned processes.

Open `http://127.0.0.1:3000/spatial-v4` for spatial control. Resolve the example intent, then generate Web 3D. Save the final JSON to replay; “new Seed” changes only the seed without calling the LLM. Same request-ID retries are deduplicated during the current server process; retain the final JSON across restarts.

For optional natural-language request translation, copy `Web/.env.example` to `Web/.env`, add your own server-side key, and never commit that file.

## Use MCP

With the standalone server running, configure an MCP client to launch `node` with an absolute path to `MCP/src/server.mjs`. This stdio server has no additional dependencies or API-key requirement. The `generate_world_v4` tool accepts `{ "request": <resolved v4 JSON> }` and returns CoreHost world data; it does not render a Unity scene. `PCG_V4_CORE_ENDPOINT` overrides its default `http://127.0.0.1:8090/api/v4/world/generate` when using a custom Core port.

The legacy `generate_world` / `generate_dungeon` tools instead target Unity's opt-in bridge at `http://127.0.0.1:8088/pcg/generate` (`UNITY_PCG_ENDPOINT` override). Never expose either local bridge to an untrusted network.

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
- `MCP`: stdio tools for validated, resolved requests
- `Shared`: versioned JSON Schema and JavaScript validation/defaults
- `Scripts`: clone verification and local launcher

A project-wide open-source license has not been added. The included screenshots do not grant rights to the depicted source assets.
