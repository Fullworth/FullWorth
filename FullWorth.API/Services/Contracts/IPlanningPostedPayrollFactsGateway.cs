namespace FullWorth.API.Services.Contracts;

public sealed record PlanningPostedPayrollFact(
    Guid TransactionId,
    decimal Amount,
    DateOnly PostedDate,
    string? CurrencyCode);

public interface IPlanningPostedPayrollFactsGateway
{
    Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(
        Guid userId,
        DateOnly fromInclusive,
        DateOnly throughInclusive,
        CancellationToken cancellationToken = default);
}
