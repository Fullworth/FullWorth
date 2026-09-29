namespace FullWorth.API.Services.Contracts;

public enum PlanningPaydayAlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed record PlanningPaydayAlertRequest(
    Guid SourceEventId,
    PlanningPaydayAlertSeverity Severity,
    string Title,
    string Message);

public interface IPlanningPaydayAlertGateway
{
    Task EnsureAsync(
        Guid userId,
        PlanningPaydayAlertRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
