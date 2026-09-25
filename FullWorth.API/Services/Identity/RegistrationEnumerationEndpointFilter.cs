using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity.Data;

namespace FullWorth.API.Services.Identity;

public sealed class RegistrationEnumerationEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var result = await next(context);

        if (!context.Arguments.OfType<RegisterRequest>().Any())
        {
            return result;
        }

        // Identity returns Results<Ok, ValidationProblem>, so inspect the
        // contained result without replacing registration or its validation.
        var inner = result;
        while (inner is INestedHttpResult nested)
        {
            inner = nested.Result;
        }

        if (inner is not ValidationProblem problem ||
            !problem.ProblemDetails.Errors.Keys.Any(IsDuplicateIdentity))
        {
            return result;
        }

        var publicErrors = problem.ProblemDetails.Errors
            .Where(error => !IsDuplicateIdentity(error.Key))
            .ToDictionary(error => error.Key, error => error.Value);

        // Only conceal identity-existence errors. Other validation failures
        // remain failures; no existing account or password is modified.
        return publicErrors.Count == 0
            ? Results.Ok()
            : Results.ValidationProblem(publicErrors);
    }

    private static bool IsDuplicateIdentity(string code) =>
        code is "DuplicateEmail" or "DuplicateUserName";
}
