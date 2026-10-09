# 15-Minute Demo Script

## Multi-Agent Coding Agent

This script is designed for the final project presentation and video demo.

---

## 0:00–1:00 — Introduction

### Show
- Repository root
- README
- Final architecture overview

### Say
> این پروژه یک Multi-Agent Coding Agent است که با n8n ارکستریت می‌شود و Backend آن با ASP.NET Core و C# پیاده‌سازی شده است.  
> هدف سیستم این است که یک درخواست نرم‌افزاری را از زبان طبیعی دریافت کند، برای آن برنامه‌ریزی کند، کد تولید کند، کد را واقعاً اجرا و تست کند، خطاها را بررسی کند و در صورت نیاز تا حداکثر سه بار اصلاح انجام دهد.

### Key points
- n8n = orchestration
- ASP.NET Core = state + agents + APIs
- Runner = real code execution
- LLM provider = OpenAI or AIFA
- Human-in-the-loop = mandatory after planning

---

## 1:00–2:30 — Architecture

### Show
Open:

```text
docs/Final/Architecture-Overview.md
```

### Explain

```text
User
 ↓
n8n
 ↓
CodingAgent.Api
 ├── Planner
 ├── Coder
 ├── Reviewer
 ├── Fixer
 ├── State
 └── Final Report
      │
      ├── ILlmService
      │    ├── OpenAI
      │    └── AIFA
      │
      └── IExecutionSandbox
            ↓
       CodingAgent.Runner
            ↓
       dotnet restore
       dotnet build
       dotnet test
```

### Say
> Agentها مستقیماً به n8n وابسته نیستند. n8n فقط مراحل را هدایت می‌کند.  
> منطق Agentها، وضعیت Run، فایل‌ها، Retry Count و Timeout در ASP.NET Core نگهداری می‌شوند.

---

## 2:30–3:30 — Agent Responsibilities

### Show
Application layer folders.

### Explain

#### Planner
- Requirement analysis
- implementation plan
- assumptions
- ambiguities

#### Coder
- generates full project files
- generates xUnit test project
- materializes files

#### Reviewer
- reviews real execution output
- returns:
  - `complete`
  - `fix`
  - `fail`

#### Fixer
- receives errors and current files
- applies corrective changes
- maximum 3 attempts

### Say
> Reviewer فقط متن تولیدشده را بررسی نمی‌کند؛ نتیجه واقعی اجرای dotnet test را تحلیل می‌کند.

---

## 3:30–4:30 — State and Human-in-the-loop

### Show
n8n workflow start path.

### Explain

```text
Start Request
 ↓
Create Run
 ↓
Planner
 ↓
WaitingForHuman
```

### Say
> برای Human-in-the-loop از Wait Node طولانی‌مدت استفاده نکردم.  
> وضعیت Run در Backend ذخیره می‌شود و Workflow خاتمه پیدا می‌کند.  
> وقتی Human Review ارسال می‌شود، همان ExecutionId ادامه داده می‌شود.

### Show example review payload

```json
{
  "action": "review",
  "executionId": "<execution-id>",
  "decision": "approve",
  "feedback": "Use .NET 9, in-memory storage, Swagger, and no authentication for this MVP."
}
```

---

## 4:30–6:00 — Start Live Demo

### Send request

```json
{
  "action": "start",
  "request": "Create a Todo ASP.NET Core Web API with CRUD operations.",
  "requestedBy": "hamed"
}
```

### Show
- executionId
- generated plan
- WaitingForHuman status

### Say
> سیستم هنوز کدنویسی را شروع نکرده است. ابتدا Plan تولید شده و منتظر تصمیم انسانی است.

---

## 6:00–7:00 — Human Approval

### Send

```json
{
  "action": "review",
  "executionId": "<execution-id>",
  "decision": "approve",
  "feedback": "Use .NET 9, in-memory storage, Swagger, and no authentication for this MVP."
}
```

### Show n8n execution

Highlight:

```text
Generate Code
 ↓
Execute Project
 ↓
Review Result
```

### Say
> بعد از Approval، Coder پروژه کامل و Test Project را تولید می‌کند.

---

## 7:00–9:00 — Generated Code and Real Execution

### Show generated workspace

Agent workspace:

```text
App_Data/workspaces/{executionId}
```

Runner workspace:

```text
RunnerData/workspaces/{executionId}
```

### Show important generated files

```text
TodoApi.sln
TodoApi/
TodoApi.Tests/
TodoApi.Tests/TodosApiTests.cs
```

### Show Execute Project output

Expected successful result:

```text
TodoApi -> ...TodoApi.dll
TodoApi.Tests -> ...TodoApi.Tests.dll

Passed!
Failed: 0
Passed: 3
Skipped: 0
Total: 3
```

### Say
> این بخش برای پروژه مهم است چون کد فقط Generate نشده؛ واقعاً روی Runner Restore، Build و Test شده است.

---

## 9:00–10:00 — Reviewer

### Show Reviewer output

```json
{
  "success": true,
  "issues": [],
  "summary": "The solution built successfully and all 3 integration tests passed.",
  "nextAction": "complete"
}
```

### Say
> Reviewer خروجی واقعی Runner را دریافت می‌کند. اگر خطای Compile یا Test وجود داشته باشد، nextAction را fix می‌کند.

---

## 10:00–11:00 — Fix Loop

### Show n8n branch

```text
Review Result
 ↓
Is Fix Needed?
 ├── complete → Final Report
 └── fix
      ↓
    Fix Project
      ↓
    Execute Project
      ↓
    Review Result
```

### Explain
- Backend owns FixAttemptCount
- maximum = 3
- timeout = 15 minutes
- Fixer receives previous attempts

### Say
> Retry Count داخل n8n نگهداری نمی‌شود؛ Backend Source of Truth است.

---

## 11:00–12:00 — Final Report

### Show Generate Final Report output

Highlight:

```text
Status: Completed
Fix Attempts: 0
Generated Files
Execution Attempts
Reviewer
Final Result
```

### Say
> Final Report شامل Request، Plan، Human Feedback، فایل‌های تولیدشده، Execution Output، Review Result و وضعیت نهایی Run است.

---

## 12:00–13:00 — Provider Configuration

### Show appsettings

### OpenAI

```json
{
  "AI": {
    "Provider": "OpenAI"
  },
  "OpenAI": {
    "BaseUrl": "https://api.openai.com/v1/",
    "Model": "gpt-5.6-sol",
    "ApiKey": ""
  }
}
```

### AIFA

```json
{
  "AI": {
    "Provider": "Aifa"
  },
  "Aifa": {
    "BaseUrl": "https://aifa-chatbot.dev.dotin.ir/",
    "Model": "assistance-model",
    "Token": "",
    "UserId": "coding-agent"
  }
}
```

### Say
> Agentها فقط به ILlmService وابسته‌اند و با تغییر Configuration می‌توان Provider را تغییر داد.

---

## 13:00–14:00 — Swagger and Diagnostics

### Show

Agent Swagger:

```text
https://n8n-agent.samanooqazvin.com/swagger
```

Runner Swagger:

```text
https://n8n-runner.samanooqazvin.com/swagger
```

### Useful diagnostics

```text
GET  /health
GET  /api/v1/diagnostics/llm
POST /api/v1/diagnostics/llm/test
GET  /api/v1/diagnostics/runner
POST /api/v1/diagnostics/runner/execute
```

### Say
> برای کاهش هزینه LLM، Endpointهای Diagnostic اضافه شده‌اند تا ارتباط با LLM و Runner بدون اجرای Flow کامل تست شود.

---

## 14:00–15:00 — Design Decisions and Closing

### Mention these decisions

1. n8n only orchestrates; backend owns state.
2. Human review uses durable state/resume.
3. Real execution is behind `IExecutionSandbox`.
4. LLM access is behind `ILlmService`.
5. Retry limit and timeout are backend rules.
6. Generated code is treated as untrusted.
7. Windows Runner is not considered a hardened sandbox.

### Final sentence

> نتیجه نهایی این پروژه یک Multi-Agent Coding Agent عملیاتی است که می‌تواند یک درخواست نرم‌افزاری را دریافت کند، Plan بسازد، با تایید انسان کد تولید کند، کد را واقعاً Build و Test کند، نتیجه را Review کند، در صورت نیاز Fix انجام دهد و در نهایت یک گزارش کامل تولید کند.

---

# Demo Checklist

Before recording:

- Agent API health is OK
- Runner health is OK
- Runner workspace is writable
- n8n workflow is active
- Workflow Config points to Agent API
- API key is configured
- selected LLM provider is reachable
- Swagger pages are reachable
- browser tabs are already opened
- use a fresh execution for the live demo
- keep one known successful execution available as backup

## Backup Successful Run

```text
ExecutionId: fc30d504-9248-4413-81ee-f0e200a96c4a
Status: Completed
Tests: 3 passed
Reviewer: complete
```

If the live demo fails because of network or LLM availability, use this completed run to demonstrate:
- generated project
- actual test output
- reviewer output
- final report
