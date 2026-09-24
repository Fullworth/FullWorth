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
}
