using Microsoft.AspNetCore.Identity.Data;

namespace FullWorth.API.Services.Identity;

public sealed class IdentityEmailDeliveryException()
    : Exception("Identity email delivery failed.");

public sealed class AnonymousIdentityEmailDeliveryEndpointFilter(
    ILogger<AnonymousIdentityEmailDeliveryEndpointFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (IdentityEmailDeliveryException) when (
            !context.HttpContext.RequestAborted.IsCancellationRequested &&
            context.Arguments.Any(argument => argument is
                RegisterRequest or ForgotPasswordRequest or ResendConfirmationEmailRequest))
        {
            // Match the existing enumeration-safe public success response.
            // Do not attach the exception, recipient, action link or provider body.
            logger.LogWarning(new EventId(4101, "IdentityEmailDeliveryFailed"),
                "An identity email could not be delivered. Check the email provider health and configuration.");
            return Results.Ok();
        }
    }
}
