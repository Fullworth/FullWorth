using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Architecture;

public sealed class CrossDomainSchemaBoundaryTests
{
    [Fact]
    public void BillAlert_ChangeCorrelationIsNotAForeignKey()
    {
        using var dbContext =
            new FullWorthDbContext(
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"schema-boundary-{Guid.NewGuid():N}")
                    .Options);

        var alertEntity =
            dbContext.Model.FindEntityType(
                typeof(BillAlertEntity));

        Assert.NotNull(
            alertEntity);

        Assert.NotNull(
            alertEntity!.FindProperty(
                nameof(BillAlertEntity.BillChangeId)));

        Assert.Contains(
            alertEntity.GetIndexes(),
            index =>
                index.Properties.Count ==
                    1 &&
                index.Properties[0].Name ==
                    nameof(BillAlertEntity.BillChangeId));

        Assert.DoesNotContain(
            alertEntity.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                    typeof(BillChangeEntity));
    }

    [Fact]
    public void BillAlert_SourceEventIsOpaqueAndOwnerScoped()
    {
        using var dbContext =
            new FullWorthDbContext(
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"schema-boundary-{Guid.NewGuid():N}")
                    .Options);

        var alertEntity =
            dbContext.Model.FindEntityType(
                typeof(BillAlertEntity));

        Assert.NotNull(
            alertEntity);

        Assert.NotNull(
            alertEntity!.FindProperty(
                nameof(BillAlertEntity.SourceEventId)));

        Assert.Contains(
            alertEntity.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties
                    .Select(
                        property =>
                            property.Name)
                    .SequenceEqual(
                        [
                            nameof(BillAlertEntity.UserId),
                            nameof(BillAlertEntity.AlertType),
                            nameof(BillAlertEntity.SourceEventId)
                        ]));

        Assert.DoesNotContain(
            alertEntity.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                    typeof(PlanningPaycheckPlanRunEntity));
    }

    [Fact]
    public void BillTransactionAssociation_OwnsBillLinkWithoutPlaidForeignKey()
    {
        using var dbContext =
            new FullWorthDbContext(
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"schema-boundary-{Guid.NewGuid():N}")
                    .Options);

        var associationEntity =
            dbContext.Model.FindEntityType(
                typeof(BillTransactionAssociationEntity));

        Assert.NotNull(
            associationEntity);

        var primaryKey =
            associationEntity!.FindPrimaryKey();

        Assert.NotNull(
            primaryKey);

        Assert.Equal(
            new[]
            {
                nameof(BillTransactionAssociationEntity.UserId),
                nameof(BillTransactionAssociationEntity.BankTransactionId)
            },
            primaryKey!.Properties
                .Select(property => property.Name)
                .ToArray());

        Assert.Contains(
            associationEntity.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                    typeof(BillStreamEntity));

        Assert.DoesNotContain(
            associationEntity.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                    typeof(BankTransactionEntity));
    }
    [Fact]
    public void BankTransaction_DoesNotOwnBillStreamLink()
    {
        using var dbContext =
            new FullWorthDbContext(
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"schema-boundary-{Guid.NewGuid():N}")
                    .Options);

        var transactionEntity =
            dbContext.Model.FindEntityType(
                typeof(BankTransactionEntity));

        Assert.NotNull(
            transactionEntity);

        Assert.Null(
            transactionEntity!.FindProperty(
                "BillStreamId"));

        Assert.DoesNotContain(
            transactionEntity.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                    typeof(BillStreamEntity));
    }

}
