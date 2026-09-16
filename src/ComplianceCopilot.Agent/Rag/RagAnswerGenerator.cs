using System.Text;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace ComplianceCopilot.Agent.Rag;

/// <summary>
/// Generates a draft answer over retrieved chunks, with an inline [chunk:ID] citation
/// required after every factual claim. This commit does not verify those citations are
/// actually honest yet - that deterministic grounding check is commit 9's job. This is the
/// "LLM proposes" half only.
/// </summary>
public sealed class RagAnswerGenerator(OllamaClients ollama)
{
    private const string SystemPrompt = """
        You are a compliance assistant answering questions about company registration and
        statutory obligations. Answer the user's question using ONLY the information in the
        numbered context chunks provided below - do not use any outside knowledge.

        For every factual claim you make, add a citation marker immediately after it in the
        exact form [chunk:ID], using the ID shown before the chunk you drew that claim from.
        A sentence with no citation marker will be treated as unsupported.

        If the context chunks do not contain enough information to answer the question, say so
        plainly instead of guessing.
        """;

    public async Task<string> GenerateAsync(
        string query,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken ct = default)
    {
        if (chunks.Count == 0)
        {
            return "I don't have enough information in my knowledge base to answer that.";
        }

        var contextBlock = string.Join(
            "\n\n",
            chunks.Select(r => $"[chunk:{r.Chunk.ChunkId}]\n{r.Chunk.Text}"));

        var userPrompt = $"Context:\n\n{contextBlock}\n\nQuestion: {query}";

        var request = new ChatRequest
        {
            Model = ollama.Chat.SelectedModel,
            Messages =
            [
                new Message(ChatRole.System, SystemPrompt),
                new Message(ChatRole.User, userPrompt),
            ],
            Stream = false,
        };

        var sb = new StringBuilder();
        await foreach (var response in ollama.Chat.ChatAsync(request, ct))
        {
            if (response?.Message?.Content is { } content)
                sb.Append(content);
        }

        return sb.ToString();
    }
}
