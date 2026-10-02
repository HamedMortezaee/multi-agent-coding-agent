# Multi-Agent Coding Agent

A multi-agent coding assistant orchestrated with **n8n**, backed by **ASP.NET Core / C#**, and integrated with **OpenAI**.

## Current Architecture

- **n8n runtime:** n8nir.ir
- **Backend:** ASP.NET Core
- **Language:** C#
- **LLM provider:** OpenAI
- **Deployment:** Windows Hosting
- **Workflow delivery:** Importable n8n JSON
- **Human-in-the-loop:** n8n
- **State management:** ASP.NET Core backend
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
