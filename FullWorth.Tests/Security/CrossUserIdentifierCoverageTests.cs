using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FullWorth.Tests.Security;

public sealed class CrossUserIdentifierCoverageTests
{
    private static readonly IReadOnlyDictionary<string, CrossUserEvidence>
        ExpectedCoverage =
            new Dictionary<string, CrossUserEvidence>(
                StringComparer.Ordinal)
            {
                ["DELETE|/api/bank-connections/{connectionId:guid}"] =
                    new(
                        typeof(FinancialDataOwnershipTests),
                        nameof(
                            FinancialDataOwnershipTests
                                .DisconnectingAnotherUsersConnection_ReturnsNotFoundAndDoesNotModifyIt)),
                ["DELETE|/api/planning/bill-funding-preferences/{billStreamId:guid}"] =
                    new(
                        typeof(PlanningAuthorizationTests),
                        nameof(
                            PlanningAuthorizationTests
                                .BillPreference_CrossUserIdManipulation_ReturnsNotFoundWithoutMutation)),
                ["GET|/api/bill-streams/{billStreamId:guid}"] =
                    new(
                        typeof(BillStreamOwnershipTests),
                        nameof(
                            BillStreamOwnershipTests
                                .Detail_ForAnotherUsersStream_ReturnsNotFound)),
                ["GET|/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}"] =
                    new(
                        typeof(BillStatementUploadStatusAuthorizationTests),
                        nameof(
                            BillStatementUploadStatusAuthorizationTests
                                .Status_CannotReadAnotherUsersUpload)),
                ["GET|/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file"] =
                    new(
                        typeof(BillStatementUploadStatusAuthorizationTests),
                        nameof(
                            BillStatementUploadStatusAuthorizationTests
                                .File_CannotReadAnotherUsersStatement)),
                ["POST|/api/alerts/{alertId:guid}/dismiss"] =
                    new(
                        typeof(BillAlertOwnershipTests),
                        nameof(
                            BillAlertOwnershipTests
                                .OtherUser_CannotDismissAlert)),
                ["POST|/api/alerts/{alertId:guid}/read"] =
                    new(
                        typeof(BillAlertOwnershipTests),
                        nameof(
                            BillAlertOwnershipTests
                                .OtherUser_CannotMarkAlertRead)),
                ["POST|/api/bill-streams/{billStreamId:guid}/statement-uploads"] =
                    new(
                        typeof(BillStatementUploadAuthorizationTests),
                        nameof(
                            BillStatementUploadAuthorizationTests
                                .Upload_CannotTargetAnotherUsersBillStream)),
                ["POST|/api/plaid/connections/{connectionId:guid}/accounts/sync"] =
                    new(
                        typeof(PlaidOwnershipBoundaryTests),
                        nameof(
                            PlaidOwnershipBoundaryTests
                                .OtherUsersConnection_CannotBeUsedForAccountSync)),
                ["POST|/api/plaid/connections/{connectionId:guid}/transactions/sync"] =
                    new(
                        typeof(PlaidOwnershipBoundaryTests),
                        nameof(
                            PlaidOwnershipBoundaryTests
                                .OtherUsersConnection_CannotBeUsedForTransactionSync)),
                ["POST|/api/plaid/connections/{connectionId:guid}/update-link-token"] =
                    new(
                        typeof(PlaidOwnershipBoundaryTests),
                        nameof(
                            PlaidOwnershipBoundaryTests
                                .OtherUsersConnection_CannotCreateUpdateModeLinkSession)),
                ["POST|/api/plaid/link-session/{sessionId:guid}/complete"] =
                    new(
                        typeof(PlaidOwnershipBoundaryTests),
                        nameof(
                            PlaidOwnershipBoundaryTests
                                .OtherUsersHostedLinkSession_CannotBeCompleted)),
                ["PUT|/api/planning/bill-funding-preferences/{billStreamId:guid}"] =
                    new(
                        typeof(PlanningAuthorizationTests),
                        nameof(
                            PlanningAuthorizationTests
                                .BillPreference_CrossUserIdManipulation_ReturnsNotFoundWithoutMutation)),
                ["PUT|/api/planning/payday-plans/{payrollTransactionId:guid}"] =
                    new(
                        typeof(PlanningPaydayPlanAuthorizationTests),
                        nameof(
                            PlanningPaydayPlanAuthorizationTests
                                .PaydayPlan_CrossUserPayrollId_ReturnsNotFoundWithoutAllocation))
            };

    [Fact]
    public void UserIdentifierRoutes_HaveExplicitTwoUserNegativeEvidence()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var endpointDataSource =
            factory.Services.GetRequiredService<EndpointDataSource>();

        var actual =
            endpointDataSource.Endpoints
                .OfType<RouteEndpoint>()
                .Where(
                    endpoint =>
                    {
                        var route =
                            "/" +
                            (endpoint.RoutePattern.RawText ??
                             string.Empty)
                            .TrimStart('/');

                        return route.StartsWith(
                                   "/api/",
                                   StringComparison.Ordinal) &&
                               !route.StartsWith(
                                   "/api/admin/",
                                   StringComparison.Ordinal) &&
                               route.Contains(
                                   ":guid}",
                                   StringComparison.Ordinal);
                    })
                .SelectMany(
                    endpoint =>
                    {
                        var route =
                            "/" +
                            (endpoint.RoutePattern.RawText ??
                             string.Empty)
                            .TrimStart('/');

                        var methods =
                            endpoint.Metadata
                                .GetMetadata<HttpMethodMetadata>()?
                                .HttpMethods ??
                            [];

                        return methods.Select(
                            method =>
                                $"{method}|{route}");
                    })
                .OrderBy(
                    value =>
                        value,
                    StringComparer.Ordinal)
                .ToArray();

        var expected =
            ExpectedCoverage.Keys
                .OrderBy(
                    value =>
                        value,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            expected,
            actual);

        foreach (var pair in ExpectedCoverage)
        {
            var method =
                pair.Value.TestType.GetMethod(
                    pair.Value.MethodName);

            Assert.True(
                method is not null,
                $"{pair.Key} references missing test " +
                $"{pair.Value.TestType.Name}.{pair.Value.MethodName}.");

            Assert.True(
                method!
                    .GetCustomAttributes(
                        inherit: true)
                    .Any(
                        attribute =>
                            attribute is FactAttribute),
                $"{pair.Key} evidence method is not an executable xUnit test.");
        }
    }

    private sealed record CrossUserEvidence(
        Type TestType,
        string MethodName);
}
