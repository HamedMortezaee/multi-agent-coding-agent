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

Current expected response while no runner exists:

```json
{
  "success": false,
  "data": {
    "available": false,
    "success": false,
    "exitCode": null,
    "stdout": "",
    "stderr": "",
    "durationMs": 0,
    "timedOut": false,
    "reason": "EXECUTION_UNAVAILABLE"
  },
  "errors": [
    {
      "code": "EXECUTION_UNAVAILABLE",
      "message": "No code execution environment is configured.",
      "retryable": false
    }
  ]
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

ASP.NET Core should expose OpenAPI/Swagger in development and test environments.

Recommended endpoints:

```text
/swagger
/openapi/v1.json
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

The execution endpoint and state transitions are implemented.

Current backend behavior without a runner:

```text
WaitingForExecution
  ↓
POST /execute
  ↓
Executing
  ↓
ExecutionAttempt recorded
  ↓
Reviewing
```

The HTTP response is currently:

```text
503 Service Unavailable
EXECUTION_UNAVAILABLE
```

This response is a known environment limitation, not a generated-code failure.

The n8n workflow should preserve the `executionId` and continue to the Review step when handling this known response so that a controlled final result/report can be produced.

---

# 25. Reviewer Behavior for Infrastructure Failure

If the latest execution result has:

```json
{
  "available": false,
  "reason": "EXECUTION_UNAVAILABLE"
}
```

Reviewer does not call the LLM.

It deterministically produces:

```json
{
  "success": false,
  "summary": "Generated code could not be executed because no runner is configured.",
  "nextAction": "fail"
}
```

This prevents the Fixer from trying to repair source code for an infrastructure problem.

---

# 26. Fix Retry Enforcement

Backend owns the retry counter.

```text
MaxFixAttempts = 3
```

Each successful entry into Fixer increments `FixAttemptCount`.

After applying a fix:

```text
Fixing → WaitingForExecution
```

The next cycle must execute and review the updated project before another fix is allowed.


---

# 27. API Key Enforcement

All routes under:

```text
/api/*
```

require:

```http
X-Agent-Api-Key: <secret>
```

The expected secret is loaded from:

```text
AGENT_API_KEY
```

with `Security:ApiKey` as a configuration fallback.

Missing or invalid credentials return:

```http
401 Unauthorized
```

The health endpoint is intentionally excluded.

---

# 28. Durable Persistence

The MVP uses a file-backed implementation of `IAgentRunRepository`.

Default location:

```text
App_Data/agent-runs
```

Persisted state includes:

- run status
- original request
- plan
- human feedback
- generated files and versions
- execution attempts
- review results
- fix summaries
- retry count
- deadline

Every state-changing Application service explicitly calls `SaveAsync`.

This allows the workflow to resume its state after application restarts.

Current limitation: the file repository is intended for a single application instance. Multi-instance deployment should move persistence to a transactional shared database.
