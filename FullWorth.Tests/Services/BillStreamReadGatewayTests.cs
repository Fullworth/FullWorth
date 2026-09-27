using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BillStreamReadGatewayTests
{
    [Fact]
    public async Task ListOwnedActiveAsync_ReturnsOnlyOwnedActiveStreams_InStableOrder()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"bill-stream-read-{Guid.NewGuid():N}")
                .Options;

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var alphaId =
            Guid.NewGuid();

        var betaId =
            Guid.NewGuid();

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            seed.BillStreams.AddRange(
                CreateStream(
                    betaId,
                    userId,
                    "Beta Utility",
                    BillCategory.Utility,
                    isActive: true),
                CreateStream(
                    alphaId,
                    userId,
                    "Alpha Internet",
                    BillCategory.Internet,
                    isActive: true),
                CreateStream(
                    Guid.NewGuid(),
                    userId,
                    "Inactive Mobile",
                    BillCategory.MobilePhone,
                    isActive: false),
                CreateStream(
                    Guid.NewGuid(),
                    otherUserId,
                    "Other User Electric",
                    BillCategory.Electricity,
                    isActive: true));

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new BillStreamReadGateway(
                dbContext);

        var result =
            await gateway.ListOwnedActiveAsync(
                userId);

        Assert.Collection(
            result,
            alpha =>
            {
                Assert.Equal(
                    alphaId,
                    alpha.BillStreamId);

                Assert.Equal(
                    "Alpha Internet",
                    alpha.ProviderName);

                Assert.Equal(
                    BillCategory.Internet,
                    alpha.Category);
            },
            beta =>
            {
                Assert.Equal(
                    betaId,
                    beta.BillStreamId);

                Assert.Equal(
                    "Beta Utility",
                    beta.ProviderName);

                Assert.Equal(
                    BillCategory.Utility,
                    beta.Category);
            });
    }

    [Fact]
    public async Task ListOwnedActiveAsync_RejectsEmptyUserId()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"bill-stream-read-validation-{Guid.NewGuid():N}")
                .Options;

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new BillStreamReadGateway(
                dbContext);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                gateway.ListOwnedActiveAsync(
                    Guid.Empty));
    }

    private static BillStreamEntity CreateStream(
        Guid id,
        Guid userId,
        string providerName,
        BillCategory category,
        bool isActive)
    {
        var now =
            DateTimeOffset.UtcNow;

        return new BillStreamEntity
        {
            Id = id,
            UserId = userId,
            ProviderName = providerName,
            Category = category,
            Source = BillStreamSource.Manual,
            IsActive = isActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
}
