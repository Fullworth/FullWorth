using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Plaid;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FullWorth.Tests.Services;

public sealed class RecurringBillDiscoveryPersistenceServiceTests
{
    [Fact]
    public async Task StableMonthlyUnclassifiedService_IsPromotedAsOther()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Cloud Service",
            9.99m,
            categoryPrimary: null,
            categoryDetailed: null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext,
                new PlaidBankTransactionDiscoveryGateway(
                    dbContext),
                new BillTransactionAssociationGateway(
                    dbContext));

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            1,
            result.BillsDiscovered);

        Assert.Equal(
            1,
            result.RecurringCandidatesDetected);

        Assert.Equal(
            0,
            result.RecurringCandidatesRejected);

        Assert.Equal(
            1,
            result.BillStreamsCreated);

        var billStream =
            Assert.Single(
                dbContext.BillStreams);

        Assert.Equal(
            BillCategory.Other,
            billStream.Category);

        Assert.True(
            billStream.IsActive);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Contains(
                    dbContext.BillTransactionAssociations,
                    association =>
                        association.BankTransactionId == transaction.Id &&
                        association.BillStreamId == billStream.Id));
    }

    [Fact]
    public async Task VariableKnownUtilityWithoutPlaidCategory_IsPromotedAsUtility()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Black Hills Energy",
            new DateOnly(2026, 1, 5),
            118.42m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Black Hills Energy",
            new DateOnly(2026, 2, 5),
            176.19m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Black Hills Energy",
            new DateOnly(2026, 3, 5),
            143.77m,
            null,
            null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext,
                new PlaidBankTransactionDiscoveryGateway(
                    dbContext),
                new BillTransactionAssociationGateway(
                    dbContext));

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            1,
            result.BillsDiscovered);

        Assert.Equal(
            1,
            result.BillStreamsCreated);

        var billStream =
            Assert.Single(
                dbContext.BillStreams);

        Assert.Equal(
            BillCategory.Utility,
            billStream.Category);

        Assert.Equal(
            "Black Hills Energy",
            billStream.ProviderName);

        Assert.True(
            billStream.IsActive);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Contains(
                    dbContext.BillTransactionAssociations,
                    association =>
                        association.BankTransactionId == transaction.Id &&
                        association.BillStreamId == billStream.Id));
    }

    [Fact]
    public async Task TwoMonthlyDigitalSubscriptionCharges_ArePromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Example Digital Subscription",
            new DateOnly(2026, 1, 12),
            8.99m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_DIGITAL_GOODS");

        AddTransaction(
            dbContext,
            userId,
            "Example Digital Subscription",
            new DateOnly(2026, 2, 12),
            8.99m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_DIGITAL_GOODS");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext,
                new PlaidBankTransactionDiscoveryGateway(
                    dbContext),
                new BillTransactionAssociationGateway(
                    dbContext));

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            1,
            result.BillsDiscovered);

        var billStream =
            Assert.Single(
                dbContext.BillStreams);

        Assert.Equal(
            BillCategory.Other,
            billStream.Category);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Contains(
                    dbContext.BillTransactionAssociations,
                    association =>
                        association.BankTransactionId == transaction.Id &&
                        association.BillStreamId == billStream.Id));
    }

    [Fact]
    public async Task TwoMonthlyGeneralMerchandisePurchases_AreNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Example Store",
            new DateOnly(2026, 1, 12),
            15m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE");

        AddTransaction(
            dbContext,
            userId,
            "Example Store",
            new DateOnly(2026, 2, 12),
            15m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext,
                new PlaidBankTransactionDiscoveryGateway(
                    dbContext),
                new BillTransactionAssociationGateway(
                    dbContext));

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Equal(
            1,
            result.RecurringCandidatesDetected);

        Assert.Equal(
            1,
            result.RecurringCandidatesRejected);

        Assert.Empty(
            dbContext.BillStreams);
    }

    [Fact]
    public async Task StableMonthlyRestaurant_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Restaurant",
            20m,
            "FOOD_AND_DRINK",
            "FOOD_AND_DRINK_RESTAURANT");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext,
                new PlaidBankTransactionDiscoveryGateway(
                    dbContext),
                new BillTransactionAssociationGateway(
                    dbContext));

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);

        Assert.Empty(
            dbContext.BillTransactionAssociations);
    }

    [Fact]
    public async Task StableMonthlyGeneralMerchandiseWithoutSubscriptionEvidence_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Store",
            15m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext,
                new PlaidBankTransactionDiscoveryGateway(
                    dbContext),
                new BillTransactionAssociationGateway(
                    dbContext));

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);
    }

    [Fact]
    public async Task VariableUnclassifiedMonthlySpending_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 1, 5),
            10m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 2, 5),
            20m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 3, 5),
            30m,
            null,
            null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext,
                new PlaidBankTransactionDiscoveryGateway(
                    dbContext),
                new BillTransactionAssociationGateway(
                    dbContext));

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("pending")]
    [InlineData("credit")]
    public async Task Discovery_WithoutEligibleOwnedCharges_PreservesBillAndAssociation(
        string transactionState)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var anotherUserId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMonths(-2);

        var bill = new BillStreamEntity
        {
            UserId = userId,
            ProviderName = "Known Utility",
            Category = BillCategory.Utility,
            Source = BillStreamSource.AutomaticDiscovery,
            IsActive = true,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        };
        dbContext.BillStreams.Add(bill);

        var transaction = AddTransaction(
            dbContext, userId, "Known Utility",
            new DateOnly(2026, 1, 5), 95m,
            "RENT_AND_UTILITIES",
            "RENT_AND_UTILITIES_GAS_AND_ELECTRICITY");

        switch (transactionState)
        {
            case "removed":
                transaction.IsRemoved = true;
                break;
            case "pending":
                transaction.IsPending = true;
                break;
            case "credit":
                transaction.Amount = -95m;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transactionState));
        }

        dbContext.BillTransactionAssociations.Add(
            new BillTransactionAssociationEntity
            {
                UserId = userId,
                BankTransactionId = transaction.Id,
                BillStreamId = bill.Id,
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = createdAt
            });

        // Another person's valid monthly charges cannot justify deactivating
        // this user's bill or make the no-evidence protection disappear.
        AddMonthlyTransactions(dbContext, anotherUserId,
            "Another User's Internet", 70m,
            "RENT_AND_UTILITIES",
            "RENT_AND_UTILITIES_INTERNET_AND_CABLE");

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = new RecurringBillDiscoveryPersistenceService(
            dbContext,
            new PlaidBankTransactionDiscoveryGateway(dbContext),
            new BillTransactionAssociationGateway(dbContext));

        var result = await service.DiscoverAndSaveAsync(userId);

        Assert.Equal(0, result.TransactionsAnalyzed);
        Assert.Equal(0, result.BillsDiscovered);
        Assert.Equal(0, result.BillStreamsDeactivated);
        Assert.Equal(0, result.TransactionsUnlinked);
        Assert.Equal(0, result.NewBillAlertsCreated);

        dbContext.ChangeTracker.Clear();
        var saved = await dbContext.BillStreams.SingleAsync();
        Assert.True(saved.IsActive);
        Assert.Equal(BillStreamSource.AutomaticDiscovery, saved.Source);
        Assert.Equal(createdAt, saved.UpdatedAtUtc);

        var savedAssociation =
            await dbContext.BillTransactionAssociations.SingleAsync();
        Assert.Equal(userId, savedAssociation.UserId);
        Assert.Equal(bill.Id, savedAssociation.BillStreamId);
        Assert.Equal(transaction.Id, savedAssociation.BankTransactionId);
    }

    [Fact]
    public async Task MultipleDiscoveries_LinkCorrectStreams_AndDeactivateStaleStream()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var now =
            DateTimeOffset.UtcNow;

        var staleStream =
            new BillStreamEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    userId,

                ProviderName =
                    "Old Service",

                Category =
                    BillCategory.Other,

                Source =
                    BillStreamSource.AutomaticDiscovery,

                IsActive =
                    true,

                CreatedAtUtc =
                    now.AddMonths(-4),

                UpdatedAtUtc =
                    now.AddMonths(-1)
            };

        dbContext.BillStreams.Add(
            staleStream);

        var staleTransaction =
            AddTransaction(
                dbContext,
                userId,
                "Old Service",
                new DateOnly(2026, 1, 2),
                4.99m,
                null,
                null);

        dbContext.BillTransactionAssociations.Add(
            new BillTransactionAssociationEntity
            {
                UserId = userId,
                BankTransactionId = staleTransaction.Id,
                BillStreamId = staleStream.Id,
                CreatedAtUtc = now.AddMonths(-1),
                UpdatedAtUtc = now.AddMonths(-1)
            });

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Cloud One",
            9.99m,
            null,
            null);

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Cloud Two",
            19.99m,
            null,
            null);

        foreach (var transaction in dbContext.ChangeTracker.Entries<BankTransactionEntity>())
            transaction.Entity.BankAccountId = staleTransaction.BankAccountId;

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext,
                new PlaidBankTransactionDiscoveryGateway(
                    dbContext),
                new BillTransactionAssociationGateway(
                    dbContext));

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            2,
            result.BillsDiscovered);

        Assert.Equal(
            2,
            result.BillStreamsCreated);

        Assert.Equal(
            2,
            result.NewBillAlertsCreated);

        Assert.Equal(
            2,
            dbContext.BillAlerts.Count(
                alert =>
                    alert.AlertType ==
                        BillAlertType.NewBill));

        Assert.Equal(
            1,
            result.BillStreamsDeactivated);

        Assert.Equal(
            6,
            result.TransactionsLinked);

        Assert.Equal(
            1,
            result.TransactionsUnlinked);

        Assert.False(
            staleStream.IsActive);

        Assert.DoesNotContain(
            dbContext.BillTransactionAssociations,
            association =>
                association.BankTransactionId == staleTransaction.Id);

        var cloudOne =
            Assert.Single(
                dbContext.BillStreams
                    .Where(
                        stream =>
                            stream.ProviderName ==
                                "Example Cloud One"));

        var cloudTwo =
            Assert.Single(
                dbContext.BillStreams
                    .Where(
                        stream =>
                            stream.ProviderName ==
                                "Example Cloud Two"));

        Assert.All(
            dbContext.BankTransactions
                .Where(
                    transaction =>
                        transaction.MerchantName ==
                            "Example Cloud One"),
            transaction =>
                Assert.Contains(
                    dbContext.BillTransactionAssociations,
                    association =>
                        association.BankTransactionId == transaction.Id &&
                        association.BillStreamId == cloudOne.Id));

        Assert.All(
            dbContext.BankTransactions
                .Where(
                    transaction =>
                        transaction.MerchantName ==
                            "Example Cloud Two"),
            transaction =>
                Assert.Contains(
                    dbContext.BillTransactionAssociations,
                    association =>
                        association.BankTransactionId == transaction.Id &&
                        association.BillStreamId == cloudTwo.Id));
    }

    [Fact]
    public async Task DiscoveryDiagnostics_CountReasonsWithoutExposingOtherUserHistory()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var anotherUserId = Guid.NewGuid();

        AddMonthlyTransactions(dbContext, userId, "Black Hills Energy",
            99m, null, null);

        AddTransaction(dbContext, userId, "Two Observations",
            new DateOnly(2026, 1, 12), 19m, null, null);
        AddTransaction(dbContext, userId, "Two Observations",
            new DateOnly(2026, 2, 12), 19m, null, null);

        AddMonthlyTransactions(dbContext, userId, "Example Restaurant",
            24m, "FOOD_AND_DRINK", "FOOD_AND_DRINK_RESTAURANT");

        AddTransaction(dbContext, userId, "Nonmonthly",
            new DateOnly(2026, 1, 12), 10m, null, null);
        AddTransaction(dbContext, userId, "Nonmonthly",
            new DateOnly(2026, 1, 20), 10m, null, null);

        AddTransaction(dbContext, userId, "One Off",
            new DateOnly(2026, 1, 1), 10m, null, null);

        var pending = AddTransaction(dbContext, userId, "Pending",
            new DateOnly(2026, 3, 6), 10m, null, null);
        pending.IsPending = true;
        AddTransaction(dbContext, userId, " ",
            new DateOnly(2026, 3, 6), 10m, null, null);

        AddMonthlyTransactions(dbContext, anotherUserId, "Other User Bills",
            10m, null, null);
        await dbContext.SaveChangesAsync();

        var service = new RecurringBillDiscoveryPersistenceService(
            dbContext,
            new PlaidBankTransactionDiscoveryGateway(dbContext),
            new BillTransactionAssociationGateway(dbContext));

        var result = await service.DiscoverAndSaveAsync(userId);
        var diagnostics = Assert.IsType<RecurringBillDiscoveryDiagnostics>(
            result.Diagnostics);

        Assert.Equal(13, diagnostics.ImportedTransactions);
        Assert.Equal(1, diagnostics.ExcludedPendingOrNonDebitTransactions);
        Assert.Equal(1, diagnostics.ExcludedMissingMerchantTransactions);
        Assert.Equal(11, diagnostics.EligiblePostedTransactions);
        Assert.Equal(new DateOnly(2026, 1, 1),
            diagnostics.FirstEligiblePostedDate);
        Assert.Equal(new DateOnly(2026, 3, 5),
            diagnostics.LastEligiblePostedDate);
        Assert.Equal(1, diagnostics.SingleObservationGroups);
        Assert.Equal(1, diagnostics.GroupsWithoutSupportedMonthlyCadence);
        Assert.Equal(3, diagnostics.DetectedMonthlyCandidates);
        Assert.Equal(1, diagnostics.RejectedExplicitFallbackCategories);
        Assert.Equal(1, diagnostics.RejectedInsufficientTwoObservationEvidence);
        Assert.Equal(0, diagnostics.RejectedOtherWeakOrVariableEvidence);
        Assert.Equal(1, diagnostics.AcceptedCandidates);
        Assert.Equal(1, result.BillsDiscovered);
        Assert.Equal(2, result.RecurringCandidatesRejected);

        // The summary must be safe for an authenticated user-facing API to
        // consume later; no raw description, account or bank identifiers.
        Assert.DoesNotContain(
            diagnostics.GetType().GetProperties(),
            property =>
                property.Name.Contains("MerchantName", StringComparison.Ordinal)
                || property.Name.Contains("ProviderName", StringComparison.Ordinal)
                || property.Name.Contains("AccountNumber", StringComparison.Ordinal)
                || property.Name.Contains("TransactionId", StringComparison.Ordinal)
                || property.Name.Contains("BankAccountId", StringComparison.Ordinal)
                || property.Name.Contains("Amount", StringComparison.Ordinal)
                || property.Name.EndsWith("Id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadOnlyDiagnostics_MatchDiscoveryAndNeverChangeFinancialState()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var anotherUserId = Guid.NewGuid();

        AddMonthlyTransactions(dbContext, userId, "Black Hills Energy",
            118m, null, null);
        AddTransaction(dbContext, anotherUserId, "Unrelated Payment",
            new DateOnly(2026, 5, 9), 15m, null, null);

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = new RecurringBillDiscoveryPersistenceService(
            dbContext,
            new PlaidBankTransactionDiscoveryGateway(dbContext),
            new BillTransactionAssociationGateway(dbContext));

        var diagnostics = await service.GetDiagnosticsAsync(userId);
        Assert.Equal(3, diagnostics.ImportedTransactions);
        Assert.Equal(3, diagnostics.EligiblePostedTransactions);
        Assert.Equal(1, diagnostics.DetectedMonthlyCandidates);
        Assert.Equal(1, diagnostics.AcceptedCandidates);
        Assert.Equal(new DateOnly(2026, 1, 5),
            diagnostics.FirstEligiblePostedDate);
        Assert.Equal(new DateOnly(2026, 3, 5),
            diagnostics.LastEligiblePostedDate);

        var otherDiagnostics = await service.GetDiagnosticsAsync(anotherUserId);
        Assert.Equal(1, otherDiagnostics.ImportedTransactions);
        Assert.Equal(1, otherDiagnostics.SingleObservationGroups);
        Assert.Equal(0, otherDiagnostics.AcceptedCandidates);

        // Neither owner read may stage an association, generate an alert,
        // deactivate a stream or write anything to the database.
        Assert.Empty(dbContext.BillStreams);
        Assert.Empty(dbContext.BillTransactionAssociations);
        Assert.Empty(dbContext.BillAlerts);
        Assert.False(dbContext.ChangeTracker.HasChanges());

        var persisted = await service.DiscoverAndSaveAsync(userId);
        Assert.Equal(diagnostics, persisted.Diagnostics);
        Assert.Equal(1, persisted.BillsDiscovered);
    }

    [Fact]
    public async Task Diagnostics_ReportDuplicateOwnedProviderBlockingInsteadOfClaimingPersistence()
    {
        await using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        AddMonthlyTransactions(db, owner, "Black Hills Energy", 85m, null, null);
        AddMonthlyTransactions(db, otherOwner, "Black Hills Energy", 90m, null, null);
        db.BillStreams.AddRange(
            new BillStreamEntity
            {
                UserId = owner, ProviderName = "Black Hills Energy",
                IsActive = true, Source = BillStreamSource.Manual
            },
            new BillStreamEntity
            {
                UserId = owner, ProviderName = "  black hills energy  ",
                IsActive = true, Source = BillStreamSource.Manual
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new RecurringBillDiscoveryPersistenceService(db,
            new PlaidBankTransactionDiscoveryGateway(db),
            new BillTransactionAssociationGateway(db));

        var before = await service.GetDiagnosticsAsync(owner);
        Assert.Equal(1, before.AcceptedCandidates);
        Assert.Equal(1, before.BlockedByExistingBillAmbiguity);
        Assert.Equal(0, (await service.GetDiagnosticsAsync(otherOwner))
            .BlockedByExistingBillAmbiguity);
        Assert.False(db.ChangeTracker.HasChanges());

        var result = await service.DiscoverAndSaveAsync(owner);
        Assert.Equal(0, result.BillsDiscovered);
        Assert.Equal(1, result.RecurringCandidatesRejected);
        Assert.Equal(before, result.Diagnostics);
        Assert.Equal(2, await db.BillStreams.CountAsync());
        Assert.Empty(db.BillTransactionAssociations);
    }

    [Fact]
    public async Task Diagnostics_ReportPreexistingAssociationConflictWithoutChangingOwnership()
    {
        await using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var stream = new BillStreamEntity
        {
            UserId = owner, ProviderName = "Another provider",
            IsActive = true, Source = BillStreamSource.Manual
        };
        db.BillStreams.Add(stream);
        AddMonthlyTransactions(db, owner, "Black Hills Energy", 85m, null, null);
        await db.SaveChangesAsync();
        var transactionId = await db.BankTransactions
            .Where(item => item.UserId == owner)
            .Select(item => item.Id)
            .FirstAsync();
        db.BillTransactionAssociations.Add(new BillTransactionAssociationEntity
        {
            UserId = owner, BankTransactionId = transactionId,
            BillStreamId = stream.Id
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new RecurringBillDiscoveryPersistenceService(db,
            new PlaidBankTransactionDiscoveryGateway(db),
            new BillTransactionAssociationGateway(db));
        var before = await service.GetDiagnosticsAsync(owner);
        Assert.Equal(1, before.AcceptedCandidates);
        Assert.Equal(1, before.BlockedByExistingBillAmbiguity);
        Assert.False(db.ChangeTracker.HasChanges());

        var result = await service.DiscoverAndSaveAsync(owner);
        Assert.Equal(0, result.BillsDiscovered);
        Assert.Equal(before, result.Diagnostics);
        Assert.Equal(stream.Id,
            (await db.BillTransactionAssociations.SingleAsync()).BillStreamId);
        Assert.Single(db.BillStreams);
    }

    [Fact]
    public async Task ReadOnlyDiagnostics_RejectsMissingAuthenticatedUserId()
    {
        await using var dbContext = CreateDbContext();
        var service = new RecurringBillDiscoveryPersistenceService(
            dbContext,
            new PlaidBankTransactionDiscoveryGateway(dbContext),
            new BillTransactionAssociationGateway(dbContext));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetDiagnosticsAsync(Guid.Empty));
    }

    private static FullWorthDbContext CreateDbContext()
    {
        return new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"recurring-discovery-{Guid.NewGuid():N}")
                .Options);
    }

    private static void AddMonthlyTransactions(
        FullWorthDbContext dbContext,
        Guid userId,
        string merchantName,
        decimal amount,
        string? categoryPrimary,
        string? categoryDetailed)
    {
        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 1, 5),
            amount,
            categoryPrimary,
            categoryDetailed);

        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 2, 5),
            amount,
            categoryPrimary,
            categoryDetailed);

        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 3, 5),
            amount,
            categoryPrimary,
            categoryDetailed);
    }

    private static BankTransactionEntity AddTransaction(
        FullWorthDbContext dbContext,
        Guid userId,
        string merchantName,
        DateOnly postedDate,
        decimal amount,
        string? categoryPrimary,
        string? categoryDetailed)
    {
        var transaction =
            new BankTransactionEntity
            {
                UserId =
                    userId,

                BankAccountId =
                    Guid.NewGuid(),

                PlaidTransactionId =
                    Guid.NewGuid()
                        .ToString("N"),

                Name =
                    merchantName,

                MerchantName =
                    merchantName,

                Amount =
                    amount,

                IsoCurrencyCode =
                    "USD",

                PostedDate =
                    postedDate,

                AuthorizedDate =
                    postedDate,

                IsPending =
                    false,

                IsRemoved =
                    false,

                CategoryPrimary =
                    categoryPrimary,

                CategoryDetailed =
                    categoryDetailed
            };

        dbContext.BankTransactions.Add(
            transaction);

        return transaction;
    }
}
