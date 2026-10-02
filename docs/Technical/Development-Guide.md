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

## Create a Run

```http
POST /api/v1/runs
Content-Type: application/json

{
  "request": "Create a Todo ASP.NET Core Web API.",
  "requestedBy": "user"
}
```

The response contains `executionId`.

## Generate the Plan

```http
POST /api/v1/runs/{executionId}/plan
Content-Type: application/json

{}
```

The Planner calls OpenAI, validates the returned JSON, stores the resulting plan in the run state, and transitions the run to:

```text
WaitingForHuman
```

for the current MVP.

## Get Run State

```http
GET /api/v1/runs/{executionId}
```

The response includes the current status and generated plan when available.

## Current Persistence

The current repository is intentionally in-memory for the bootstrap phase. Persistent state storage will be introduced before the complete n8n workflow is connected.

## Current Execution Limitation

Real generated-code execution is not implemented because no sandbox/runner is currently available.
