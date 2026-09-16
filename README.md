# Compliance Copilot

*Scaffold in progress — this README is filled in properly in a later commit (see `docs/project-overview.html` for the full write-up once it exists).*

A small AI assistant for a fictional SaaS platform used by accounting/bookkeeping firms, built on
.NET 10 with a locally-hosted Ollama model. It answers general compliance-knowledge questions via a
RAG pipeline over public regulator guidance, and answers user-specific questions (companies, filing
deadlines, documents) by calling tools exposed over an MCP server.

## Project layout

```
ComplianceCopilot.sln
src/
  ComplianceCopilot.Agent/        orchestrator, RAG, verification, MCP client
  ComplianceCopilot.McpServer/    separate stdio MCP server, mock data + tools
  ComplianceCopilot.Shared/       shared DTOs/contracts
  ComplianceCopilot.Ingestion/    chunk + embed + persist the RAG index
tests/
  ComplianceCopilot.Tests/        xUnit — deterministic pieces only
docs/
  project-overview.html           full architecture write-up (public)
  testing.md                      manual walkthrough of every scenario (public)
content/raw/                      RAG source material (public regulator pages)
```

## Running it

*To be documented once the agent/MCP server are wired up.*
