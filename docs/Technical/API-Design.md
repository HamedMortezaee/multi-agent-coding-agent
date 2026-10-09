# API Design
## Multi-Agent Coding Agent Backend

**Version:** 1.0  
**Base Prefix:** `/api/v1`  
**Transport:** HTTPS + JSON

---

# 1. Authentication

n8n requests to the ASP.NET Core backend use an API key header for the MVP.

```http
X-Agent-Api-Key: <secret>
```

The API key must be stored as a credential/secret and must never be committed to GitHub or embedded in exported n8n workflow JSON.

---

# 2. Common HTTP Rules

Content type:

```http
Content-Type: application/json
```

Recommended status codes:

| Code | Usage |
|---:|---|
| 200 | Successful read/action |
| 201 | Run created |
| 400 | Invalid request |
| 401 | Missing/invalid API key |
| 404 | Run not found |
| 409 | Invalid run state/conflict |
| 422 | Structured validation failure |
| 429 | Rate limit/retry later |
| 500 | Unexpected backend error |
| 503 | External dependency unavailable |

---

# 3. Create Run

```http
POST /api/v1/runs
```

Request:

```json
{
  "request": "Create a Todo ASP.NET Core Web API.",
  "requestedBy": "user"
}
```

Response:

```json
{
  "success": true,
  "data": {
    "executionId": "49fb20b7-8b64-4e26-aaf9-78d99215b419",
    "status": "Created",
    "startedAt": "2026-10-03T12:30:00Z",
    "deadline": "2026-10-03T12:45:00Z"
  },
  "errors": []
}
```

---

# 4. Get Run State

```http
GET /api/v1/runs/{executionId}
```

Response data:

```json
{
  "executionId": "...",
  "status": "Planning",
  "userRequest": "...",
  "fixAttemptCount": 0,
  "startedAt": "...",
  "deadline": "..."
}
```

---

# 5. Generate Plan

```http
POST /api/v1/runs/{executionId}/plan
```

Request:

```json
{}
```

The backend loads the original user request from state.

Response data:

```json
{
  "goal": "...",
  "summary": "...",
  "steps": [],
  "assumptions": [],
  "ambiguities": [],
  "requiresHumanReview": true,
  "humanReviewReason": "Mandatory MVP approval step"
}
```

State transition:

```text
Created → Planning → WaitingForHuman
```

or for future smart-stop mode:

```text
Created → Planning → Coding
```

---

# 6. Submit Human Review

```http
POST /api/v1/runs/{executionId}/human-review
```

Request:

```json
{
  "decision": "approve",
  "feedback": "Add Swagger documentation."
}
```

Allowed decisions:

```text
approve
modify
reject
```

Behavior:

### approve
Stores optional feedback and transitions the run to `Coding`.

### modify
Requires non-empty feedback and transitions the run back to `Planning`.
The Planner endpoint can then be called again.

### reject
Stores optional feedback and transitions the run to `Failed`.

---

# 7. Generate Code

```http
POST /api/v1/runs/{executionId}/code
```

Request:

```json
{}
```

Backend loads:

- original request
- approved plan
- human feedback
- current files

Response data:

```json
{
  "projectName": "Todo.Api",
  "files": [
    {
      "path": "Todo.Api/Program.cs",
      "content": "...",
      "operation": "create",
      "version": 1
    }
  ],
  "notes": []
}
```

State transition:

```text
WaitingForHuman → Coding → WaitingForExecution
```

The generated files are stored in the current run state and are visible from `GET /api/v1/runs/{executionId}`.

---

# 8. Save / Apply File Changes

Normally code generation persists files automatically.

A separate internal/administrative endpoint can be used when needed:

```http
POST /api/v1/runs/{executionId}/files/apply
```

Request:

```json
{
  "changes": [
    {
      "path": "Todo.Api/Program.cs",
      "content": "...",
      "operation": "update"
    }
  ]
}
```

Validation:

- relative paths only
- reject path traversal
- reject duplicate path operations
- reject unsupported binary data in MVP

---

# 9. List Project Files

```http
GET /api/v1/runs/{executionId}/files
```

Response data:

```json
{
  "files": [
    {
      "path": "Todo.Api/Program.cs",
      "version": 2
    }
  ]
}
```

---

# 10. Execute Project

```http
POST /api/v1/runs/{executionId}/execute
```

Request:

```json
{
  "command": "dotnet test",
  "timeoutSeconds": 60
}
```

Execution is delegated to the configured Runner. Example successful response:

```json
{
  "success": true,
  "data": {
    "available": true,
    "success": true,
    "exitCode": 0,
    "stdout": "Passed! - Failed: 0, Passed: 3",
    "stderr": "",
    "durationMs": 13292,
    "timedOut": false,
    "reason": null
  },
  "errors": []
}
```

When a runner is added, the endpoint contract remains unchanged.

---

# 11. Review Result

```http
POST /api/v1/runs/{executionId}/review
```

Request:

```json
{}
```

The backend loads the latest execution result.

Response data:

```json
{
  "success": false,
  "issues": [],
  "summary": "...",
  "nextAction": "fix"
}
```

Allowed nextAction:

```text
complete
fix
fail
```

---

# 12. Apply Fix

```http
POST /api/v1/runs/{executionId}/fix
```

Request:

```json
{}
```

Preconditions:

- latest review requests a fix
- `fixAttemptCount < 3`
- run has not timed out

Response data:

```json
{
  "fixAttemptNumber": 1,
  "changes": [
    {
      "path": "Todo.Api/Program.cs",
      "operation": "update",
      "content": "...",
      "reason": "..."
    }
  ],
  "summary": "..."
}
```

State transition:

```text
Reviewing → Fixing → WaitingForExecution
```

---

# 13. Get Attempt History

```http
GET /api/v1/runs/{executionId}/attempts
```

Response:

```json
{
  "attempts": [
    {
      "attemptNumber": 1,
      "executionResult": {},
      "review": {},
      "fixSummary": null
    }
  ]
}
```

---

# 14. Generate Final Report

```http
POST /api/v1/runs/{executionId}/report
```

Response data:

```json
{
  "format": "markdown",
  "content": "# Coding Agent Report\n...",
  "fileName": "execution-report.md"
}
```

---

# 15. Get Final Report

```http
GET /api/v1/runs/{executionId}/report
```

Response may return:

- JSON containing Markdown text, or
- `text/markdown`

For the MVP, JSON is preferred for simpler n8n processing.

---

# 16. Mark Controlled Failure

Normally the backend derives this automatically.

Internal behavior:

```text
if fixAttemptCount >= 3:
    Status = Failed
    FailureCode = MAX_FIX_ATTEMPTS_REACHED
```

Similarly:

```text
if UtcNow >= Deadline:
    Status = TimedOut
    FailureCode = TIMEOUT
```

---

# 17. State Transition Rules

```text
Created
  ↓
Planning
  ↓
WaitingForHuman
  ↓
Coding
  ↓
WaitingForExecution
  ↓
Executing
  ↓
Reviewing
  ├── Completed
  ├── Failed
  └── Fixing
        ↓
  WaitingForExecution
```

Invalid transitions must return:

```http
409 Conflict
```

Example:

Calling `/fix` while the run is still `Planning` is invalid.

---

# 18. n8n Usage Mapping

Recommended n8n HTTP Request sequence:

```text
POST /runs
 ↓
POST /runs/{id}/plan
 ↓
Human Review
 ↓
POST /runs/{id}/human-review
 ↓
POST /runs/{id}/code
 ↓
POST /runs/{id}/execute
 ↓
POST /runs/{id}/review
 ↓
IF nextAction
 ├── complete → report
 ├── fix → fix → execute → review
 └── fail → report
```

---

# 19. Retry Logic

n8n should not implement retry counters independently from the backend.

Backend is the source of truth for:

```text
fixAttemptCount
maxFixAttempts
deadline
status
```

n8n only routes based on returned state.

---

# 20. Idempotency Guidance

Mutation endpoints should be designed to tolerate accidental duplicate calls where practical.

Future enhancement:

```http
Idempotency-Key: <uuid>
```

Especially useful for:

- code generation
- fix application
- report generation

---

# 21. OpenAPI

Both ASP.NET Core applications expose OpenAPI/Swagger.

Implemented endpoints:

```text
Agent:  https://n8n-agent.samanooqazvin.com/swagger
Runner: https://n8n-runner.samanooqazvin.com/swagger
```

This will also make it easier to validate requests manually before wiring them into n8n.

---

# 22. API Security Baseline

Minimum requirements:

- HTTPS only
- API key authentication for n8n
- OpenAI key stored server-side
- request size limits
- input validation
- path traversal prevention
- structured logging
- no secret values in logs

---

# 23. Future API Additions

Possible later endpoints:

```text
POST /api/v1/runs/{id}/github/publish
GET  /api/v1/runs/{id}/events
GET  /api/v1/runs/{id}/artifacts
POST /api/v1/runs/{id}/cancel
```

These are intentionally excluded from the MVP.


---

# 24. Implemented Execution Behavior

The execution endpoint delegates the current project snapshot to `RemoteExecutionSandbox`, which calls `CodingAgent.Runner`.

```text
WaitingForExecution
  ↓
POST /execute
  ↓
Executing
  ↓
Runner materializes files
  ↓
restore / build / test
  ↓
ExecutionAttempt recorded
  ↓
Reviewing
```

The Runner returns structured execution data including availability, exit code, stdout, stderr, duration, timeout state and reason.

A validated execution completed with:

```text
available = true
success = true
exitCode = 0
tests passed = 3
tests failed = 0
```

---

# 25. Reviewer Behavior

Reviewer analyzes the latest real execution result.

Typical success response:

```json
{
  "success": true,
  "issues": [],
  "summary": "The solution built successfully and all 3 integration tests passed.",
  "nextAction": "complete"
}
```

For a code/test failure, Reviewer may return `fix`. For infrastructure failure it may return `fail` rather than sending an unrelated source-code fix.

---

# 26. Fix Retry Enforcement

Backend owns:

```text
MaxFixAttempts = 3
```

After a fix:

```text
Fixing → WaitingForExecution → Executing → Reviewing
```

A new execution/review cycle is required before another fix.

---

# 27. API Key Enforcement

All Agent API routes under:

```text
/api/*
```

require:

```http
X-Agent-Api-Key: <secret>
```

The expected value is read from:

```text
Security:ApiKey
```

Runner endpoints may require:

```http
X-Runner-Api-Key: <secret>
```

from `Runner:ApiKey`.

Health endpoints remain public.

---

# 28. Durable Persistence

The MVP uses a file-backed implementation of `IAgentRunRepository`.

Default location:

```text
App_Data/agent-runs
```

Persisted state includes run status, request, plan, human feedback, generated files, execution attempts, review results, fix summaries, retry count and deadline.

This is intended for a single Agent API instance. A multi-instance deployment should use a transactional shared store.

---

# 29. Diagnostics

Agent API exposes lightweight diagnostics so infrastructure can be tested without running the full multi-agent flow:

```text
GET  /api/v1/diagnostics/llm
POST /api/v1/diagnostics/llm/test
GET  /api/v1/diagnostics/runner
POST /api/v1/diagnostics/runner/execute
```

This reduces unnecessary LLM usage while troubleshooting provider or Runner connectivity.

---

# 30. Runner API

Runner exposes:

```text
GET  /health
GET  /api/v1/diagnostics/dotnet
POST /api/v1/executions
```

Supported execution commands are restricted to:

```text
dotnet restore
dotnet build
dotnet test
```

For `dotnet test`, Runner resolves a solution/test project, performs restore and then executes the tests.
