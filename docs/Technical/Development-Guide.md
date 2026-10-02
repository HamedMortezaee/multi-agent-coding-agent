# Development

## Prerequisites

- .NET 9 SDK
- OpenAI API key
- Access to the deployed ASP.NET Core API from n8n

## Configure OpenAI

For the current temporary test phase, the OpenAI key is hardcoded in:

```text
src/CodingAgent.Api/Configuration/TemporarySecrets.cs
```

Replace:

```csharp
public const string OpenAiApiKey = "CHANGE_ME_OPENAI_API_KEY";
```

with your current test key before publishing.

This is temporary and must be moved back to a secure secret store/environment variable before any real deployment.

The model name is configuration-driven:

```json
{
  "AI": {
    "Provider": "OpenAI",
    "Model": "luna-5.6-gpt"
  }
}
```

> The exact required model identifier must be available to the configured OpenAI account/provider before end-to-end testing.

## Run locally

```bash
dotnet restore CodingAgent.sln
dotnet build CodingAgent.sln
dotnet run --project src/CodingAgent.Api/CodingAgent.Api.csproj
```

Health endpoint:

```http
GET /health
```

---

## Step 1 — Create a Run

```http
POST /api/v1/runs
Content-Type: application/json

{
  "request": "Create a Todo ASP.NET Core Web API.",
  "requestedBy": "user"
}
```

Save the returned `executionId`.

---

## Step 2 — Generate the Plan

```http
POST /api/v1/runs/{executionId}/plan
Content-Type: application/json

{}
```

Current MVP transition:

```text
Created
  ↓
Planning
  ↓
WaitingForHuman
```

---

## Step 3 — Human Review

### Approve

```http
POST /api/v1/runs/{executionId}/human-review
Content-Type: application/json

{
  "decision": "approve",
  "feedback": "Add Swagger documentation."
}
```

Transition:

```text
WaitingForHuman → Coding
```

### Request Plan Modification

```json
{
  "decision": "modify",
  "feedback": "Keep persistence in-memory for the MVP."
}
```

Transition:

```text
WaitingForHuman → Planning
```

After this, call the Planner endpoint again. The stored human feedback is available in run state.

### Reject

```json
{
  "decision": "reject",
  "feedback": "Scope rejected."
}
```

Transition:

```text
WaitingForHuman → Failed
```

---

## Step 4 — Generate Code

After approval:

```http
POST /api/v1/runs/{executionId}/code
Content-Type: application/json

{}
```

Coder receives:

- original user request
- approved plan
- human feedback

and returns structured project files.

Transition:

```text
Coding
  ↓
WaitingForExecution
```

The files are also stored in the current run state.

---

## Get Run State

```http
GET /api/v1/runs/{executionId}
```

Current state response includes:

- request
- status
- plan
- human feedback
- generated files
- fix attempt count
- deadline

---

## Current End-to-End Backend Flow

```text
POST /runs
    ↓
POST /plan
    ↓
WaitingForHuman
    ↓
POST /human-review
    ↓
Coding
    ↓
POST /code
    ↓
WaitingForExecution
```

## Step 5 — Execute

```http
POST /api/v1/runs/{executionId}/execute
Content-Type: application/json

{
  "command": "dotnet test",
  "timeoutSeconds": 60
}
```

The execution contract is now implemented.

Because no sandbox/runner is currently configured, the endpoint intentionally returns:

```text
HTTP 503
EXECUTION_UNAVAILABLE
```

The failed infrastructure attempt is still recorded in run state and the run moves to `Reviewing`.

---

## Step 6 — Review

```http
POST /api/v1/runs/{executionId}/review
Content-Type: application/json

{}
```

Current behavior without a runner:

```text
Reviewing
  ↓
ExecutionUnavailable
  ↓
Failed
```

This is intentional: an infrastructure blocker must not be treated as a code bug.

When a real runner is added and execution produces build/test results, Reviewer can choose:

```text
complete
fix
fail
```

---

## Step 7 — Fix

When Reviewer returns `nextAction = fix`:

```http
POST /api/v1/runs/{executionId}/fix
Content-Type: application/json

{}
```

Fixer receives:

- approved plan
- human feedback
- current files
- latest real execution result
- latest review
- previous attempts

It applies structured create/update/delete changes and returns the run to:

```text
WaitingForExecution
```

Maximum fix attempts:

```text
3
```

After each fix the flow is:

```text
Fix
 ↓
Execute
 ↓
Review
 ↓
Fix (if required)
```

---

## Attempt History

```http
GET /api/v1/runs/{executionId}/attempts
```

This returns execution results, reviews and fix summaries for previous attempts.

---

## Current End-to-End Backend Flow

```text
Create
 ↓
Plan
 ↓
Human Review
 ↓
Code
 ↓
Execute
 ↓
Review
 ├── Complete
 ├── Failed
 └── Fix
      ↓
    Execute
```

Without a runner, the current flow ends as a controlled `Failed` state after Reviewer identifies `EXECUTION_UNAVAILABLE`.

## Current Persistence

The current repository is intentionally in-memory for the bootstrap phase.

Important: restarting the ASP.NET Core application currently clears all run state.

Persistent state storage must be added before production-like n8n testing.

## Current Execution Limitation

Real generated-code execution is not implemented because no sandbox/runner is currently available.


---

## Configure Backend API Key

All `/api/*` endpoints require:

```http
X-Agent-Api-Key: <secret>
```

For the current temporary test phase, the backend key is hardcoded in:

```text
src/CodingAgent.Api/Configuration/TemporarySecrets.cs
```

Replace:

```csharp
public const string AgentApiKey = "CHANGE_ME_AGENT_API_KEY";
```

with a temporary test value.

Then set the n8n variable:

```text
CODING_AGENT_API_KEY
```

to exactly the same value.

Do not commit a real production secret. This hardcoded mode is only for short-lived testing.

The `/health` endpoint remains public.

---

## Durable Run State

Run state is now persisted as JSON under:

```text
App_Data/agent-runs
```

Each execution is stored separately by `ExecutionId`.

Example:

```text
App_Data/
└── agent-runs/
    └── 49fb20b78b644e26aaf978d99215b419.json
```

State survives an ASP.NET Core application restart.

The Windows hosting process must have write permission to the configured persistence directory.

The path can be changed using:

```json
{
  "Persistence": {
    "RootPath": "App_Data/agent-runs"
  }
}
```

The repository writes through a temporary file and replaces the target file, reducing the chance of leaving a partially-written state file.

### Hosting Constraint

If the Windows host does not allow file writes, replace `JsonFileAgentRunRepository` with another `IAgentRunRepository` implementation such as SQL Server/PostgreSQL without changing Agent services or n8n contracts.
