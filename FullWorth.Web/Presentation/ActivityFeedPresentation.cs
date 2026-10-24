using FullWorth.Web.Components.Pages.App;

namespace FullWorth.Web.Presentation;

public static class ActivityFeedPresentation
{
    public static bool IsAttentionSeverity(string? severity) =>
        string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase);

    public static bool IsConnectionIssue(string? alertType) =>
        string.Equals(alertType, "ConnectionIssue", StringComparison.OrdinalIgnoreCase);

    public static string SeverityClass(string? severity) =>
        severity?.ToLowerInvariant() switch
        {
            "warning" => "warning",
            "critical" => "critical",
            _ => "info"
        };

    // Destinations are built from typed IDs, never from alert titles, messages or provider URLs.
    public static string? GetReviewDestination(Guid? billId, string? alertType) =>
        billId is { } id && id != Guid.Empty ? $"/app/bills/{id:D}" :
        IsConnectionIssue(alertType) ? "/app/account" : null;

    public static IReadOnlyList<Activity.BillAlertViewModel> Filter(
        IEnumerable<Activity.BillAlertViewModel> alerts, Activity.ActivityFilter filter, string? search)
    {
        var query = search?.Trim() ?? string.Empty;
        return alerts.Where(alert => !alert.IsDismissed)
            .Where(alert => filter switch
            {
                Activity.ActivityFilter.Unread => !alert.IsRead,
                Activity.ActivityFilter.Attention => IsAttentionSeverity(alert.Severity),
                Activity.ActivityFilter.ConnectionIssues => IsConnectionIssue(alert.AlertType),
                _ => true
            })
            .Where(alert => query.Length == 0 ||
                (alert.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (alert.Message?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }
}
