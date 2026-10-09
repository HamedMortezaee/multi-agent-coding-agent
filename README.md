# Multi-Agent Coding Agent

A multi-agent coding system orchestrated with **n8n**, implemented with **ASP.NET Core / C#**, and backed by configurable LLM providers.

## Current Status

The core flow has been validated end-to-end:

```text
Planner
  ↓
Human Review
  ↓
Coder
  ↓
Generated Project + xUnit Tests
  ↓
Runner
  ↓
dotnet restore / build / test
  ↓
Reviewer
  ↓
Final Report
```

Validated run:

```text
ExecutionId: fc30d504-9248-4413-81ee-f0e200a96c4a
Status: Completed
Tests: 3 passed, 0 failed
Reviewer nextAction: complete
```

## Architecture

```text
User
  ↓
n8n
  ↓ HTTPS/JSON
CodingAgent.Api
  ├── Planner
  ├── Human Review
  ├── Coder
  ├── Reviewer
  ├── Fixer
  ├── State/Persistence
  ├── Workspace
  └── Final Report
       │
       ├── ILlmService
       │    ├── OpenAiLlmService
       │    └── AifaLlmService
       │
       └── IExecutionSandbox
            └── RemoteExecutionSandbox
                  ↓ HTTPS
             CodingAgent.Runner
                  ↓
             dotnet restore
             dotnet build
             dotnet test
```

## Deployment

- Agent API: `https://n8n-agent.samanooqazvin.com`
- Runner API: `https://n8n-runner.samanooqazvin.com`
- Agent Swagger: `https://n8n-agent.samanooqazvin.com/swagger`
- Runner Swagger: `https://n8n-runner.samanooqazvin.com/swagger`
- Target Framework: .NET 9

## Main Features

- Planner, Coder, Reviewer and Fixer agents
- Durable human-in-the-loop after planning
- Maximum 3 fix attempts
- Maximum 15-minute run duration
- Physical generated workspaces
- Real code execution through a remote Runner
- Real `dotnet restore`, `dotnet build` and `dotnet test`
- xUnit test generation
- Configurable OpenAI / AIFA provider
- Markdown final report
- Swagger for both APIs
- Importable n8n workflow

## Repository Structure

```text
multi-agent-coding-agent/
├── CodingAgent.sln
├── README.md
├── docs/
│   ├── ADR/
│   ├── SDD/
│   └── Technical/
├── src/
│   ├── CodingAgent.Api/
│   ├── CodingAgent.Application/
│   ├── CodingAgent.Domain/
│   ├── CodingAgent.Infrastructure/
│   └── CodingAgent.Runner/
└── workflows/
    └── 01-main-coding-agent.json
```

## Workspaces

Agent API workspace:

```text
App_Data/workspaces/{executionId:N}/
```

Runner workspace:

```text
RunnerData/workspaces/{executionId:N}/
```

Persistent Runner NuGet cache:

```text
RunnerData/nuget-packages/
```

## LLM Providers

OpenAI:

```json
{
  "AI": { "Provider": "OpenAI" },
  "OpenAI": {
    "BaseUrl": "https://api.openai.com/v1/",
    "Model": "gpt-5.6-sol",
    "ApiKey": ""
  }
}
```

AIFA:

```json
{
  "AI": { "Provider": "Aifa" },
  "Aifa": {
    "BaseUrl": "https://aifa-chatbot.dev.dotin.ir/",
    "Model": "assistance-model",
    "Token": "",
    "UserId": "coding-agent"
  }
}
```

Credentials are configuration-driven and real secrets must not be committed.

## Security

Agent API routes under `/api/*` use:

```http
X-Agent-Api-Key: <secret>
```

Runner endpoints can use:

```http
X-Runner-Api-Key: <secret>
```

Generated code is untrusted. The Windows Runner is an execution runner, not a hardened security sandbox, so it should run with minimal permissions and no production secrets.

## Human-in-the-loop

```text
Start Request
  ↓
Planner
  ↓
WaitingForHuman
  ↓
workflow ends

Human Review Request
  ↓
same executionId resumes
  ↓
Coder
```

Supported decisions: `approve`, `modify`, `reject`.

## Fix Loop

```text
Execute
  ↓
Reviewer
  ↓
nextAction?
 ├── complete → Final Report
 ├── fail     → Final Report
 └── fix
      ↓
    Fixer
      ↓
    Execute
      ↓
    Reviewer
```

## Final Report

The Markdown report contains execution metadata, request, plan, human feedback, generated files, execution attempts, stdout/stderr, reviewer result, fix history and final status.

## Documentation

- `docs/ADR/ADR-001-Architecture-Decisions.md`
- `docs/SDD/Software-Design-Document.md`
- `docs/Technical/Agent-Contracts.md`
- `docs/Technical/API-Design.md`
- `workflows/README.md`
