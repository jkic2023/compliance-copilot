# Compliance Copilot

A small AI assistant for a fictional SaaS platform used by accounting/bookkeeping firms, built on
**.NET 10** with a **locally-hosted Ollama model**. It answers general compliance-knowledge questions
via a RAG pipeline over public regulator guidance, and answers user-specific questions (companies,
filing deadlines, documents) by calling tools exposed over an **MCP server**.

> Full architecture write-up — diagrams, every design decision, the full verification mechanism — is
> in [`docs/project-overview.html`](docs/project-overview.html). Open it in a browser.

The one sentence this whole build is organised around: **the LLM proposes, deterministic code
disposes — it never gets the last word on whether something is true.**

---

## What it does

`dotnet run --project src/ComplianceCopilot.Agent -- ask "<question>"`

A real, unedited transcript from this build — the brief's own example query, captured live:

```
Query: When is my next annual review due for Acme Pty Ltd, and what happens if I miss it?

Answer: Acme Pty Ltd's next annual review is due 2026-11-05 and its status is Active. I don't
have enough information in my knowledge base to answer that confidently.
```

This one query needs both an MCP tool call (Acme's specific due date) and RAG retrieval (the
general consequences of missing a review) — the tool half returned real, correct data; the RAG half
correctly **declined** rather than guessing at the consequences, because the retrieved passages
didn't clear this build's grounding bar for that particular phrasing. That's not a bug being hidden —
it's the hallucination-mitigation layer (see below) doing exactly its job: abstain rather than
fabricate. A plain-knowledge version of the same underlying question, phrased slightly differently,
answers cleanly with citations:

```
Query: What happens if I miss my annual review deadline?

Answer: If you miss your annual review deadline, you must pay the annual review fee by the due
date on the annual statement, which is usually 2 months after the annual review date.
[chunk:asic-company-annual-review#5] Additionally, if you do not pay the annual review fee by the
due date, a late annual company review payment fee will apply. In some cases, ASIC may consider
deregistering your company. [chunk:asic-company-annual-review#5]
```

More real transcripts, including the required hallucination-catch demo, are in
[`docs/testing.md`](docs/testing.md).

---

## Architecture at a glance

```
User query
   │
   ▼
[Agentic]       Intent/entity extraction (LLM proposes structured JSON)
   ▼
[Deterministic] IntentParser + QueryRouter → RAG only / MCP tool only / both
   │                                   │
   ▼                                   ▼
[Agentic] RAG draft answer         [Deterministic] Resolve company (never LLM-invented) →
   w/ [chunk:N] citations              MCP tool call → deterministic template (zero LLM,
   ▼                                   zero hallucination risk by construction)
[Deterministic] CitationGroundingChecker
   (no citation / invalid chunk id /
    fabricated number) → 1 correction
    pass on a distinct smaller model →
    abstain if still failing
   │                                     │
   └─────────────────┬───────────────────┘
                      ▼
      Both halves real? ──No──▶ [Deterministic] plain concatenation of both honest answers
                      │
                     Yes
                      ▼
      [Agentic] Compose one answer → [Deterministic] tool-value cross-check +
                                       citation drop/invention check →
                                       same plain-concatenation fallback on failure
                      │
                      ▼
              response to user
```

```
ComplianceCopilot.sln
├── ComplianceCopilot.Agent        console app — orchestrator, RAG, verification, MCP client
├── ComplianceCopilot.McpServer    separate console process — stdio MCP server, mock data + tools
├── ComplianceCopilot.Shared       DTOs, verification/checker logic, config
├── ComplianceCopilot.Ingestion    one-off tool: raw source → chunk → embed → rag-index.json
└── ComplianceCopilot.Tests        xUnit — every deterministic piece, 58 tests, no live model needed
```

**Key decisions** (full reasoning in `docs/project-overview.html`):

- **Ollama, low-level `ChatAsync`/`EmbedAsync` API** — `qwen2.5:7b-instruct` for reasoning/tool-use,
  `qwen2.5:1.5b` for the one hallucination-correction pass (a genuinely smaller, distinct model —
  not the same model re-reading its own mistake), `nomic-embed-text` for embeddings. The high-level
  OllamaSharp `Chat` wrapper silently returned nothing in an early smoke test; the low-level API also
  fits the architecture better, since it puts the tool-call boundary in this code, not a library's
  event loop.
- **In-memory brute-force cosine RAG store, no vector DB** — the corpus is 3 real ASIC guidance pages
  (34 chunks); a vector database is unjustifiable overhead at this scale.
- **A genuine separate MCP process over stdio** — the official `ModelContextProtocol` C# SDK, not an
  in-process call. Tools return a typed `ToolOutcome<T>` (`NotFound` / `Forbidden` /
  `ValidationFailed` / `Unavailable`) instead of throwing, and a per-tenant scoping constraint is
  enforced server-side (a request for another user's company returns `Forbidden`, proven by a real
  cross-process test, not just a unit-test mock).
- **Two independent, deterministic verification mechanisms**, not a system-prompt instruction:
  citation grounding for RAG answers, and a tool-result cross-check for the mixed-query composition
  step — see below.

---

## Agentic vs. Deterministic

| Step | Who decides | Why |
|---|---|---|
| Extracting intent/entities from free text | **Agentic** (LLM proposes a JSON object) | Genuinely needs language understanding — a free-text question can't be parsed by rules alone. |
| Turning that JSON into a typed `Intent` + choosing a route | **Deterministic** (`IntentParser`, `QueryRouter`) | Interpreting and acting on the LLM's proposal is exactly the kind of decision that should never be left to another LLM call — `IntentParser` has a named fallback for malformed/unexpected output, and `QueryRouter` is a pure function. |
| Resolving a free-text company name to a real `companyId` | **Deterministic** (`CompanyResolver`) | The LLM never sees or invents a `companyId` — it only ever proposes a name, matched against the real list returned by the MCP server. |
| Calling the MCP tool with validated arguments | **Deterministic** | Tool arguments are constructed from already-resolved, validated values — never passed through free-form LLM text. |
| Formatting a tool-only answer | **Deterministic** (`ToolResultFormatter`) | Pure string templating over real data — zero LLM calls, zero hallucination risk **by construction**, not by a check catching one after the fact. |
| Drafting a RAG answer with citations | **Agentic** (`RagAnswerGenerator`) | Composing a natural-language answer from retrieved passages is exactly what an LLM is for. |
| Verifying those citations are honest | **Deterministic** (`CitationGroundingChecker`) | Checking that a citation exists and a claimed number actually appears in the cited text is a mechanical check, not a judgement call — and it's the part that actually catches a hallucination, not just states a policy against one. |
| The one-shot correction rewrite | **Agentic**, on a **distinct, smaller model** | A real second opinion, not the same model re-reading its own draft — and still deterministically re-checked afterwards, exactly once, then abstain. |
| Composing a tool fact + RAG answer into one response | **Agentic** (`ComposeAsync`) — but **only when both halves are already-verified real facts** | Blending two pieces of text into natural prose is a language task; deciding *whether* it's safe to do that at all is not (see below). |
| Deciding whether to compose at all, and the tool-value/citation cross-check on the result | **Deterministic** | Live testing surfaced two real failure modes here — the compose step silently dropping a verified citation, and separately inventing a fake one — both now caught by code, not policy, with the same safe fallback (plain concatenation) either way. |
| Failure/outage handling (Ollama down, MCP down, empty retrieval) | **Deterministic** | Degrading to a clear message is a control-flow decision, and the one place it should never be "ask the LLM to explain the error." |

---

## Hallucination mitigation

**Chosen mechanism, implemented end-to-end and demonstrated with a live, real catch (not staged):**

1. **Citation grounding** (RAG answers) — `CitationGroundingChecker` runs three deterministic checks
   on the draft: every factual sentence must carry a `[chunk:ID]` citation; every cited ID must be one
   of the chunks genuinely retrieved for this query; and every number in a cited sentence (a fee, a day
   count) must actually appear in that chunk's text — not just any real-looking citation attached to an
   invented figure. One correction pass on a distinct, smaller model on failure, then abstain.
2. **Tool-result cross-check** (the mixed-query composition step) — the only place a tool-derived fact
   passes through an LLM at all (a tool-only answer is pure templating and never touches one).
   Deterministic code verifies the composed answer still contains the tool's real dates/values
   verbatim, and that it neither dropped nor invented a `[chunk:ID]`. Either failure discards the LLM's
   phrasing for a plain, safe concatenation of the two already-verified pieces.

**A real hallucination, caught live** (kept as a permanent regression test,
`CitationGroundingCheckerTests.Check_RealCapturedHallucination_...`): asked `qwen2.5:1.5b` the exact
dollar amount of the ASIC late annual review fee — a figure the source explicitly never states (it
only says the amount "will be on your annual statement" and varies by company type). It answered
*"The late annual review payment fee is $20. [chunk:asic-company-annual-review#5]"* — a real chunk ID
attached to a fabricated number. `qwen2.5:7b-instruct`, asked the same question, correctly declined
instead. The checker catches the `qwen2.5:1.5b` case regardless of which model is used for drafting.

**Rejected alternative:** confidence/log-prob or self-critique scoring. Ollama doesn't reliably expose
per-token log-probs across arbitrary local models, and a self-critique prompt is itself just another
LLM call that can hallucinate its own confidence — it would add complexity without a stronger
guarantee than the deterministic checks above.

---

## MCP server

Three tools, a genuinely separate process over stdio (official `ModelContextProtocol` C# SDK):

| Tool | Purpose |
|---|---|
| `get_user_companies` | Lists companies for the current mock user — called first, always, to resolve a free-text company name deterministically. |
| `get_company_compliance_status` | Overdue/upcoming obligations, computed from an injected `TimeProvider` reference date, never a stale baked-in label. |
| `get_document` | A mock document record (metadata + text summary) by company + document type. |

**Scoping constraint:** every tool is scoped to the current mock user (`u1`, owning Acme Pty Ltd and
Beta Holdings Pty Ltd). A second mock user (`u2`, owning Gamma Trading Pty Ltd) exists purely to prove
the constraint: a request for Gamma's `companyId` while scoped to `u1` returns **`Forbidden`** — a
distinct error from `NotFound` (an unknown ID) and `ValidationFailed` (a bad ID) — proven by a real
cross-process test in `ComplianceToolsTests`, not just a mock assertion. Logging is routed to `stderr`
since `stdout` is the JSON-RPC transport channel for a stdio server.

---

## RAG implementation

- **Source:** 3 real ASIC guidance pages (company annual review, officeholder obligations, director
  obligations) — 34 chunks total.
- **Chunking:** section/heading-based (`DocumentChunker.ChunkMarkdownBySection`), not fixed-size —
  these are real guidance pages with genuine heading structure, and section boundaries preserve a
  coherent unit of meaning better than blind splitting.
- **Retrieval:** brute-force cosine top-k over an in-memory index persisted to `rag-index.json`, which
  carries its own provenance (embedding model, dimension, generation time) so a future model swap fails
  loudly on a dimension mismatch instead of silently corrupting scores. Below the configured similarity
  threshold, a chunk isn't returned at all — this is what lets the pipeline abstain on an out-of-corpus
  question without even calling the chat model.
- **Traceability:** every RAG-grounded claim carries a `[chunk:ID]` citation, deterministically checked
  (see Hallucination mitigation above) — not just present, but verified against the actual retrieved
  text.
- **Explicitly out of scope for RAG:** the mocked user/entity/document data — that's MCP-tool territory
  only, never embedded or retrieved via RAG.

---

## Running it

Requires the **.NET 10 SDK** and **Ollama** running locally with three models pulled:

```bash
ollama pull qwen2.5:7b-instruct
ollama pull qwen2.5:1.5b
ollama pull nomic-embed-text
```

```bash
dotnet build ComplianceCopilot.slnx
dotnet test ComplianceCopilot.slnx        # 58 tests, all deterministic, no live model needed
```

`rag-index.json` is committed (small enough that "clone and run" beats "technically a build
artifact"), and fully regeneratable from source:

```bash
dotnet run --project src/ComplianceCopilot.Ingestion   # from the repo root — full rebuild, always
```

```bash
# Full pipeline
dotnet run --project src/ComplianceCopilot.Agent -- ask "When is my next annual review due for Acme Pty Ltd, and what happens if I miss it?"

# RAG only, standalone, prints retrieved chunks + grounding status
dotnet run --project src/ComplianceCopilot.Agent -- rag "What happens if I miss my annual review deadline?"

# Intent/routing only, standalone
dotnet run --project src/ComplianceCopilot.Agent -- intent "What companies do I have?"
```

The MCP server is launched automatically as a subprocess by the Agent. To inspect the raw protocol
directly, run it standalone: `dotnet run --project src/ComplianceCopilot.McpServer`.

A full step-by-step manual walkthrough of every required scenario is in
[`docs/testing.md`](docs/testing.md).

---

## Known limitations

- **The citation-grounding check is stricter than the model is consistent** — it requires a citation on
  *every* sentence of a claim, but the small local model sometimes cites only the *last* sentence of a
  multi-sentence answer, treating one citation as covering the whole block. The result: an occasional
  accurate answer gets sent through an unnecessary correction pass (or ends up abstaining) even though
  nothing was actually wrong with it — the system is being more cautious than strictly necessary, never
  less. This was left as a known trade-off rather than loosened, because every cheaper fix (crediting an
  earlier sentence from a later citation, or checking once per paragraph instead of per sentence) opens
  a real gap: a fabricated claim could then hide next to a genuinely cited one. The more robust fix — a
  code-based citation matcher that works out which sentence a source actually supports, instead of
  relying on the model to tag it — is real future work, not implemented here given the scope of this
  assessment.
- **A citation only proves a number appears somewhere in the cited chunk, not that it's being used in
  the right context** — the checker cannot catch a real figure quoted for the wrong scenario, only a
  figure that's fabricated outright.
- **The ASIC late-fee dollar amount genuinely isn't in this build's corpus** — the source page names no
  figure at all ("the fee amount... will be on your annual statement... varies by company type"), and a
  page that would have named one had its real numbers populated client-side, not present in the fetched
  markup. This is why "what's the exact late fee" is this build's most reliable hallucination-prone demo
  query — a genuine content gap, not a contrived trap.
- **A mixed-query answer that fails verification reads as two concatenated sentences, not one smooth
  paragraph** — a deliberate trade-off: correctness over polish. The brief explicitly deprioritises UI
  polish, and a safe, honest, slightly less fluent answer is the right side of that trade for this build.
- Single-turn queries only — no multi-turn conversation memory.
- `get_document` returns a text summary/metadata record, not an actual file.
- English-only content and queries.
- "Current user" is a single hardcoded mock identity per run — no login/auth flow, since auth is
  explicitly out of scope for a mocked system.
- "Traceable to source" means citing the source chunk/section, not exact byte/character offsets.

---

## Testing

```bash
dotnet test ComplianceCopilot.slnx
```

58 tests, all deterministic — the checkers, chunker, intent parser, router, tool-result formatter and
cross-checker, and the MCP tools' scoping constraint, all exercised against fixed inputs with no live
Ollama connection required. A full manual walkthrough of the four required end-to-end scenarios (a
general-knowledge RAG question, an MCP-tool question, the mixed query, the hallucination-catch demo)
plus the scoping-constraint failure and the MCP-down/Ollama-down/RAG-empty degradation paths is in
[`docs/testing.md`](docs/testing.md).

---

## Project layout

```
ComplianceCopilot.slnx
README.md
docs/
  project-overview.html      full architecture write-up (this file's companion, TOC + diagrams)
  testing.md                 manual walkthrough of every required scenario
  architecture.html          internal build notes (gitignored, not part of the deliverable)
  interview-prep.md          internal walkthrough rehearsal notes (gitignored)
content/raw/                 the 3 ASIC guidance pages the RAG corpus is built from
rag-index.json               the persisted, committed, regeneratable RAG index
src/
  ComplianceCopilot.Agent/         Orchestration/, Rag/, Mcp/
  ComplianceCopilot.McpServer/     Tools/, Data/
  ComplianceCopilot.Shared/        Domain/, Agent/, Rag/, Results/, Configuration/
  ComplianceCopilot.Ingestion/
tests/
  ComplianceCopilot.Tests/
```
