# LLM2PCG

LLM2PCG is a portfolio-ready, deterministic procedural world-generation prototype. A validated JSON request selects one of seven generators and produces reproducible world data that can be rendered in a browser or inside Unity 6.

This repository is currently a **private release candidate**. It deliberately contains no commercial art packs, private AI source images, internal documents, presentations, screenshots, editor-agent configuration, conversation logs, or credentials.

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

No open-source license has been selected in this private candidate yet. Choose and add the final license before changing the GitHub repository to public.
