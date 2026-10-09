# Final Architecture Overview

## Multi-Agent Coding Agent

### End-to-End Flow

```text
User Request
    ↓
n8n Webhook
    ↓
Create Run
    ↓
Planner Agent
    ↓
WaitingForHuman
    ↓
Human Review
    ↓
Coder Agent
    ↓
Generated ASP.NET Core Project + xUnit Tests
    ↓
CodingAgent.Api
    ↓
RemoteExecutionSandbox
    ↓
CodingAgent.Runner
    ↓
dotnet restore
dotnet build
dotnet test
    ↓
Reviewer Agent
    ↓
┌───────────────┬───────────────┐
│ complete      │ fix           │
│               ↓               │
│          Fixer Agent          │
│               ↓               │
│          Execute Again        │
└───────────────┴───────────────┘
    ↓
Final Report
```

## Components

### n8n
Owns orchestration and branch routing. It starts runs, pauses at the human-review boundary, resumes the same execution, routes reviewer decisions and requests the final report.

### CodingAgent.Api
Owns agent state, business rules, LLM calls, generated files, retry limits, deadlines and final reporting.

### CodingAgent.Runner
Executes generated .NET projects on a separate Windows application. It materializes the received project files, restores packages, builds and runs tests.

### LLM Abstraction
`ILlmService` keeps agent logic independent from the provider.

```text
ILlmService
 ├── OpenAiLlmService
 └── AifaLlmService
```

### Execution Abstraction
`IExecutionSandbox` keeps application logic independent from the concrete runner.

```text
IExecutionSandbox
 ├── UnavailableExecutionSandbox
 └── RemoteExecutionSandbox
```

## Agent Responsibilities

### Planner
Transforms the natural-language request into a structured implementation plan and identifies assumptions and ambiguities.

### Coder
Generates a complete ASP.NET Core project with required project files and automated xUnit tests.

### Reviewer
Analyzes actual execution output. It decides `complete`, `fix` or `fail`.

### Fixer
Uses the latest execution and review errors to modify the generated project. A maximum of three fix attempts is enforced by the backend.

## Human-in-the-loop

Human review happens after planning. The run is persisted with status `WaitingForHuman`; n8n does not need to keep a workflow execution open. A later review request resumes the same execution ID.

## Persistence

Agent runs are persisted as JSON files under:

```text
App_Data/agent-runs
```

Generated API-side workspaces are materialized under:

```text
App_Data/workspaces/{executionId:N}
```

Runner workspaces are created under:

```text
RunnerData/workspaces/{executionId:N}
```

## Execution Controls

- Allowed commands: `dotnet restore`, `dotnet build`, `dotnet test`
- Maximum fix attempts: 3
- Maximum run duration: 15 minutes
- Test projects are resolved before `dotnet test`
- NuGet packages are stored in a persistent Runner cache

## Validated Result

The final validated Todo API run completed successfully:

```text
Status: Completed
Build: successful
Tests: 3 passed, 0 failed
Reviewer: success
nextAction: complete
Fix attempts: 0
```

## Security Note

The Runner executes generated code and therefore must be treated as an execution environment for untrusted code. The current Windows Runner is suitable for the course MVP but is not a hardened sandbox. It should run under a low-privilege identity and contain no production secrets.
