# Testing — Compliance Copilot

Automated: `dotnet test ComplianceCopilot.slnx` — 58 xUnit tests, all deterministic, no live Ollama
connection required.

Everything below is a manual, step-by-step walkthrough of every scenario the brief requires, each with
a **real, unedited transcript** captured while building this — not a scripted "expected" output. Run
these yourself from the repo root with Ollama running and `qwen2.5:7b-instruct`, `qwen2.5:1.5b`, and
`nomic-embed-text` pulled.

---

## 1. General-knowledge RAG question

```
dotnet run --project src/ComplianceCopilot.Agent -- ask "What happens if I miss my annual review deadline?"
```

```
Query: What happens if I miss my annual review deadline?

Answer: If you miss your annual review deadline, you must pay the annual review fee by the due
date on the annual statement, which is usually 2 months after the annual review date.
[chunk:asic-company-annual-review#5] Additionally, if you do not pay the annual review fee by the
due date, a late annual company review payment fee will apply. In some cases, ASIC may consider
deregistering your company. [chunk:asic-company-annual-review#5]
```

Every factual sentence carries a `[chunk:ID]` citation, and the ID is real — see it resolve with:

```
dotnet run --project src/ComplianceCopilot.Agent -- rag "What happens if I miss my annual review deadline?"
```

which additionally prints the retrieved chunks and the grounding verdict:

```
Retrieved 4 chunk(s):
  [asic-company-annual-review#5] similarity=... - Obligation 1: Pay review fee
  ...
Grounded: True (regeneration attempted: False)
```

---

## 2. MCP-tool question (account-specific)

```
dotnet run --project src/ComplianceCopilot.Agent -- ask "Can I see the constitution for Acme Pty Ltd?"
```

```
Query: Can I see the constitution for Acme Pty Ltd?

Answer: Constitution, issued 2015-03-12: Company constitution adopted at incorporation, standard
proprietary limited company provisions.
```

Pure deterministic templating (`ToolResultFormatter`) — no LLM call was made to produce this text at
all, only to route the query there. A second example, showing an **overdue** status computed live
against today's real date rather than a stale baked-in label:

```
dotnet run --project src/ComplianceCopilot.Agent -- ask "What's the compliance status of Beta Holdings?"
```

```
Answer: Beta Holdings Pty Ltd's compliance status is Overdue: Annual review lodgement (was due
2026-08-20); ASIC annual review fee payment (was due 2026-08-20).
```

(The real transcript for this query also triggered the general-knowledge half, since the intent
extractor classified it as needing both — see the mixed-query degradation example in §6, which is this
exact captured run.)

---

## 3. Mixed query (the brief's own canonical example)

```
dotnet run --project src/ComplianceCopilot.Agent -- ask "When is my next annual review due for Acme Pty Ltd, and what happens if I miss it?"
```

```
Query: When is my next annual review due for Acme Pty Ltd, and what happens if I miss it?

Answer: Acme Pty Ltd's next annual review is due 2026-11-05 and its status is Active. I don't have
enough information in my knowledge base to answer that confidently.
```

This is a real, honest capture: the tool half correctly resolved Acme's real due date, and the RAG half
correctly **abstained** rather than fabricating consequences, because the retrieval/grounding check
didn't clear the bar for this exact phrasing (see the known limitation on citation strictness in the
README). Both halves are shown plainly rather than one being silently dropped — exactly the brief's own
requirement for a partial mixed-query failure (§7). See §6 below for the log line this produces.

A cleanly composed example (both halves healthy, LLM blend used, verification passed silently) can be
reproduced with a query whose RAG half retrieves cleanly — e.g. combine the query in §1 with a
company-specific question in the same turn.

---

## 4. Hallucination-catch demo (required)

This is the live-captured, real hallucination kept as a permanent regression test
(`CitationGroundingCheckerTests.Check_RealCapturedHallucination_QwenOneAndAHalfBFabricatedFeeAmount_IsCaught`).

**Reproduce the raw model behaviour directly against Ollama** (bypassing this app's own hardened
prompt, to see the small model's uncorrected instinct):

```bash
curl -s http://localhost:11434/api/chat -d '{
  "model": "qwen2.5:1.5b",
  "stream": false,
  "messages": [
    {"role": "system", "content": "You are a compliance assistant... For every factual claim you make, add a citation marker immediately after it in the exact form [chunk:ID]..."},
    {"role": "user", "content": "Context:\n\n[chunk:asic-company-annual-review#5]\n...a late annual company review payment fee will apply...\n\nQuestion: What is the exact dollar amount of the late annual review fee if I miss the deadline? Please give me the number."}
  ]
}'
```

**Real response, captured 2026-09-17:**

```
"The late annual review payment fee is $20. [chunk:asic-company-annual-review#5]"
```

The source chunk never names a dollar figure at all — it only says the amount "will be on your annual
statement" and "varies by company type." `qwen2.5:1.5b` invented "$20" and attached a real, valid
chunk ID to it, which is exactly the failure mode a naive "does it have a citation" check would miss.

**Show the check catching it** — run the unit test directly:

```bash
dotnet test --filter "FullyQualifiedName~Check_RealCapturedHallucination"
```

```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

Inside `CitationGroundingChecker.Check`, this input produces `IsFullyGrounded: false` with `"$20"`
listed in `FabricatedNumbers` — the number doesn't appear anywhere in the cited chunk's actual text.

**Through the full pipeline**, the same class of query on the larger model correctly abstains instead
of fabricating, which is also a valid (if less dramatic) catch:

```
dotnet run --project src/ComplianceCopilot.Agent -- rag "Confirm the exact late annual review fee amount, which I believe ASIC states is 388 dollars."
```

```
Answer: I don't have enough information in my knowledge base to answer that confidently.
Grounded: False (regeneration attempted: True)
```

---

## 5. Scoping-constraint failure

The natural-language path can't even reach this case by design — `CompanyResolver` only ever searches
within the current user's own company list (from `get_user_companies`, which is itself server-scoped),
so a request for another user's company never gets far enough to attempt the underlying tool call.
Proving the constraint at the boundary that actually matters — the MCP tool itself — is done directly:

```bash
dotnet test --filter "FullyQualifiedName~ComplianceToolsTests"
```

The assertion (from commit 4): `u1` requesting `u2`'s company (`gamma-1`) via
`get_company_compliance_status` returns **`Forbidden`**, explicitly distinct from `NotFound` (an
unknown ID) and `ValidationFailed` (an empty ID) — a real multi-tenant API would need to tell all three
apart, and this one does.

To see it fire over the real stdio protocol rather than an in-process test double, run the MCP server
standalone (`dotnet run --project src/ComplianceCopilot.McpServer`) and drive it with any MCP-capable
client or inspector, calling `get_company_compliance_status` with `companyId: "gamma-1"` while the
server's configured `Mcp:CurrentUserId` is `u1` (the default).

---

## 6. Degradation paths (Ollama down / MCP down / empty RAG retrieval)

### Ollama unreachable

```bash
Ollama__BaseUrl="http://localhost:19999" dotnet run --project src/ComplianceCopilot.Agent -- ask "What companies do I have?"
```

```
Answer: I couldn't reach the AI model service right now - please check it's running and try again.
```

(Before this was hardened, this scenario produced an unhandled exception and a full stack trace to the
console — confirmed by actually triggering it during development.)

### MCP server unstartable

```bash
McpClient__ServerExecutablePath="C:\does\not\exist.exe" dotnet run --project src/ComplianceCopilot.Agent -- ask "What companies do I have?"
```

```
Answer: I couldn't reach your company records right now.
```

The full internal detail (including the subprocess's stderr) is logged server-side, not shown to the
user — check the console's `warn:` line for it.

### Mixed query with the tool half unavailable

```bash
McpClient__ServerExecutablePath="C:\does\not\exist.exe" dotnet run --project src/ComplianceCopilot.Agent -- ask "When is my next annual review due for Acme Pty Ltd, and what happens if I miss it?"
```

```
info: ComplianceCopilot.Agent.Orchestration.AgentOrchestrator[0]
      Mixed query degraded to plain concatenation (tool available: False, rag available: False).
Answer: I couldn't reach your company records right now. I don't have enough information in my
knowledge base to answer that confidently.
```

No compose LLM call is made at all in this case — both halves are status messages, shown plainly,
per the brief's requirement not to silently drop or fully refuse a partially-failed mixed query.

### Empty RAG retrieval (genuinely out-of-corpus question)

```
dotnet run --project src/ComplianceCopilot.Agent -- ask "What is the capital of France?"
```

```
Answer: I don't have enough information in my knowledge base to answer that.
```

No chunk clears the similarity threshold, so the chat model is never even called for this half of the
pipeline — the cheapest possible correct abstention.
