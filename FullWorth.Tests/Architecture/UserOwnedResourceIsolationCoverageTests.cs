using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Services;
using Security = FullWorth.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Architecture;

public sealed class UserOwnedResourceIsolationCoverageTests
{
    /*
     * CrossUserIdentifierCoverageTests independently ratchets every
     * identifier-addressable non-admin API route. This model-wide check covers
     * the complementary persistence boundary so nested and non-route
     * UserId-scoped resources cannot silently appear without negative
     * cross-user evidence.
     */
    [Fact]
    public void EveryUserIdScopedEntity_HasNegativeIsolationEvidence()
    {
        using var dbContext =
            new FullWorthDbContext(
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"ownership-coverage-{Guid.NewGuid():N}")
                    .Options);

        var evidence =
            new Dictionary<Type, IsolationEvidence>
            {
                [typeof(BankAccountEntity)] =
                    Evidence<Security.FinancialDataOwnershipTests>(
                        nameof(Security.FinancialDataOwnershipTests.FinancialLists_ReturnOnlyAuthenticatedUsersData)),

                [typeof(BankConnectionEntity)] =
                    Evidence<Security.FinancialDataOwnershipTests>(
                        nameof(Security.FinancialDataOwnershipTests.DisconnectingAnotherUsersConnection_ReturnsNotFoundAndDoesNotModifyIt)),

                [typeof(BankTransactionEntity)] =
                    Evidence<Security.FinancialDataOwnershipTests>(
                        nameof(Security.FinancialDataOwnershipTests.FinancialLists_ReturnOnlyAuthenticatedUsersData)),

                [typeof(BillAlertEntity)] =
                    Evidence<Security.BillAlertOwnershipTests>(
                        nameof(Security.BillAlertOwnershipTests.OtherUser_CannotSeeAlert)),

                [typeof(BillChangeEntity)] =
                    Evidence<Security.AccountDataExportTests>(
                        nameof(Security.AccountDataExportTests.ExportAccountData_ReturnsOnlyOwnedSafeData)),

                [typeof(BillLineItemEntity)] =
                    Evidence<Security.AccountDataExportTests>(
                        nameof(Security.AccountDataExportTests.ExportAccountData_ReturnsOnlyOwnedSafeData)),

                [typeof(BillStatementAiEvaluationEntity)] =
                    Evidence<Security.AccountDataExportTests>(
                        nameof(Security.AccountDataExportTests.ExportAccountData_ReturnsOnlyOwnedSafeData)),

                [typeof(BillStatementEntity)] =
                    Evidence<Security.AccountDataExportTests>(
                        nameof(Security.AccountDataExportTests.ExportAccountData_ReturnsOnlyOwnedSafeData)),

                [typeof(BillStatementUploadEntity)] =
                    Evidence<Security.BillStatementUploadStatusAuthorizationTests>(
                        nameof(Security.BillStatementUploadStatusAuthorizationTests.Status_CannotReadAnotherUsersUpload)),

                [typeof(BillStreamEntity)] =
                    Evidence<Security.BillStreamOwnershipTests>(
                        nameof(Security.BillStreamOwnershipTests.Detail_ForAnotherUsersStream_ReturnsNotFound)),

                [typeof(BillTransactionAssociationEntity)] =
                    Evidence<Security.AccountDataExportTests>(
                        nameof(Security.AccountDataExportTests.ExportAccountData_ReturnsOnlyOwnedSafeData)),

                [typeof(PlaidLinkSessionEntity)] =
                    Evidence<Security.PlaidOwnershipBoundaryTests>(
                        nameof(Security.PlaidOwnershipBoundaryTests.OtherUsersHostedLinkSession_CannotBeCompleted)),

                [typeof(PlanningBillFundingPreferenceEntity)] =
                    Evidence<Security.PlanningAuthorizationTests>(
                        nameof(Security.PlanningAuthorizationTests.BillPreference_CrossUserIdManipulation_ReturnsNotFoundWithoutMutation)),

                [typeof(PlanningPayScheduleEntity)] =
                    Evidence<Security.PlanningAuthorizationTests>(
                        nameof(Security.PlanningAuthorizationTests.PaySchedule_IsScopedToAuthenticatedUser)),

                [typeof(PlanningPaycheckAllocationEntity)] =
                    Evidence<PlanningPaycheckAllocationStoreTests>(
                        nameof(PlanningPaycheckAllocationStoreTests.GetForPaycheckAsync_IsOwnershipScoped)),

                [typeof(PlanningPaycheckPlanRunEntity)] =
                    Evidence<Security.UserOwnedResourceIsolationTests>(
                        nameof(Security.UserOwnedResourceIsolationTests.RecentPaydayPlans_DoNotExposeAnotherUsersHistory)),

                [typeof(SubscriptionAccessKeyRedemptionEntity)] =
                    Evidence<AccountSubscriptionDeletionGatewayTests>(
                        nameof(AccountSubscriptionDeletionGatewayTests.ApplyOwnedDataDeletionAsync_PreservesOtherUsersSubscriptionState)),

                [typeof(SubscriptionEntitlementEntity)] =
                    Evidence<Security.UserOwnedResourceIsolationTests>(
                        nameof(Security.UserOwnedResourceIsolationTests.SubscriptionStatus_DoesNotExposeAnotherUsersEntitlement)),

                [typeof(UserProgramMembershipEntity)] =
                    Evidence<AccountSubscriptionDeletionGatewayTests>(
                        nameof(AccountSubscriptionDeletionGatewayTests.ApplyOwnedDataDeletionAsync_PreservesOtherUsersSubscriptionState))
            };

        var userOwnedEntityTypes =
            dbContext.Model
                .GetEntityTypes()
                .Where(
                    entityType =>
                        entityType.FindProperty(
                            "UserId") is not null)
                .Select(
                    entityType =>
                        entityType.ClrType)
                .Where(
                    entityType =>
                        entityType.Namespace ==
                        typeof(BillStreamEntity).Namespace)
                .OrderBy(
                    entityType =>
                        entityType.FullName,
                    StringComparer.Ordinal)
                .ToArray();

        var uncovered =
            userOwnedEntityTypes
                .Where(
                    entityType =>
                        !evidence.ContainsKey(
                            entityType))
                .Select(
                    entityType =>
                        entityType.Name)
                .ToArray();

        var stale =
            evidence.Keys
                .Where(
                    entityType =>
                        !userOwnedEntityTypes.Contains(
                            entityType))
                .Select(
                    entityType =>
                        entityType.Name)
                .OrderBy(
                    name =>
                        name,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.True(
            uncovered.Length == 0,
            "Every UserId-scoped persistence entity must be assigned to " +
            "negative cross-user evidence. Missing: " +
            string.Join(
                ", ",
                uncovered));

        Assert.True(
            stale.Length == 0,
            "Ownership evidence contains entity types that are no longer " +
            "UserId-scoped. Review rather than silently carrying stale coverage: " +
            string.Join(
                ", ",
                stale));

        foreach (var item in
                 evidence.OrderBy(
                     pair =>
                         pair.Key.FullName,
                     StringComparer.Ordinal))
        {
            var method =
                item.Value.TestClass.GetMethod(
                    item.Value.MethodName);

            Assert.NotNull(
                method);

            var isExecutableTest =
                method.GetCustomAttributes(
                        inherit: true)
                    .Any(
                        attribute =>
                            attribute.GetType().Name is
                                "FactAttribute" or
                                "TheoryAttribute");

            Assert.True(
                isExecutableTest,
                $"{item.Key.Name} points to " +
                $"{item.Value.TestClass.Name}.{item.Value.MethodName}, " +
                "but that method is not an executable xUnit test.");
        }
    }

    private static IsolationEvidence Evidence<TTest>(
        string methodName) =>
        new(
            typeof(TTest),
            methodName);

    private sealed record IsolationEvidence(
        Type TestClass,
        string MethodName);
}
