# Architecture Decision Records
## Multi-Agent Coding Agent

**Version:** 2.0  
**Status:** Accepted / Implemented  
**Date:** 2026-10-09

---

# ADR-001 — Hybrid Architecture with n8n and ASP.NET Core

## Status
Accepted

## Decision
Use n8n for orchestration and ASP.NET Core for stateful backend responsibilities.

```text
User
 ↓
n8n
 ↓
CodingAgent.Api
 ├── Agents
 ├── State
 ├── Workspace
 ├── Reporting
 ├── ILlmService
 └── IExecutionSandbox
```

n8n routes steps; backend state is the source of truth.

---

# ADR-002 — Durable Human-in-the-loop

## Status
Accepted

## Decision
Human review occurs after planning using durable state/resume rather than a long-running n8n Wait node.

```text
Planner
 ↓
WaitingForHuman
 ↓
workflow ends

Human review
 ↓
same ExecutionId resumes
```

---

# ADR-003 — ASP.NET Core / C# / .NET 9

## Status
Accepted

## Decision
Implement backend services and Runner using ASP.NET Core and C# targeting .NET 9.

Solution structure:

```text
src/
 ├── CodingAgent.Api
 ├── CodingAgent.Application
 ├── CodingAgent.Domain
 ├── CodingAgent.Infrastructure
 └── CodingAgent.Runner
```

---

# ADR-004 — Separate Agent API and Runner

## Status
Accepted / Implemented

## Context
The system requires actual execution of generated code.

## Decision
Keep orchestration/business logic in CodingAgent.Api and execute generated code in a separate CodingAgent.Runner application.

```text
CodingAgent.Api
    │
    ▼
IExecutionSandbox
    │
    ▼
RemoteExecutionSandbox
    │ HTTPS
    ▼
CodingAgent.Runner
```

This separation reduces coupling and allows replacing the Runner later.

---

# ADR-005 — Execution Abstraction

## Status
Accepted / Implemented

## Decision
Code execution must remain behind:

```csharp
public interface IExecutionSandbox
{
    Task<ExecutionResult> ExecuteAsync(
        Guid executionId,
        IReadOnlyCollection<ProjectFile> files,
        string command,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);
}
```

Concrete implementations:

```text
UnavailableExecutionSandbox
RemoteExecutionSandbox
```

The current production-like MVP uses `RemoteExecutionSandbox`.

---

# ADR-006 — Configurable LLM Provider

## Status
Accepted / Implemented

## Decision
Agent business logic depends only on `ILlmService`.

```text
ILlmService
 ├── OpenAiLlmService
 └── AifaLlmService
```

Provider selection is configuration-driven.

OpenAI config:

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

AIFA config:

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

---

# ADR-007 — Agent Responsibility Separation

## Status
Accepted

### Planner
- understand requirement
- produce implementation plan
- identify assumptions and ambiguities

### Coder
- generate complete project files
- generate automated xUnit tests
- follow approved plan and human feedback

### Reviewer
- inspect actual execution result
- classify issues
- return `complete`, `fix` or `fail`

### Fixer
- inspect current files, errors and previous attempts
- apply corrective changes
- avoid repeating failed fixes

---

# ADR-008 — Persist State Between Steps

## Status
Accepted / Implemented

## Decision
Persist state through `IAgentRunRepository`.

MVP storage:

```text
App_Data/agent-runs
```

Persisted data includes request, plan, human feedback, files, attempts, review results, fix count and deadline.

---

# ADR-009 — Physical Workspace per ExecutionId

## Status
Accepted / Implemented

Agent API workspace:

```text
App_Data/workspaces/{executionId:N}
```

Runner workspace:

```text
RunnerData/workspaces/{executionId:N}
```

All generated file paths are validated as relative paths and path traversal is rejected.

---

# ADR-010 — Maximum Three Fix Attempts

## Status
Accepted / Implemented

```text
MaxFixAttempts = 3
```

The backend owns the counter.

---

# ADR-011 — Maximum Run Duration

## Status
Accepted / Implemented

```text
MaxRunDuration = 15 minutes
```

Each run records StartedAt and Deadline.

---

# ADR-012 — Structured Agent Output

## Status
Accepted

Planner, Coder, Reviewer and Fixer outputs are machine-readable JSON and validated before use.

---

# ADR-013 — n8n and Backend Communicate Through HTTP APIs

## Status
Accepted / Implemented

No shared local filesystem between n8n and backend is assumed.

Communication is through HTTPS/JSON APIs.

---

# ADR-014 — Deterministic Runner Command Allowlist

## Status
Accepted / Implemented

Runner accepts only:

```text
dotnet restore
dotnet build
dotnet test
```

Arbitrary shell commands are not accepted by the public execution contract.

---

# ADR-015 — Test Target Resolution

## Status
Accepted / Implemented

Before `dotnet test`, Runner resolves a solution or test project.

If no target exists:

```text
TEST_PROJECT_NOT_FOUND
```

For tests, Runner restores first and then executes `dotnet test --no-restore`.

---

# ADR-016 — Persistent NuGet Cache

## Status
Accepted / Implemented

NuGet packages are stored outside per-execution workspaces:

```text
RunnerData/nuget-packages
```

Reason: execution workspaces are recreated, so package cache must not live inside a disposable workspace.

---

# ADR-017 — Controlled Failure and Final Report

## Status
Accepted / Implemented

Failed, timed-out and successful runs can produce a final Markdown report.

Report includes:
- request
- plan
- human feedback
- generated files
- execution attempts
- stdout/stderr
- reviewer result
- fix history
- final status

---

# ADR-018 — API Key Authentication

## Status
Accepted / Implemented

Agent API:

```http
X-Agent-Api-Key: <secret>
```

Runner:

```http
X-Runner-Api-Key: <secret>
```

Credentials are read from deployment configuration.

---

# ADR-019 — Swagger for Both APIs

## Status
Accepted / Implemented

Swagger is exposed for both applications:

```text
https://n8n-agent.samanooqazvin.com/swagger
https://n8n-runner.samanooqazvin.com/swagger
```

Swagger defines the relevant API-key headers.

---

# ADR-020 — Runner is Not a Hardened Sandbox

## Status
Accepted

## Context
Generated code is untrusted and build/test execution may execute code.

## Decision
The current Windows Runner is acceptable for the course MVP but must not be described as a hardened sandbox.

Operational controls:
- separate application/site
- low-privilege app-pool identity
- no production secrets
- writable access limited to Runner data
- command allowlist
- timeout
- controlled workspace

A stronger production design would use an isolated container/VM/sandbox.

---

# ADR-021 — GitHub Automation is Post-MVP

## Status
Accepted

Automated branch/commit/PR publication is optional and outside the core validated flow.

---

# Current Architecture Baseline

```text
Orchestration: n8n
Backend: CodingAgent.Api / ASP.NET Core
Runner: CodingAgent.Runner / ASP.NET Core
Language: C#
Target: .NET 9
LLM: OpenAI or AIFA
State: JSON file-backed repository
Human-in-the-loop: durable state/resume
Execution: RemoteExecutionSandbox
Real restore/build/test: implemented
Max Fix Attempts: 3
Max Run Time: 15 minutes
Report: Markdown
Swagger: Agent + Runner
MVP Target: ASP.NET Core Todo API
```

# Validation Evidence

Validated run:

```text
ExecutionId: fc30d504-9248-4413-81ee-f0e200a96c4a
Status: Completed
Build: successful
Tests Passed: 3
Tests Failed: 0
Reviewer nextAction: complete
Fix Attempts: 0
```
