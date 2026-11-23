using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Aggregation;

public sealed record SanitizationResult(IReadOnlyList<InventoryRecord> Records, IReadOnlyList<string> Problems);

/// <summary>
/// Turns raw upstream records into validated ones. A record that cannot be trusted (missing or negative values) is
/// excluded and explained rather than being guessed at or allowed to corrupt a total. Pure and HTTP-free.
/// </summary>
public static class RecordSanitizer
{
    /// <summary>Validates raw records, retaining usable records and reporting excluded ones.</summary>
    /// <param name="raw">The records returned by the upstream API.</param>
    /// <returns>The validated records and descriptions of records that were excluded.</returns>
    public static SanitizationResult Sanitize(IEnumerable<RawInventoryRecord?> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var records = new List<InventoryRecord>();
        var problems = new List<string>();
        var position = 0;

        foreach (var candidate in raw)
        {
            var record = TryConvert(candidate, out var problem);
            if (record is null)
            {
                var label = candidate?.Id is { } id ? $"id {id}" : "unknown id";
                problems.Add($"Record at position {position} ({label}) excluded: {problem}.");
            }
            else
            {
                records.Add(record);
            }

            position++;
        }

        return new SanitizationResult(records, problems);
    }

    private static InventoryRecord? TryConvert(RawInventoryRecord? raw, out string problem)
    {
        if (raw is null)
        {
            problem = "the record is null";
            return null;
        }

        problem = (raw.Id, raw.Name, raw.Category, raw.Price, raw.Quantity, raw.LastUpdated) switch
        {
            (null, _, _, _, _, _) => "id is missing",
            (_, var name, _, _, _, _) when string.IsNullOrWhiteSpace(name) => "name is missing or blank",
            (_, _, var cat, _, _, _) when string.IsNullOrWhiteSpace(cat) => "category is missing or blank",
            (_, _, _, null, _, _) => "price is missing",
            (_, _, _, < 0m, _, _) => "price is negative",
            (_, _, _, _, null, _) => "quantity is missing",
            (_, _, _, _, < 0, _) => "quantity is negative",
            (_, _, _, _, _, null) => "lastUpdated is missing",
            _ => string.Empty
        };

        return problem.Length > 0
            ? null
            : new InventoryRecord(
                raw.Id!.Value,
                raw.Name!.Trim(),
                raw.Category!.Trim(),
                raw.Price!.Value,
                raw.Quantity!.Value,
                raw.LastUpdated!.Value.UtcDateTime);
    }
}
