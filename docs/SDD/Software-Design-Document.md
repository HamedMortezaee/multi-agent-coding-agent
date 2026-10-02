# Software Design Document
## Multi-Agent Coding Agent

**Version:** 1.0  
**Status:** Baseline  
**Date:** 2026-10-03

---

# 1. Purpose

The system receives a software-development request and coordinates multiple agents to plan, generate, review, fix and report the result.

```text
Requirement
    ↓
Planning
    ↓
Human Review
    ↓
Code Generation
    ↓
Execution
    ↓
Testing / Review
    ↓
Fixing
    ↓
Final Result
```

---

# 2. Scope

The MVP targets small ASP.NET Core Web API projects.

Primary demo scenario:

```text
Todo REST API
```

---

# 3. Actors

## User
Provides the initial development request.

## Human Reviewer
Reviews the generated plan and can approve, reject or modify it.

## n8n
Orchestrates workflow transitions.

## ASP.NET Core Backend
Provides APIs, OpenAI integration, state, workspace and file services.

## OpenAI
Provides LLM capabilities.

## Code Execution Environment
Will execute generated code when a runner becomes available.

---

# 4. Functional Requirements

## FR-001 — Receive Request
The system must accept a text requirement.

## FR-002 — Generate Plan
Planner must produce a structured implementation plan.

Example:

```json
{
  "goal": "Create Todo REST API",
  "steps": [
    {
      "order": 1,
      "title": "Create project"
    }
  ],
  "ambiguities": [],
  "requiresHumanReview": true
}
```

## FR-003 — Human Review
The workflow must allow a human to approve, reject, modify or add feedback after planning.

## FR-004 — Generate Code
Coder must output structured project files rather than one unstructured text block.

Example:

```json
{
  "files": [
    {
      "path": "Todo.Api/Program.cs",
      "content": "..."
    }
  ]
}
```

## FR-005 — Execute Code
Generated code should be built/run/tested in an isolated execution environment.

Current status: blocked because no execution environment is available.

## FR-006 — Review Execution
Tester/Reviewer analyzes compilation, runtime and test results.

## FR-007 — Fix Failed Code
Fixer receives current files, previous attempts and latest errors and produces corrective changes.

## FR-008 — Retry Limit
Maximum fix attempts: 3.

## FR-009 — Global Timeout
Maximum run duration: 15 minutes.

## FR-010 — Final Report
Every run must produce a final Markdown report, including controlled failures.

---

# 5. High-Level Architecture

```text
                       ┌───────────────┐
                       │     User      │
                       └───────┬───────┘
                               │
                               ▼
                      ┌────────────────┐
                      │   n8nir.ir     │
                      │      n8n       │
                      └───────┬────────┘
                              │
          ┌───────────────────┼─────────────────┐
          │                   │                 │
          ▼                   ▼                 ▼
      Planner              Coder         Tester/Fixer
          │                   │                 │
          └───────────────────┼─────────────────┘
                              │
                              ▼
                   ┌────────────────────┐
                   │ ASP.NET Core API   │
                   └─────────┬──────────┘
                             │
          ┌──────────────────┼─────────────────┐
          │                  │                 │
          ▼                  ▼                 ▼
      OpenAI              State            Workspace
      Service             Store            Manager
                                                │
                                                ▼
                                      IExecutionSandbox
                                                │
                                                ▼
                                        Future Runner
```

---

# 6. Deployment Architecture

```text
                    Internet
                       │
          ┌────────────┴────────────┐
          │                         │
          ▼                         ▼
     n8nir.ir                Windows Hosting
       n8n                   ASP.NET Core API
          │                         │
          └──────────HTTPS──────────┘
                                    │
                                    ▼
                                OpenAI API
```

Windows Hosting is API-only and must not be used for arbitrary process execution.

---

# 7. ASP.NET Core Solution Structure

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

# 8. Domain Model

## AgentRun

```text
ExecutionId
UserRequest
Status
StartedAt
Deadline
FixAttemptCount
```

## AgentPlan

```text
Goal
Steps
Ambiguities
RequiresHumanReview
Approved
```

## ProjectFile

```text
Path
Content
Version
```

## ExecutionAttempt

```text
AttemptNumber
StartedAt
CompletedAt
ExitCode
StdOut
StdErr
Success
```

---

# 9. Run Status

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

---

# 10. Agent State

Example:

```json
{
  "executionId": "...",
  "status": "Fixing",
  "userRequest": "...",
  "plan": {},
  "humanFeedback": "...",
  "files": [],
  "executionAttempts": [],
  "fixAttemptCount": 2,
  "startedAt": "...",
  "deadline": "..."
}
```

---

# 11. LLM Architecture

```text
Planner
Coder
Tester
Fixer
   │
   ▼
ILlmService
   │
   ▼
OpenAiLlmService
   │
   ▼
OpenAI API
```

Agent business logic must not call the OpenAI SDK directly.

---

# 12. Planner Contract

## Input

```json
{
  "userRequest": "..."
}
```

## Output

```json
{
  "goal": "...",
  "steps": [
    {
      "order": 1,
      "title": "...",
      "description": "..."
    }
  ],
  "ambiguities": [],
  "requiresHumanReview": true
}
```

---

# 13. Coder Contract

## Input

```json
{
  "userRequest": "...",
  "plan": {},
  "humanFeedback": null,
  "currentFiles": []
}
```

## Output

```json
{
  "files": [
    {
      "path": "...",
      "content": "..."
    }
  ]
}
```

---

# 14. Tester Contract

```json
{
  "success": false,
  "issues": [
    {
      "severity": "Error",
      "type": "Compilation",
      "message": "..."
    }
  ],
  "nextAction": "fix"
}
```

---

# 15. Fixer Contract

## Input

```json
{
  "currentFiles": [],
  "latestExecutionResult": {},
  "previousAttempts": []
}
```

## Output

```json
{
  "changes": [
    {
      "path": "...",
      "content": "...",
      "reason": "..."
    }
  ]
}
```

---

# 16. API Design

Initial API surface:

```text
POST /api/runs
GET  /api/runs/{id}

POST /api/runs/{id}/plan
POST /api/runs/{id}/approve
POST /api/runs/{id}/generate
POST /api/runs/{id}/execute
POST /api/runs/{id}/review
POST /api/runs/{id}/fix

GET  /api/runs/{id}/report
```

---

# 17. Main n8n Workflow

```text
Webhook / Form
      ↓
Create Run
      ↓
Planner
      ↓
Requires Human?
   /           \
 Yes            No
 ↓               │
Wait Approval    │
 ↓               │
 └───────┬───────┘
         ↓
       Coder
         ↓
     Save Files
         ↓
      Execute
         ↓
       Tester
         ↓
      Success?
      /      \
    Yes       No
     ↓         ↓
Report       Retry Check
               ↓
          attempt < 3 ?
            /      \
          Yes       No
           ↓         ↓
         Fixer      Failure
           ↓
       Save Changes
           ↓
         Execute
```

---

# 18. n8n Workflow Delivery

Workflow files will be committed under:

```text
workflows/
```

Expected files:

```text
01-main-coding-agent.json
02-human-approval.json
03-final-report.json
```

Credentials and secrets must not be committed.

---

# 19. Security

## n8n → ASP.NET Core
Use API authentication, initially an API key header:

```http
X-Agent-Api-Key: ***
```

## OpenAI API Key
Must never be stored in:
- source code
- workflow JSON
- GitHub repository
- reports

## Workspace Paths
Reject path traversal such as:

```text
../../
```

## Generated Code
Generated code must be treated as untrusted.

---

# 20. Observability

All logs should include:

```text
ExecutionId
Agent
Attempt
Status
Duration
```

---

# 21. Error Classification

```text
AgentError
LlmError
ValidationError
ExecutionError
TimeoutError
InfrastructureError
```

---

# 22. Reporting

Final report format: Markdown.

Sections:

```text
User Request
Approved Plan
Generated Files
Execution Attempts
Errors
Fix Attempts
Final Result
```

Failure runs must still produce a report.

---

# 23. Short-Term Memory

Required state includes:
- Plan
- Files
- Errors
- Attempts
- Human feedback

---

# 24. Long-Term Memory

Not part of the MVP.

Possible future data:
- successful fix patterns
- known errors
- previous solutions

---

# 25. GitHub Integration

Post-MVP:

```text
Generate Files
 ↓
Create Branch
 ↓
Commit
 ↓
Push
 ↓
Pull Request
```

---

# 26. Known Blocker

## BLK-001 — Code Execution Environment

Current environment:

```text
Windows Hosting: API hosting only
n8nir.ir: Workflow runtime
Sandbox: unavailable
Docker: unavailable
Remote Runner: unavailable
```

Impact:

```text
Generated code cannot currently be built,
executed and tested in an isolated environment.
```

This is the main blocker for full compliance with the real-code-execution requirement.

---

# 27. Implementation Phases

## Phase 1
- ASP.NET Core solution
- Domain models
- State management
- Basic API

## Phase 2
- OpenAI integration
- Planner
- Coder
- Tester
- Fixer

## Phase 3
- n8n main workflow
- Human-in-the-loop
- Retry logic
- Timeout

## Phase 4
- Report generation
- Logging
- Error handling

## Phase 5
- Execution runner

## Phase 6
- GitHub integration
- Long-term memory
- Smart human stop

---

# 28. Definition of Done

MVP functional baseline:

```text
User Request            ✓
Planner                 ✓
Human Approval          ✓
Coder                   ✓
State Management        ✓
Tester                  ✓
Fixer                   ✓
Maximum 3 Retries       ✓
15 Minute Limit         ✓
Final Report            ✓
n8n JSON Import         ✓
ASP.NET Core Deployment ✓
OpenAI Integration      ✓
```

Full project compliance additionally requires:

```text
Real Code Execution
```
