using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Data;

public sealed class PlanningPersistenceModelTests
{
    [Fact]
    public void Model_KeepsPlanningOwnerScopedWithoutBillsForeignKey()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"planning-model-{Guid.NewGuid():N}")
                .Options;

        using var context =
            new FullWorthDbContext(
                options);

        var paySchedule =
            context.Model.FindEntityType(
                typeof(
                    PlanningPayScheduleEntity));

        Assert.NotNull(
            paySchedule);

        Assert.Contains(
            paySchedule.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties
                    .Select(
                        property =>
                            property.Name)
                    .SequenceEqual(
                        [
                            nameof(
                                PlanningPayScheduleEntity.UserId)
                        ]));

        Assert.Contains(
            paySchedule.GetKeys(),
            key =>
                key.Properties
                    .Select(
                        property =>
                            property.Name)
                    .SequenceEqual(
                        [
                            nameof(
                                PlanningPayScheduleEntity.Id),
                            nameof(
                                PlanningPayScheduleEntity.UserId)
                        ]));

        var fundingPreference =
            context.Model.FindEntityType(
                typeof(
                    PlanningBillFundingPreferenceEntity));

        Assert.NotNull(
            fundingPreference);

        Assert.Contains(
            fundingPreference.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties
                    .Select(
                        property =>
                            property.Name)
                    .SequenceEqual(
                        [
                            nameof(
                                PlanningBillFundingPreferenceEntity.UserId),
                            nameof(
                                PlanningBillFundingPreferenceEntity.BillStreamId)
                        ]));

        Assert.Contains(
            fundingPreference.GetKeys(),
            key =>
                key.Properties
                    .Select(
                        property =>
                            property.Name)
                    .SequenceEqual(
                        [
                            nameof(
                                PlanningBillFundingPreferenceEntity.Id),
                            nameof(
                                PlanningBillFundingPreferenceEntity.UserId)
                        ]));

        var preferenceForeignKey =
            Assert.Single(
                fundingPreference.GetForeignKeys());

        Assert.Equal(
            typeof(
                ApplicationUser),
            preferenceForeignKey.PrincipalEntityType.ClrType);

        Assert.Equal(
            [
                nameof(
                    PlanningBillFundingPreferenceEntity.UserId)
            ],
            preferenceForeignKey.Properties
                .Select(
                    property =>
                        property.Name));

        Assert.DoesNotContain(
            fundingPreference.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType ==
                typeof(
                    BillStreamEntity));
    }
}
