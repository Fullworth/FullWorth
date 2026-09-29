namespace FullWorth.API.Services.Contracts;

public sealed record PlanningPaydayAlertRefreshResult(
    int PayrollFactsScanned,
    int ReadyPlansEnsured,
    int SkippedFacts,
    bool PayScheduleRequired);

public interface IPlanningPaydayAlertRefreshGateway
{
    Task<PlanningPaydayAlertRefreshResult> RefreshAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
