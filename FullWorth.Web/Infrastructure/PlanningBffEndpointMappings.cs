using FullWorth.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace FullWorth.Web.Infrastructure;

public static class PlanningBffEndpointMappings
{
    public static IEndpointRouteBuilder MapFullWorthPlanningBffEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(
            endpoints);

        var bff =
            endpoints
                .MapGroup(
                    "/bff/planning")
                .RequireAuthorization();

        bff.MapGet(
            "/pay-schedule",
            async (
                HttpContext context,
                FullWorthBffProxyService proxy) =>
                await proxy.ForwardGetAsync(
                    context,
                    "/api/planning/pay-schedule",
                    context.RequestAborted));

        bff.MapPut(
            "/pay-schedule",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService proxy,
                PlanningPayScheduleBffRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(
                    context);

                return await proxy.ForwardJsonAsync(
                    context,
                    HttpMethod.Put,
                    "/api/planning/pay-schedule",
                    request,
                    context.RequestAborted);
            });

        bff.MapGet(
            "/bill-funding-preferences",
            async (
                HttpContext context,
                FullWorthBffProxyService proxy) =>
                await proxy.ForwardGetAsync(
                    context,
                    "/api/planning/bill-funding-preferences",
                    context.RequestAborted));

        bff.MapPut(
            "/bill-funding-preferences/{billStreamId:guid}",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService proxy,
                Guid billStreamId,
                PlanningBillFundingPreferenceBffRequest request) =>
            {
                if (billStreamId ==
                    Guid.Empty)
                {
                    return Results.NotFound();
                }

                await antiforgery.ValidateRequestAsync(
                    context);

                return await proxy.ForwardJsonAsync(
                    context,
                    HttpMethod.Put,
                    $"/api/planning/bill-funding-preferences/{billStreamId:D}",
                    request,
                    context.RequestAborted);
            });

        bff.MapDelete(
            "/bill-funding-preferences/{billStreamId:guid}",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                FullWorthBffProxyService proxy,
                Guid billStreamId) =>
            {
                if (billStreamId ==
                    Guid.Empty)
                {
                    return Results.NotFound();
                }

                await antiforgery.ValidateRequestAsync(
                    context);

                return await proxy.ForwardDeleteAsync(
                    context,
                    $"/api/planning/bill-funding-preferences/{billStreamId:D}",
                    context.RequestAborted);
            });

        bff.MapGet(
            "/upcoming-bill-changes",
            async (
                HttpContext context,
                FullWorthBffProxyService proxy) =>
                await proxy.ForwardGetAsync(
                    context,
                    "/api/planning/upcoming-bill-changes",
                    context.RequestAborted));

        return endpoints;
    }
}

public sealed record PlanningPayScheduleBffRequest(
    string Frequency,
    DateOnly AnchorPayDate,
    int? SecondaryDayOfMonth,
    int DefaultPaychecksAhead);

public sealed record PlanningBillFundingPreferenceBffRequest(
    int PaychecksAheadOverride);
