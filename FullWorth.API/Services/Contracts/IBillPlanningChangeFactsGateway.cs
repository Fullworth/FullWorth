namespace FullWorth.API.Services.Contracts;

public sealed record BillPlanningChangeFact(
    Guid BillStreamId,
    Guid ChangeId,
    Guid? PreviousStatementId,
    Guid CurrentStatementId,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal AmountDifference,
    decimal AnnualizedImpact,
    string Description,
    DateOnly CurrentPeriodEnd);

public interface IBillPlanningChangeFactsGateway
{
    Task<IReadOnlyDictionary<Guid, BillPlanningChangeFact>> GetLatestConfirmedAsync(
        Guid userId,
        IReadOnlyCollection<Guid> billStreamIds,
        CancellationToken cancellationToken = default);
}
