# Software Design Document
## Multi-Agent Coding Agent

**Version:** 2.0  
**Status:** Implemented / Validated  
**Date:** 2026-10-09

---

# 1. Purpose

The system receives a natural-language software-development request and coordinates multiple agents to plan, generate, execute, review, fix and report the result.

```text
User Request
    ↓
Planner
    ↓
Human Review
    ↓
Coder
    ↓
Real Execution
    ↓
Reviewer
    ↓
Fixer (when needed)
    ↓
Final Report
```

# 2. Scope

The MVP targets small ASP.NET Core Web API projects.

Validated demo scenario:

```text
Todo REST API
.NET 9
Swagger
EF Core InMemory
xUnit integration tests
```

# 3. Main Components

## n8n
Owns orchestration and routing between backend steps.

## CodingAgent.Api
Owns:
- run state and transitions
- Planner, Coder, Reviewer and Fixer services
- human-review decisions
- LLM provider selection
- generated files
- execution history
- retry/timeout rules
- final Markdown report

## CodingAgent.Runner
Executes generated .NET projects using a separate Windows application.

Supported commands:

```text
dotnet restore
dotnet build
dotnet test
```

## LLM Providers

```text
ILlmService
 ├── OpenAiLlmService
 └── AifaLlmService
```

Provider selection is configuration-driven.

# 4. Functional Requirements

## FR-001 — Receive Request
Accept a natural-language development request.

## FR-002 — Planning
Planner produces a structured goal, steps, assumptions and ambiguities.

## FR-003 — Human Review
After planning, a human can:
- approve
- modify
- reject

The state is persisted so the workflow can resume later with the same ExecutionId.

## FR-004 — Code Generation
Coder generates complete text project files using relative safe paths.

Coder is instructed to generate:
- ASP.NET Core project
- solution/project files
- automated xUnit tests

## FR-005 — Real Code Execution
Generated code is sent to CodingAgent.Runner and actually restored, built and tested.

## FR-006 — Review
Reviewer analyzes the real execution result and returns one of:

```text
complete
fix
fail
```

## FR-007 — Fix
Fixer receives current files, latest errors and previous attempts, applies changes and returns the run to execution.

## FR-008 — Retry Limit

```text
MaxFixAttempts = 3
```

## FR-009 — Global Timeout

```text
MaxRunDuration = 15 minutes
```

## FR-010 — Final Report
Every completed or controlled-failure run can produce a Markdown report.

# 5. High-Level Architecture

```text
                       User
                        │
                        ▼
                       n8n
                        │
                        ▼
                CodingAgent.Api
        ┌───────────────┼────────────────┐
        │               │                │
        ▼               ▼                ▼
     Agents          State Store      Workspace
        │
        ├───────────────► ILlmService
        │                  ├─ OpenAI
        │                  └─ AIFA
        │
        └───────────────► IExecutionSandbox
                           │
                           ▼
                  RemoteExecutionSandbox
                           │ HTTPS
                           ▼
                  CodingAgent.Runner
                           │
                           ▼
               restore / build / test
```

# 6. Deployment Architecture

```text
n8n
 │
 │ HTTPS/JSON
 ▼
CodingAgent.Api
https://n8n-agent.samanooqazvin.com
 │
 ├────► LLM Provider
 │
 └────► CodingAgent.Runner
        https://n8n-runner.samanooqazvin.com
             │
             ▼
        .NET SDK execution
```

The Agent API and Runner are separate applications.

# 7. Solution Structure

```text
CodingAgent.sln

src/
 ├── CodingAgent.Api
 ├── CodingAgent.Application
 ├── CodingAgent.Domain
 ├── CodingAgent.Infrastructure
 └── CodingAgent.Runner

workflows/
 └── 01-main-coding-agent.json
```

# 8. Domain State

## AgentRun

```text
ExecutionId
UserRequest
RequestedBy
Status
StartedAt
Deadline
Plan
HumanFeedback
Files
Attempts
FixAttemptCount
```

## Run Status

```text
Created
Planning
WaitingForHuman
Coding
WaitingForExecution
Executing
Reviewing
Fixing
Completed
Failed
TimedOut
```

# 9. Persistence

Run state is persisted through `IAgentRunRepository`.

MVP implementation:

```text
App_Data/agent-runs
```

This survives application restarts but is intended for a single Agent API instance.

# 10. Workspace Design

Agent API workspace:

```text
App_Data/workspaces/{executionId:N}
```

Runner workspace:

```text
RunnerData/workspaces/{executionId:N}
```

Runner NuGet cache:

```text
RunnerData/nuget-packages
```

The Runner re-materializes the supplied file snapshot for each execution.

# 11. LLM Architecture

```text
Planner
Coder
Reviewer
Fixer
   │
   ▼
ILlmService
 ├── OpenAiLlmService
 └── AifaLlmService
```

Agent business logic does not directly depend on an external provider SDK.

# 12. Execution Architecture

```text
Application
    │
    ▼
IExecutionSandbox
    │
    ▼
RemoteExecutionSandbox
    │
    ▼
POST Runner /api/v1/executions
    │
    ▼
dotnet restore/build/test
```

Runner execution results include:

```text
Available
Success
ExitCode
Stdout
Stderr
DurationMs
TimedOut
Reason
```

# 13. Test Target Resolution

For `dotnet test`, Runner:
1. materializes files,
2. resolves a solution or test project,
3. performs a deterministic restore,
4. executes test with `--no-restore`.

If no test target is available, it returns:

```text
TEST_PROJECT_NOT_FOUND
```

# 14. Human-in-the-loop

Human review uses durable state/resume.

```text
Start
 ↓
Planner
 ↓
WaitingForHuman
 ↓
workflow ends

Review request
 ↓
same ExecutionId resumes
 ↓
Coder
```

This avoids keeping a hosted n8n execution open during human wait time.

# 15. Fix Loop

```text
Execute
 ↓
Reviewer
 ↓
nextAction
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

Backend state is the source of truth for fix count and timeout.

# 16. API Surface

Base path:

```text
/api/v1
```

Main endpoints:

```text
POST /runs
GET  /runs/{executionId}
POST /runs/{executionId}/plan
POST /runs/{executionId}/human-review
POST /runs/{executionId}/code
POST /runs/{executionId}/execute
POST /runs/{executionId}/review
POST /runs/{executionId}/fix
GET  /runs/{executionId}/attempts
POST /runs/{executionId}/report
GET  /runs/{executionId}/report
```

Diagnostics:

```text
GET  /diagnostics/llm
POST /diagnostics/llm/test
GET  /diagnostics/runner
POST /diagnostics/runner/execute
```

# 17. Security

Agent API routes under `/api/*` use:

```http
X-Agent-Api-Key: <secret>
```

Runner can use:

```http
X-Runner-Api-Key: <secret>
```

Secrets must come from deployment configuration and must not be committed.

Generated paths reject traversal and absolute paths.

Generated code is untrusted.

The current Windows Runner is an execution runner, not a hardened sandbox.

# 18. Swagger

Agent API:

```text
https://n8n-agent.samanooqazvin.com/swagger
```

Runner API:

```text
https://n8n-runner.samanooqazvin.com/swagger
```

# 19. Reporting

Final report format: Markdown.

Sections include:

```text
Execution Metadata
User Request
Plan
Human Feedback
Generated Project Files
Execution Attempts
Reviewer Result
Fix History
Final Result
```

# 20. Validated End-to-End Run

Validated execution:

```text
ExecutionId: fc30d504-9248-4413-81ee-f0e200a96c4a
Status: Completed
ExitCode: 0
Tests Passed: 3
Tests Failed: 0
Reviewer Success: true
Reviewer NextAction: complete
Fix Attempts: 0
```

The generated Todo API was built and its integration tests executed successfully.

# 21. Definition of Done

```text
User Request             ✓
Planner                  ✓
Human Review             ✓
Coder                    ✓
State Persistence        ✓
Physical Workspace       ✓
Real Code Execution      ✓
dotnet restore           ✓
dotnet build             ✓
dotnet test              ✓
Reviewer                 ✓
Fix Loop                 ✓
Maximum 3 Fix Attempts   ✓
15-Minute Limit          ✓
Final Report             ✓
n8n Workflow             ✓
OpenAI Provider          ✓
AIFA Provider            ✓
Swagger                  ✓
Windows Deployment       ✓
```

# 22. Remaining Hardening

The MVP is operational. Production hardening would include:
- hardened isolated sandbox/container execution
- transactional shared database for multi-instance state
- proper TLS certificate validation everywhere
- secret vault/environment-secret management
- workspace cleanup/retention policy
- richer observability and metrics
