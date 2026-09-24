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
    public void BankTransaction_BillLinkLivesInBillsOwnedAssociation()
    {
        using var dbContext =
            new FullWorthDbContext(
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"transaction-link-schema-{Guid.NewGuid():N}")
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

        var linkEntity =
            dbContext.Model.FindEntityType(
                typeof(BillTransactionLinkEntity));

        Assert.NotNull(
            linkEntity);

        Assert.NotNull(
            linkEntity!.FindProperty(
                nameof(BillTransactionLinkEntity.BankTransactionId)));

        Assert.NotNull(
            linkEntity.FindProperty(
                nameof(BillTransactionLinkEntity.BillStreamId)));

        Assert.Contains(
            linkEntity.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                    typeof(BillStreamEntity));

        Assert.DoesNotContain(
            linkEntity.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                    typeof(BankTransactionEntity));
    }

}
