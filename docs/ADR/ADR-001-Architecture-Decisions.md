# Architecture Decision Records
## Multi-Agent Coding Agent

**Version:** 1.0  
**Status:** Accepted / In Progress  
**Date:** 2026-10-03

---

# ADR-001 — Hybrid Architecture with n8n and ASP.NET Core

## Status
Accepted

## Context
The system is a multi-agent coding assistant. The project requires distinct responsibilities for Planner, Coder, Tester/Reviewer and Fixer.

## Decision
Use a hybrid architecture:

```text
User
  ↓
n8n
  ├── Planner
  ├── Human-in-the-loop
  ├── Coder
  ├── Tester / Reviewer
  ├── Fixer
  └── Final Report
  ↓
ASP.NET Core API
  ├── OpenAI Integration
  ├── State Management
  ├── Workspace Management
  ├── File Management
  └── Execution Abstraction
```

n8n is responsible for workflow orchestration. ASP.NET Core provides backend APIs, state, OpenAI integration and project/workspace services.

---

# ADR-002 — n8nir.ir as n8n Runtime

## Status
Accepted

## Decision
Use `n8nir.ir` as the n8n execution environment.

All workflows will be delivered as importable JSON files.

Secrets and environment-specific values must not be hard-coded into workflow JSON.

---

# ADR-003 — ASP.NET Core and C#

## Status
Accepted

## Decision
Implement the backend using ASP.NET Core and C#.

Proposed solution structure:

```text
CodingAgent.sln

src/
 ├── CodingAgent.Api
 ├── CodingAgent.Application
 ├── CodingAgent.Domain
 └── CodingAgent.Infrastructure

tests/
 ├── CodingAgent.UnitTests
 └── CodingAgent.IntegrationTests
```

---

# ADR-004 — Windows Hosting is API-only

## Status
Accepted

## Context
The available Windows host can run the published ASP.NET Core application and expose HTTP APIs, but does not provide Docker, shell execution, Process.Start, dotnet build or dotnet test.

## Decision
Use Windows Hosting only for ASP.NET Core API hosting.

No business logic may depend on arbitrary process execution on this host.

---

# ADR-005 — Execution Sandbox is an unresolved dependency

## Status
Open / Blocked

## Context
The project requires actual execution of generated code, but currently no Docker host, sandbox, VPS or remote code runner is available.

## Decision
Keep code execution behind this abstraction:

```csharp
public interface IExecutionSandbox
{
    Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default);
}
```

Current concrete implementation: **None**.

This is the primary blocker for full compliance with the project requirement for real code execution.

---

# ADR-006 — OpenAI as LLM Provider

## Status
Accepted

## Decision
OpenAI will be the LLM provider.

Agent business logic must depend on an abstraction:

```csharp
public interface ILlmService
{
    Task<LlmResponse> GenerateAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default);
}
```

The model name must be configuration-driven:

```json
{
  "AI": {
    "Provider": "OpenAI",
    "Model": "luna-5.6-gpt"
  }
}
```

---

# ADR-007 — Agent Responsibility Separation

## Status
Accepted

### Planner
- Understand requirement
- Break requirement into steps
- Identify ambiguities
- Generate implementation plan
- Decide whether human review is required

### Coder
- Generate project structure
- Generate files
- Apply approved plan
- Modify files

### Tester / Reviewer
- Analyze execution result
- Analyze compilation/runtime/test errors
- Determine success/failure

### Fixer
- Analyze current and previous failures
- Create corrective changes
- Avoid repeating unsuccessful fixes

---

# ADR-008 — Human-in-the-loop after Planning

## Status
Accepted

## Decision

```text
Planner
   ↓
Human Review
   ↓
Coder
```

Human reviewer can:
- Approve
- Reject
- Modify
- Add feedback

Future enhancement: smart human stop only when ambiguity exists.

---

# ADR-009 — Persist State Between Steps

## Status
Accepted

State must include at least:

```text
ExecutionId
User Request
Plan
Human Feedback
Generated Files
Code Versions
Execution Attempts
Previous Errors
Fix Attempt Count
Final Result
```

State will be managed by the ASP.NET Core backend.

---

# ADR-010 — Maximum Three Fix Attempts

## Status
Accepted

```text
MaxFixAttempts = 3
```

After the third failed correction, the system must stop and generate a controlled failure report.

---

# ADR-011 — Maximum Run Duration

## Status
Accepted

```text
MaxRunDuration = 15 minutes
```

Each run must track StartedAt and Deadline.

---

# ADR-012 — Structured Agent Output

## Status
Accepted

Agents must return machine-readable structured output.

Planner example:

```json
{
  "goal": "Create Todo API",
  "steps": [],
  "ambiguities": [],
  "requiresHumanReview": true
}
```

Coder example:

```json
{
  "files": [
    {
      "path": "Program.cs",
      "content": "..."
    }
  ]
}
```

---

# ADR-013 — Workspace Isolation by ExecutionId

## Status
Accepted

Every run receives a unique `ExecutionId`.

Logical workspace:

```text
/workspaces/{executionId}
```

Files, attempts, errors and reports belong to that run.

---

# ADR-014 — n8n and Backend communicate through HTTP APIs

## Status
Accepted

n8n and ASP.NET Core run in different environments.

No shared local filesystem is assumed.

All communication is through HTTPS/JSON APIs.

---

# ADR-015 — Controlled Failure

## Status
Accepted

A failed run must still produce a report containing:
- Completed steps
- Generated files
- Execution attempts
- Errors
- Fix attempts
- Failure reason

---

# ADR-016 — Markdown Report

## Status
Accepted

Use Markdown as the initial report format.

Default report name:

```text
execution-report.md
```

---

# ADR-017 — GitHub Integration is post-MVP

## Status
Accepted

GitHub branch/commit/PR automation is valuable but not required for the first MVP.

It will be implemented after the core agent flow works.

---

# ADR-018 — MVP Scope: ASP.NET Core projects

## Status
Accepted

The initial agent supports small ASP.NET Core Web API projects only.

Primary demo target:

```text
Todo REST API
```

---

# Current Architecture Baseline

```text
n8n Runtime: n8nir.ir
Backend: ASP.NET Core
Language: C#
LLM: OpenAI
Hosting: Windows Hosting
Workflow Delivery: Importable n8n JSON
State: ASP.NET Core backend
Human-in-the-loop: n8n
Execution Sandbox: Not currently available
Max Fix Attempts: 3
Max Run Time: 15 minutes
Report: Markdown
MVP Target: ASP.NET Core Todo API
```


---

# ADR-019 — File-backed State Persistence for MVP

## Status
Accepted

## Context
The Windows host can run the ASP.NET Core API, but no external database has been selected yet. In-memory state would be lost whenever the application restarts.

## Decision
Use a JSON file-backed implementation of `IAgentRunRepository` for the MVP.

Default path:

```text
App_Data/agent-runs
```

Each run is persisted by `ExecutionId`.

Application services explicitly call `SaveAsync` after state transitions.

## Consequences

### Positive
- survives application restarts
- no external database dependency
- works with current Windows-host deployment model
- repository abstraction allows later migration to SQL Server/PostgreSQL

### Negative
- Windows host must allow write access to the persistence directory
- intended for a single application instance
- concurrent updates use process-local locking rather than distributed transactions

---

# ADR-020 — API Key Authentication between n8n and Backend

## Status
Accepted

## Context
The ASP.NET Core endpoints will be reachable over the internet from n8nir.ir and must not be callable anonymously.

## Decision
Protect all `/api/*` endpoints using:

```http
X-Agent-Api-Key
```

The secret is read primarily from:

```text
AGENT_API_KEY
```

The health endpoint remains anonymous.

The API key is never committed to GitHub or embedded in exported n8n workflow JSON.

## Future
The API-key mechanism can later be replaced with stronger service-to-service authentication without changing Agent domain logic.
