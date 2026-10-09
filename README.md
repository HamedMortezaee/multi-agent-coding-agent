# Multi-Agent Coding Agent

A multi-agent coding assistant orchestrated with **n8n**, backed by **ASP.NET Core / C#**, and integrated with **OpenAI**.

## Current Architecture

- **n8n runtime:** n8nir.ir
- **Backend:** ASP.NET Core
- **Language:** C#
- **LLM provider:** Configurable (`OpenAI` or `Aifa`)
- **Deployment:** Windows Hosting
- **Workflow delivery:** Importable n8n JSON
- **Human-in-the-loop:** n8n
- **State management:** ASP.NET Core backend
- **Generated workspace:** `App_Data/workspaces/{executionId:N}` on the API host
- **Code execution:** Abstracted behind `IExecutionSandbox` (runner currently unavailable)
- **Max fix attempts:** 3
- **Max run duration:** 15 minutes
- **Final report:** Markdown
- **Initial demo target:** ASP.NET Core Todo REST API

## Documentation

- `docs/ADR/ADR-001-Architecture-Decisions.md`
- `docs/SDD/Software-Design-Document.md`
- `docs/Technical/Agent-Contracts.md`
- `docs/Technical/API-Design.md`

## Repository Structure

```text
multi-agent-coding-agent/
├── README.md
├── docs/
│   ├── ADR/
│   └── SDD/
├── src/
├── tests/
└── workflows/
```

The `workflows` directory will contain n8n workflow JSON files that can be imported into n8nir.ir.


## Generated Workspaces

After the Coder agent produces files, the backend materializes the current project snapshot to disk:

```text
App_Data/
└── workspaces/
    └── {executionId:N}/
        ├── *.sln
        ├── README.md
        ├── src/
        └── tests/
```

The workspace is rebuilt from the canonical files stored in the agent run. Fixer changes are also re-materialized so the on-disk workspace stays synchronized with the latest project state.

The Windows application pool identity must have **Modify/Write** permission on `App_Data/workspaces`.


## LLM Provider Selection

The backend uses `ILlmService` and can switch providers without changing the agents.

OpenAI:

```json
"AI": {
  "Provider": "OpenAI",
  "Model": "gpt-5.6-sol"
}
```

AIFA:

```json
"AI": {
  "Provider": "Aifa",
  "Model": "gpt-5.6-sol"
},
"Aifa": {
  "BaseUrl": "https://aifa-chatbot.dev.dotin.ir/",
  "Model": "assistance-model",
  "Token": "",
  "UserId": "coding-agent"
}
```

Provider credentials are read from configuration only. Prefer environment variables or deployment secrets in real environments; do not commit real credentials.

Diagnostics:

```text
GET  /api/v1/diagnostics/llm
POST /api/v1/diagnostics/llm/test
```
