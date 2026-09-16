using System.Text.Json;

namespace ComplianceCopilot.Shared.Domain;

/// <summary>
/// Deserializes the fixture JSON into a <see cref="MockDataSet"/>. Deliberately just
/// deserialization — no scoping/lookup behaviour, which belongs to the MCP server layer.
/// </summary>
public static class MockDataLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static MockDataSet LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MockDataSet>(json, Options)
            ?? throw new InvalidOperationException($"Mock data file '{path}' deserialized to null.");
    }
}
