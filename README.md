# SolidWorks AI Assistant

SolidWorks AI Assistant is an in-process SolidWorks add-in that gives you a chat-based assistant inside the SolidWorks task pane. It can inspect the current model, create sketches and features, change dimensions, and export files through the SolidWorks API.

This repo is aimed at a practical workflow, not a demo-only mockup:
- the add-in runs inside SolidWorks
- the LLM layer is provider-agnostic
- OpenAI and Anthropic are both supported
- mutating actions require human approval before execution

If you want the full system view, start with [ARCHITECTURE.md](./ARCHITECTURE.md).

## Current Status
What is already in place:
- provider-neutral orchestration layer
- OpenAI and Anthropic adapters
- shared tool definitions across providers
- per-provider encrypted API-key storage
- per-action approval for model-changing and file-writing tools
- session reset when the active SolidWorks document changes

What this means in practice:
- read-only questions can run directly
- geometry creation and export actions stop for confirmation
- the same UI can talk to different model providers

## What It Can Do Today
Read-only operations:
- inspect the active document
- list features
- inspect dimensions
- get mass properties
- change view orientation and zoom

Mutating operations:
- create a new part or assembly
- create sketches on the standard planes
- add sketch lines, circles, arcs, and rectangles
- extrude, cut-extrude, revolve, fillet, and chamfer
- change named dimensions
- save and export STEP files

## How It Works
At a high level, the flow is:

```text
TaskPane UI
  -> AgentSessionOrchestrator
  -> ModelProviderRegistry
  -> OpenAI or Anthropic provider
  -> SwToolExecutor
  -> SolidWorks COM API
```

The task pane collects your prompt. The orchestrator keeps the structured conversation and decides when tools should run. The provider adapter translates that conversation to the selected LLM API. Tool calls are routed back into the executor, which marshals execution onto the SolidWorks STA thread.

## Safety Model
The add-in does not auto-run all CAD changes.

- Read-only tools run immediately.
- Sketch, feature, dimension, create, save, and export tools require approval.
- If you reject a tool call, the model is told that the action was rejected.
- If the active SolidWorks document changes, the AI session is reset so stale context is not reused.
- If the model loop becomes incomplete or runs too long, the add-in surfaces an explicit error instead of silently returning a partial answer.

## Supported Providers
OpenAI:
- default model: `gpt-5.4`
- presets: `gpt-5.4-mini`, `gpt-4.1`

Anthropic:
- default model: `claude-sonnet-4-20250514`
- presets: `claude-opus-4-6`, `claude-haiku-4-5-20251001`

## Build And Install
This project targets `.NET Framework 4.8` and uses local SolidWorks interop DLLs.

Basic install flow:
1. Open [ClaudeSW.sln](./ClaudeSW.sln) in Visual Studio 2022.
2. Make sure the `.NET Framework 4.8 targeting pack` is installed.
3. Restore `Newtonsoft.Json`.
4. Build `Release | x64`.
5. If automatic COM registration does not succeed, register the output DLL with `RegAsm.exe`.
6. Open SolidWorks and enable `SolidWorks AI Assistant` in `Tools -> Add-Ins`.

The project file already points at the standard SolidWorks interop folder on this machine layout:
- `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\`

## First Run
1. Open the task pane.
2. Open `Settings`.
3. Select a provider.
4. Select a model for that provider.
5. Paste the provider-specific API key.
6. Click `Validate & Save`.

API keys are encrypted with Windows DPAPI and stored under `%APPDATA%\ClaudeSW`.

## Known Contract Limits
The tool surface was intentionally narrowed to match the current executor implementation.

- `sw_extrude` supports `one_direction` and `both_directions`
- `sw_revolve` does not expose an explicit axis argument yet
- `sw_fillet` and `sw_chamfer` work on currently selected edges
- `sw_new_part` does not expose unit-system switching yet
- `sw_create_sketch` is limited to `Front`, `Top`, and `Right`

## Pending Work
The project is usable, but it is not finished. The main items still pending are:

- stronger SolidWorks typing instead of relying so heavily on `dynamic`
- integration testing on a real SolidWorks workstation
- better entity-selection tools for faces, edges, and named references
- grouped approvals for multi-step operations instead of one dialog per action
- undo-group support for a full agent action sequence
- better assembly-specific operations, mates, and drawing support
- richer provider settings and model capability handling
- stronger packaging and installation flow for non-developer users

## Repo Map
- [ARCHITECTURE.md](./ARCHITECTURE.md): product flow and communication diagrams
- [ClaudeSwAddin.cs](./ClaudeSwAddin.cs): add-in entry point and provider bootstrap
- [Api/AgentSessionOrchestrator.cs](./Api/AgentSessionOrchestrator.cs): shared agent loop and session state
- [Api/OpenAIModelProvider.cs](./Api/OpenAIModelProvider.cs): OpenAI adapter
- [Api/AnthropicClient.cs](./Api/AnthropicClient.cs): Anthropic adapter
- [Tools/SwToolExecutor.cs](./Tools/SwToolExecutor.cs): SolidWorks tool execution
- [UI/ChatTaskPane.cs](./UI/ChatTaskPane.cs): task pane and approval UI

## License
This project is licensed under the [MIT License](./LICENSE).
