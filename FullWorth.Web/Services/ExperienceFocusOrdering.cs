namespace FullWorth.Web.Services;

public static class ExperienceFocusOrdering
{
    private static readonly string[] DefaultQuickLinks =
    [
        "transactions",
        "accounts",
        "activity"
    ];

    public static IReadOnlyList<string> OrderQuickLinks(
        IEnumerable<string>? focusAreas)
    {
        var selected =
            new HashSet<string>(
                focusAreas ?? [],
                StringComparer.OrdinalIgnoreCase);

        var scores =
            new Dictionary<string, int>(
                StringComparer.Ordinal)
            {
                ["transactions"] =
                    selected.Contains("Spending")
                        ? 0
                        : 1,

                ["accounts"] =
                    selected.Contains("AccountOverview")
                        ? 0
                        : 1,

                ["activity"] =
                    selected.Contains("BillChanges") ||
                    selected.Contains("RecurringCosts") ||
                    selected.Contains("Statements")
                        ? 0
                        : 1
            };

        return DefaultQuickLinks
            .Select(
                (key, index) =>
                    new
                    {
                        Key =
                            key,

                        Score =
                            scores[key],

                        DefaultOrder =
                            index
                    })
            .OrderBy(
                item =>
                    item.Score)
            .ThenBy(
                item =>
                    item.DefaultOrder)
            .Select(
                item =>
                    item.Key)
            .ToArray();
    }
}
