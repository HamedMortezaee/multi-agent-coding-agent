# CodingAgent.Runner

Standalone execution service for generated projects.

## Purpose

The main `CodingAgent.Api` sends the current generated project files to this service. The runner materializes them into an isolated workspace directory and executes an allow-listed .NET command while capturing stdout, stderr, exit code, duration, and timeout state.

Supported commands:

- `dotnet restore`
- `dotnet build`
- `dotnet test`

The n8n workflow currently sends `dotnet test`.

## Configuration

```json
{
  "Runner": {
    "ApiKey": "SET_A_PRIVATE_RUNNER_KEY",
    "WorkspaceRootPath": "RunnerData/workspaces",
    "MaxTimeoutSeconds": 120
  }
}
```

Configure the main API with the runner address:

```json
{
  "Runner": {
    "BaseUrl": "https://runner.example.internal/",
    "ApiKey": "SET_THE_SAME_PRIVATE_RUNNER_KEY"
  }
}
```

When `Runner:BaseUrl` is empty, the main API keeps the existing controlled `EXECUTION_UNAVAILABLE` behavior.

## Run locally

The runner machine needs the .NET 9 SDK.

```text
dotnet run --project src/CodingAgent.Runner/CodingAgent.Runner.csproj
```

Check:

```text
GET /health
```

## Security boundary

Generated projects are untrusted code. Build and test can execute arbitrary MSBuild targets, analyzers, generators, and test code.

Do **not** deploy this runner inside the production web host or with privileged credentials. Use a dedicated disposable VM/container or otherwise strongly isolated machine with:

- a low-privilege OS identity;
- no production secrets;
- restricted outbound network access;
- CPU/memory/process limits at the hosting layer;
- a short execution timeout;
- workspace cleanup/retention policy;
- an API key and private network/TLS where possible.

This service provides process control and command allow-listing, but by itself it is **not a security sandbox**.
