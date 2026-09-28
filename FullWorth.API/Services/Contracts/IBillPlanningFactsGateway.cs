namespace FullWorth.API.Services.Contracts;

public sealed record BillPlanningFact(
    Guid BillStreamId,
    Guid StatementId,
    decimal Amount,
    string CurrencyCode,
    DateOnly? DueDate,
    DateOnly PeriodEnd);

public interface IBillPlanningFactsGateway
{
    Task<IReadOnlyDictionary<Guid, BillPlanningFact>> GetLatestAsync(
        Guid userId,
        IReadOnlyCollection<Guid> billStreamIds,
        CancellationToken cancellationToken = default);
}
