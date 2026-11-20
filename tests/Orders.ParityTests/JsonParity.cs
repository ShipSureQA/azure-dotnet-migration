using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Orders.ParityTests;

/// <summary>
/// Semantic JSON comparison used for the parity (contract) checks.
/// - Objects: same property names (order ignored), values compared recursively.
/// - Arrays: same length and order.
/// - Numbers: compared by value, so PHP's 53.3 equals .NET's 53.30 (documented normalisation).
/// - Strings, booleans, null: exact.
/// </summary>
public static class JsonParity
{
    public const string TimestampPlaceholder = "<timestamp>";

    private static readonly Regex UtcTimestamp = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Diff(JsonElement expected, JsonElement actual)
    {
        var differences = new List<string>();
        Compare(expected, actual, "$", differences);
        return differences;
    }

    /// <summary>
    /// Replaces the createdAt value of a newly created order (it contains "now") with a
    /// placeholder, after checking it has the legacy format.
    /// </summary>
    public static JsonElement NormalizeVolatile(JsonElement element, IReadOnlyCollection<string> volatileFields)
    {
        if (volatileFields.Count == 0 || element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        var node = JsonNode.Parse(element.GetRawText())!.AsObject();
        foreach (var field in volatileFields)
        {
            var value = node[field]?.GetValue<string>();
            if (value is null || !UtcTimestamp.IsMatch(value))
            {
                throw new InvalidOperationException($"'{field}' should be a UTC timestamp like 2026-11-20T03:00:05Z but was '{value}'.");
            }

            node[field] = TimestampPlaceholder;
        }

        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static void Compare(JsonElement expected, JsonElement actual, string path, List<string> differences)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            differences.Add($"{path}: expected {expected.ValueKind} {Short(expected)} but got {actual.ValueKind} {Short(actual)}");
            return;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedNames = expected.EnumerateObject().Select(p => p.Name).ToHashSet();
                var actualNames = actual.EnumerateObject().Select(p => p.Name).ToHashSet();
                foreach (var missing in expectedNames.Except(actualNames))
                {
                    differences.Add($"{path}.{missing}: missing");
                }

                foreach (var extra in actualNames.Except(expectedNames))
                {
                    differences.Add($"{path}.{extra}: unexpected property");
                }

                foreach (var name in expectedNames.Intersect(actualNames))
                {
                    Compare(expected.GetProperty(name), actual.GetProperty(name), $"{path}.{name}", differences);
                }

                break;

            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                if (expectedItems.Count != actualItems.Count)
                {
                    differences.Add($"{path}: expected {expectedItems.Count} items but got {actualItems.Count}");
                    return;
                }

                for (var i = 0; i < expectedItems.Count; i++)
                {
                    Compare(expectedItems[i], actualItems[i], $"{path}[{i}]", differences);
                }

                break;

            case JsonValueKind.Number:
                var same = expected.TryGetDecimal(out var e) && actual.TryGetDecimal(out var a)
                    ? e == a
                    : expected.GetRawText() == actual.GetRawText();
                if (!same)
                {
                    differences.Add($"{path}: expected {expected.GetRawText()} but got {actual.GetRawText()}");
                }

                break;

            case JsonValueKind.String:
                if (expected.GetString() != actual.GetString())
                {
                    differences.Add($"{path}: expected \"{expected.GetString()}\" but got \"{actual.GetString()}\"");
                }

                break;
        }
    }

    private static string Short(JsonElement element)
    {
        var raw = element.GetRawText();
        return raw.Length > 40 ? raw[..40] + "..." : raw;
    }
}
