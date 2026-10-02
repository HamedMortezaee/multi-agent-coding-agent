# Development

## Prerequisites

- .NET 9 SDK
- An OpenAI API key (required in a later phase)
- Access to the deployed ASP.NET Core API from n8n

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

Create a run:

```http
POST /api/v1/runs
Content-Type: application/json

{
  "request": "Create a Todo ASP.NET Core Web API.",
  "requestedBy": "user"
}
```

Get a run:

```http
GET /api/v1/runs/{executionId}
```

## Current Persistence

The current repository is intentionally in-memory for the first bootstrap step. Persistent state storage will be introduced before connecting the complete n8n workflow.

## Current Limitation

Real generated-code execution is not implemented because no sandbox/runner is currently available.
