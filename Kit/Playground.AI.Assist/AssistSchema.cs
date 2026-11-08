using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Playground.AI.Assist;

/// <summary>
/// The JSON a task's records travel as, and the JSON schema the API holds an answer to. The schema follows the API's rules
/// for structured outputs: every object closed (<c>additionalProperties: false</c>) with every property required; no
/// recursive types, dictionaries or untyped values; no number or length bounds, which go in the description and the task's
/// checks instead; and at most 16 properties that can be one of several types, where a nullable one counts.
/// </summary>
public static class AssistSchema
{
    /// <summary>
    /// How records go out and come back: camelCase names and enum values, nulls only where a record says a property can be
    /// null, and nothing a record doesn't have.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    const int MostUnions = 16;
    static readonly string[] Formats = ["date-time", "time", "date", "duration", "email", "hostname", "uri", "ipv4", "ipv6", "uuid"];
    static readonly string[] NotTaken = ["$schema", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength",
        "maxLength", "pattern", "minItems", "maxItems", "uniqueItems"];
    static readonly ConcurrentDictionary<Type, Dictionary<string, JsonElement>> Made = new();

    /// <summary>The schema for <paramref name="type"/>, made the first time and kept.</summary>
    /// <exception cref="InvalidOperationException">The type breaks one of the API's rules; the message says where.</exception>
    public static Dictionary<string, JsonElement> For(Type type) => Made.GetOrAdd(type, t =>
    {
        var exported = Json.GetJsonSchemaAsNode(t, new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                var described = context.PropertyInfo?.AttributeProvider ?? (context.PropertyInfo is null ? context.TypeInfo.Type : null);
                var description = described?.GetCustomAttributes(typeof(DescriptionAttribute), true).OfType<DescriptionAttribute>().FirstOrDefault();
                if (description is not null && node is JsonObject obj && !obj.ContainsKey("description")) obj.Insert(0, "description", description.Description);
                return node;
            },
        });
        var schema = Inline(exported, exported, t, 0);
        var unions = Tighten(schema, t, "$");
        if (unions > MostUnions) throw new InvalidOperationException($"{t.Name} has {unions} properties that can be one of several types (a nullable one counts); the API allows {MostUnions}.");
        return schema.AsObject().ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));
    });

    /// <summary>
    /// A copy of the schema with what each <c>$ref</c> points to in its place: the exporter refers back to a type it has
    /// already written, and the API wants each one written out. A type that contains itself would never end, so it's refused.
    /// </summary>
    static JsonNode Inline(JsonNode node, JsonNode root, Type type, int depth)
    {
        if (depth > 100) throw new InvalidOperationException($"{type.Name} contains itself: the API doesn't take recursive types.");
        switch (node)
        {
            case JsonObject obj when obj["$ref"] is JsonValue reference:
                var target = root;
                foreach (var part in reference.GetValue<string>().TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
                    target = target is JsonArray array ? array[int.Parse(part)]! : target[part.Replace("~1", "/").Replace("~0", "~")]!;
                // What sits beside the $ref (a description, or a type that adds null) goes over the copy.
                if (Inline(target, root, type, depth + 1) is not JsonObject resolved) return target.DeepClone();
                foreach (var (key, value) in obj.Where(p => p.Key != "$ref")) resolved[key] = value is null ? null : Inline(value, root, type, depth + 1);
                return resolved;
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj) copy[key] = value is null ? null : Inline(value, root, type, depth + 1);
                return copy;
            case JsonArray array:
                return new JsonArray([.. array.Select(item => item is null ? null : Inline(item, root, type, depth + 1))]);
            default:
                return node.DeepClone();
        }
    }

    /// <summary>
    /// Closes every object and makes all its properties required, drops the keywords the API doesn't take, refuses what it
    /// can't have, and counts the properties that can be one of several types.
    /// </summary>
    static int Tighten(JsonNode? node, Type type, string path)
    {
        if (node is not JsonObject obj) throw new InvalidOperationException($"{type.Name} allows any value at {path}: give it a type.");
        foreach (var keyword in NotTaken) obj.Remove(keyword);
        if (obj["format"] is JsonValue format && !Formats.Contains(format.GetValue<string>())) obj.Remove("format");
        var types = obj["type"] is JsonArray list ? list.Select(t => t?.GetValue<string>()).ToList() : [obj["type"]?.GetValue<string>()];
        var unions = obj["type"] is JsonArray || obj["anyOf"] is not null ? 1 : 0;
        if (types.Contains("object"))
        {
            if (obj["additionalProperties"] is JsonObject) throw new InvalidOperationException($"{type.Name} has a dictionary at {path}: use a list of records.");
            if (obj["properties"] is not JsonObject properties) obj["properties"] = properties = [];
            obj["additionalProperties"] = false;
            obj["required"] = new JsonArray([.. properties.Select(p => (JsonNode)p.Key)]);
            foreach (var (name, property) in properties) unions += Tighten(property, type, $"{path}.{name}");
        }

        if (types.Contains("array")) unions += Tighten(obj["items"], type, $"{path}[]");
        if (obj["anyOf"] is JsonArray options) foreach (var option in options) unions += Tighten(option, type, path);
        return unions;
    }
}
