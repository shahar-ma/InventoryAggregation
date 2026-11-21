using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Aggregation;

public sealed record SanitizationResult(IReadOnlyList<InventoryRecord> Records, IReadOnlyList<string> Problems);

/// <summary>
/// Turns raw upstream records into validated ones. A record that cannot be trusted (missing or negative values) is
/// excluded and explained rather than being guessed at or allowed to corrupt a total. Pure and HTTP-free.
/// </summary>
public static class RecordSanitizer
{
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
        problem = string.Empty;

        if (raw is null)
        {
            problem = "the record is null";
            return null;
        }

        if (raw.Id is not { } id)
        {
            problem = "id is missing";
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw.Name))
        {
            problem = "name is missing or blank";
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw.Category))
        {
            problem = "category is missing or blank";
            return null;
        }

        if (raw.Price is not { } price)
        {
            problem = "price is missing";
            return null;
        }

        if (price < 0m)
        {
            problem = "price is negative";
            return null;
        }

        if (raw.Quantity is not { } quantity)
        {
            problem = "quantity is missing";
            return null;
        }

        if (quantity < 0)
        {
            problem = "quantity is negative";
            return null;
        }

        if (raw.LastUpdated is not { } lastUpdated)
        {
            problem = "lastUpdated is missing";
            return null;
        }

        return new InventoryRecord(id, raw.Name.Trim(), raw.Category.Trim(), price, quantity, lastUpdated.UtcDateTime);
    }
}
