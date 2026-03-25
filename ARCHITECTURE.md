# SolidWorks AI Assistant Architecture

## Product Summary
The plugin is an in-process SolidWorks add-in that lets a chat model inspect and operate on the active CAD session through a controlled tool layer. It now supports multiple LLM providers behind one provider-neutral orchestration layer, with explicit approval for any mutating CAD or file operation.

Primary goals:
- keep SolidWorks COM access inside the add-in process
- allow OpenAI and Anthropic to share one tool contract
- preserve structured tool-call history across turns
- force human approval before geometry, dimension, save, or export actions

## Component Diagram
```text
User
  |
  v
TaskPane UI
  |
  | user prompt / provider selection / API key validation
  v
AgentSessionOrchestrator
  |
  | normalized conversation + loop control + approval gate
  +--------------------------+
  |                          |
  v                          v
ModelProviderRegistry    Tool Approval Dialog
  |                          |
  | current provider/model   | approve / reject mutating tool calls
  v                          |
OpenAIModelProvider          |
AnthropicModelProvider       |
  |                          |
  +------------+-------------+
               |
               | normalized tool calls
               v
          SwToolExecutor
               |
               | STA-marshalled COM calls
               v
          SolidWorks API
```

## Prompt To Execution Flow
```text
1. User sends prompt in TaskPane.
2. ChatTaskPane forwards the prompt to AgentSessionOrchestrator.
3. Orchestrator checks active document identity.
   - If the document changed since the last turn, structured conversation history is reset.
4. Orchestrator sends the normalized conversation to the selected provider adapter.
5. Provider adapter translates shared tool definitions into provider-specific tool/function schema.
6. Model returns assistant text and/or tool calls with JSON arguments.
7. Orchestrator appends the structured assistant response to history.
8. For each tool call:
   - if the tool is read-only, execute immediately
   - if the tool mutates CAD or files, ask the user for approval first
9. SwToolExecutor marshals the call back to the SolidWorks STA thread and executes the COM API call.
10. Tool results are appended to structured history as tool_result blocks.
11. The orchestrator sends the updated conversation back to the provider until:
   - the model returns final assistant text, or
   - the loop hits an explicit failure condition.
12. Final assistant text is shown in the TaskPane.
```

## Provider-Agnostic Communication Model
```text
Shared conversation model
  - role = user / assistant / tool
  - blocks = text / tool_call / tool_result

Shared request
  - system prompt
  - selected model
  - structured conversation
  - tool definitions

Provider translation
  - Anthropic:
    assistant text + tool_use blocks
    tool results returned as user tool_result content blocks
  - OpenAI:
    assistant content + tool_calls
    tool results returned as tool-role messages

Shared execution contract
  - provider returns normalized tool calls
  - executor returns normalized JSON result envelopes
```

## State Ownership
- `ChatTaskPane`: owns UI controls, busy state, and approval prompts.
- `ModelProviderRegistry`: owns current provider selection, current model selection, and loaded provider instances.
- `AgentSessionOrchestrator`: owns structured conversation state, document-boundary resets, provider turn loop, and usage aggregation.
- `SwToolExecutor`: owns the only write path into SolidWorks COM APIs.
- `CredentialStore`: owns encrypted API-key persistence per provider.

## Read/Write Boundaries
- Read-only tools:
  - active document info
  - feature list
  - model dimensions
  - mass properties
  - view orientation / zoom
- Mutating tools:
  - sketch creation and sketch geometry
  - feature creation
  - dimension updates
  - new part / new assembly
  - save / export

Mutating tools always require approval before execution.

## Failure Handling
- Invalid provider credentials:
  - validation fails in Settings before the provider is used
- Tool loop exhaustion:
  - surfaced as an explicit error instead of silently returning partial text
- Provider returns incomplete response:
  - surfaced as an explicit error with the provider stop reason
- User rejects a mutating tool:
  - orchestrator returns a structured tool error to the model so it can recover
- Active document changes between turns:
  - conversation state is reset to avoid stale CAD context crossing files
