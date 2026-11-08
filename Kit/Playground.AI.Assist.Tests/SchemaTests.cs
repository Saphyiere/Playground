using System.Text.Json;

namespace Playground.AI.Assist.Tests;

public class SchemaTests
{
    sealed record WithCounts(Dictionary<string, int> Counts);

    sealed record WithAnything(object Anything);

    sealed record Branch(string Name, Branch? Inner);

    sealed record ManyMaybes(string? A, string? B, string? C, string? D, string? E, string? F, string? G, string? H, string? I, string? J,
        string? K, string? L, string? M, string? N, string? O, string? P, string? Q);

    static JsonElement Schema(Type type) => JsonSerializer.SerializeToElement(AssistSchema.For(type));

    static IEnumerable<JsonElement> Objects(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) yield break;
        if (node.TryGetProperty("properties", out _)) yield return node;
        foreach (var child in node.EnumerateObject().SelectMany(p => p.Value.ValueKind == JsonValueKind.Array ? [.. p.Value.EnumerateArray()] : new[] { p.Value }))
            foreach (var found in Objects(child)) yield return found;
    }

    [Fact]
    public void For_ClosesEveryObjectAndRequiresEveryProperty()
    {
        var objects = Objects(Schema(typeof(Reply<Plan>))).ToList();

        Assert.Equal(3, objects.Count);
        foreach (var obj in objects)
        {
            Assert.False(obj.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(obj.GetProperty("properties").EnumerateObject().Select(p => p.Name), obj.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        }
    }

    [Fact]
    public void For_NamesAndEnumValuesAreCamelCase_AndDescriptionsComeFromTheRecords()
    {
        var schema = Schema(typeof(Reply<Plan>));
        var lift = schema.GetProperty("properties").GetProperty("answer").GetProperty("properties").GetProperty("lifts").GetProperty("items").GetProperty("properties");

        Assert.Equal(["status", "summary", "notes", "answer"], schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Equal(["answered", "needMore", "cannot"], schema.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["up", "same", "down"], lift.GetProperty("change").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("One of the owner's exercises, by id.", lift.GetProperty("exerciseId").GetProperty("description").GetString());
        Assert.StartsWith("One or two plain sentences", schema.GetProperty("properties").GetProperty("summary").GetProperty("description").GetString());
    }

    [Fact]
    public void For_OnlyTheAnswerCanBeNull()
    {
        var properties = Schema(typeof(Reply<Plan>)).GetProperty("properties");

        Assert.Equal(["object", "null"], properties.GetProperty("answer").GetProperty("type").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal("string", properties.GetProperty("summary").GetProperty("type").GetString());
        Assert.Equal("array", properties.GetProperty("notes").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(typeof(WithCounts), "dictionary")]
    [InlineData(typeof(WithAnything), "any value")]
    [InlineData(typeof(Branch), "contains itself")]
    [InlineData(typeof(ManyMaybes), "17 properties")]
    public void For_RefusesWhatTheApiCantTake(Type type, string reason)
    {
        Assert.Contains(reason, Assert.Throws<InvalidOperationException>(() => AssistSchema.For(type)).Message);
    }

    [Fact]
    public void Json_ReadsARepliesFieldsAndRefusesANullWhereTheRecordHasNone()
    {
        var reply = JsonSerializer.Deserialize<Reply<Plan>>("""{"status":"Answered","summary":"Fine.","notes":[],"answer":{"lifts":[]}}""", AssistSchema.Json)!;

        Assert.Equal(ReplyStatus.Answered, reply.Status);
        Assert.Empty(reply.Answer!.Lifts);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Reply<Plan>>("""{"status":"answered","summary":null,"notes":[],"answer":null}""", AssistSchema.Json));
    }
}
