# Development

## Prerequisites

- .NET 9 SDK
- OpenAI API key
- Access to the deployed ASP.NET Core API from n8n

## Configure OpenAI

Set the API key as an environment variable.

PowerShell:

```powershell
$env:OPENAI_API_KEY="your-api-key"
```

Do not store the API key in `appsettings.json` or commit it to GitHub.

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

The next implementation phase will add the execution contract endpoint, Tester/Reviewer and Fixer loop.

## Current Persistence

The current repository is intentionally in-memory for the bootstrap phase.

Important: restarting the ASP.NET Core application currently clears all run state.

Persistent state storage must be added before production-like n8n testing.

## Current Execution Limitation

Real generated-code execution is not implemented because no sandbox/runner is currently available.
