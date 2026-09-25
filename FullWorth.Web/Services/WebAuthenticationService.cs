using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FullWorth.Web.Services;

public sealed class WebAuthenticationService
{
    private readonly IHttpClientFactory
        _httpClientFactory;

    public WebAuthenticationService(
        IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory =
            httpClientFactory;
    }

    public Task<AuthOperationResult>
        LoginAsync(
            HttpContext httpContext,
            string email,
            string password,
            bool rememberMe,
            CancellationToken cancellationToken = default)
    {
        return LoginAsync(
            httpContext,
            email,
            password,
            rememberMe,
            twoFactorCode: null,
            recoveryCode: null,
            cancellationToken);
    }

    public async Task<AuthOperationResult>
        LoginAsync(
            HttpContext httpContext,
            string email,
            string password,
            bool rememberMe,
            string? twoFactorCode,
            string? recoveryCode,
            CancellationToken cancellationToken = default)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return new AuthOperationResult(
                false,
                "Sign out before signing in to another account.");
        }

        email =
            email.Trim();

        twoFactorCode =
            NormalizeOptionalCode(
                twoFactorCode);

        recoveryCode =
            NormalizeOptionalCode(
                recoveryCode);

        var client =
            _httpClientFactory
                .CreateClient(
                    "FullWorthApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password,
                    twoFactorCode,
                    twoFactorRecoveryCode =
                        recoveryCode
                },
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode ==
                    HttpStatusCode.Unauthorized &&
                string.IsNullOrWhiteSpace(
                    twoFactorCode) &&
                string.IsNullOrWhiteSpace(
                    recoveryCode) &&
                await IsTwoFactorRequiredAsync(
                    response,
                    cancellationToken))
            {
                return AuthOperationResult
                    .TwoFactorRequired;
            }

            var submittedSecondFactor =
                !string.IsNullOrWhiteSpace(
                    twoFactorCode) ||
                !string.IsNullOrWhiteSpace(
                    recoveryCode);

            return new AuthOperationResult(
                false,
                submittedSecondFactor &&
                response.StatusCode ==
                    HttpStatusCode.Unauthorized
                    ? "The authenticator or recovery code is invalid."
                    : GetSafeLoginError(
                        response.StatusCode));
        }

        var tokenResponse =
            await ReadAccessTokenResponseAsync(
                response,
                cancellationToken);

        if (tokenResponse is null)
        {
            return new AuthOperationResult(
                false,
                "FullWorth received an invalid sign-in response.");
        }

        tokenResponse =
            await EnrollRefreshFamilyAsync(
                tokenResponse,
                cancellationToken);

        if (tokenResponse is null)
        {
            return new AuthOperationResult(
                false,
                "FullWorth could not establish a secure sign-in session.");
        }

        await SignInWebSessionAsync(
            httpContext,
            displayName:
                email,
            nameIdentifier:
                email,
            email:
                email,
            tokenResponse,
            rememberMe);

        return AuthOperationResult.Success;
    }

    public async Task<AuthOperationResult>
        LoginExternalAsync(
            HttpContext httpContext,
            string provider,
            string idToken,
            string providerSubject,
            string? email,
            CancellationToken cancellationToken = default)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return new AuthOperationResult(
                false,
                "Sign out before signing in to another account.");
        }

        provider =
            provider.Trim()
                .ToLowerInvariant();

        providerSubject =
            providerSubject.Trim();

        email =
            string.IsNullOrWhiteSpace(
                    email)
                ? null
                : email.Trim();

        if (string.IsNullOrWhiteSpace(
                provider) ||
            string.IsNullOrWhiteSpace(
                idToken) ||
            string.IsNullOrWhiteSpace(
                providerSubject))
        {
            return new AuthOperationResult(
                false,
                "External sign-in could not be completed.");
        }

        var client =
            _httpClientFactory
                .CreateClient(
                    "FullWorthApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/login",
                new
                {
                    provider,
                    idToken
                },
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AuthOperationResult(
                false,
                GetSafeExternalLoginError(
                    response.StatusCode));
        }

        var tokenResponse =
            await ReadAccessTokenResponseAsync(
                response,
                cancellationToken);

        if (tokenResponse is null)
        {
            return new AuthOperationResult(
                false,
                "FullWorth received an invalid external sign-in response.");
        }

        tokenResponse =
            await EnrollRefreshFamilyAsync(
                tokenResponse,
                cancellationToken);

        if (tokenResponse is null)
        {
            return new AuthOperationResult(
                false,
                "FullWorth could not establish a secure external sign-in session.");
        }

        await SignInWebSessionAsync(
            httpContext,
            displayName:
                email ?? "FullWorth user",
            nameIdentifier:
                $"{provider}:{providerSubject}",
            email,
            tokenResponse,
            rememberMe:
                false);

        return AuthOperationResult.Success;
    }

    public async Task<AuthOperationResult>
        RegisterAsync(
            HttpContext httpContext,
            string email,
            string password,
            bool acceptedTermsAndPrivacy,
            string legalTermsVersion,
            CancellationToken cancellationToken = default)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return new AuthOperationResult(
                false,
                "Sign out before creating another account.");
        }

        email =
            email.Trim();

        var client =
            _httpClientFactory
                .CreateClient(
                    "FullWorthApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password,
                    acceptedTermsAndPrivacy,
                    legalTermsVersion
                },
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error =
                await ReadRegistrationErrorAsync(
                    response,
                    cancellationToken);

            return new AuthOperationResult(
                false,
                error);
        }

        return await LoginAsync(
            httpContext,
            email,
            password,
            rememberMe: false,
            cancellationToken);
    }

    public async Task<AuthOperationResult>
        RequestPasswordResetAsync(
            string email,
            CancellationToken cancellationToken = default)
    {
        email =
            email.Trim();

        var client =
            _httpClientFactory
                .CreateClient(
                    "FullWorthApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/forgotPassword",
                new
                {
                    email
                },
                cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return AuthOperationResult.Success;
        }

        return new AuthOperationResult(
            false,
            response.StatusCode ==
                HttpStatusCode.TooManyRequests
                ? "Too many recovery attempts. Wait a minute and try again."
                : "FullWorth could not send a recovery email right now.");
    }

    public async Task<AuthOperationResult>
        ResetPasswordAsync(
            string email,
            string resetCode,
            string newPassword,
            CancellationToken cancellationToken = default)
    {
        email =
            email.Trim();

        resetCode =
            resetCode.Trim();

        var client =
            _httpClientFactory
                .CreateClient(
                    "FullWorthApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/resetPassword",
                new
                {
                    email,
                    resetCode,
                    newPassword
                },
                cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return AuthOperationResult.Success;
        }

        if (response.StatusCode ==
            HttpStatusCode.TooManyRequests)
        {
            return new AuthOperationResult(
                false,
                "Too many recovery attempts. Wait a minute and try again.");
        }

        var passwordError =
            await ReadPasswordValidationErrorAsync(
                response,
                cancellationToken);

        return new AuthOperationResult(
            false,
            passwordError ??
            "This password reset link is invalid or expired.");
    }

    public async Task<AuthOperationResult>
        ConfirmEmailAsync(
            string userId,
            string code,
            string? changedEmail,
            CancellationToken cancellationToken = default)
    {
        var query =
            $"?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}";

        if (!string.IsNullOrWhiteSpace(
                changedEmail))
        {
            query +=
                $"&changedEmail={Uri.EscapeDataString(changedEmail.Trim())}";
        }

        var client =
            _httpClientFactory
                .CreateClient(
                    "FullWorthApi");

        using var response =
            await client.GetAsync(
                "/api/auth/confirmEmail" + query,
                cancellationToken);

        return response.IsSuccessStatusCode
            ? AuthOperationResult.Success
            : new AuthOperationResult(
                false,
                "This email confirmation link is invalid or expired.");
    }

    public async Task LogoutAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            httpContext);

        try
        {
            var authenticateResult =
                await httpContext.AuthenticateAsync(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

            var refreshToken =
                authenticateResult.Properties?
                    .GetTokenValue(
                        "refresh_token");

            if (!string.IsNullOrWhiteSpace(
                    refreshToken))
            {
                var client =
                    _httpClientFactory.CreateClient(
                        "FullWorthApi");

                using var response =
                    await client.PostAsJsonAsync(
                        "/api/auth/logout",
                        new
                        {
                            refreshToken
                        },
                        cancellationToken);
            }
        }
        catch (HttpRequestException)
        {
            /*
             * A user must always be able to sign out locally. If the API is
             * unreachable, the 14-day refresh lifetime remains the remote
             * upper bound and a later explicit security action can revoke
             * all sessions through the Identity security stamp.
             */
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            /*
             * Treat an HttpClient timeout like an unavailable server. Local
             * sign-out still completes in the finally block.
             */
        }
        finally
        {
            await httpContext.SignOutAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);
        }
    }

    private async Task<AccessTokenResponse?>
        EnrollRefreshFamilyAsync(
            AccessTokenResponse tokenResponse,
            CancellationToken cancellationToken)
    {
        var client =
            _httpClientFactory.CreateClient(
                "FullWorthApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        tokenResponse.RefreshToken
                },
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await ReadAccessTokenResponseAsync(
            response,
            cancellationToken);
    }

    private static async Task<AccessTokenResponse?>
        ReadAccessTokenResponseAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        var tokenResponse =
            await response.Content
                .ReadFromJsonAsync<
                    AccessTokenResponse>(
                    cancellationToken:
                        cancellationToken);

        if (tokenResponse is null ||
            string.IsNullOrWhiteSpace(
                tokenResponse.AccessToken) ||
            string.IsNullOrWhiteSpace(
                tokenResponse.RefreshToken) ||
            tokenResponse.ExpiresIn <= 0)
        {
            return null;
        }

        return tokenResponse;
    }

    private static async Task
        SignInWebSessionAsync(
            HttpContext httpContext,
            string displayName,
            string nameIdentifier,
            string? email,
            AccessTokenResponse tokenResponse,
            bool rememberMe)
    {
        var claims =
            new List<Claim>
            {
                new(
                    ClaimTypes.Name,
                    displayName),

                new(
                    ClaimTypes.NameIdentifier,
                    nameIdentifier)
            };

        if (!string.IsNullOrWhiteSpace(
                email))
        {
            claims.Add(
                new Claim(
                    ClaimTypes.Email,
                    email));
        }

        var identity =
            new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        var principal =
            new ClaimsPrincipal(
                identity);

        var now =
            DateTimeOffset.UtcNow;

        var accessTokenExpiresAt =
            now.AddSeconds(
                tokenResponse.ExpiresIn);

        var properties =
            new AuthenticationProperties
            {
                IsPersistent =
                    rememberMe,

                AllowRefresh =
                    true,

                ExpiresUtc =
                    rememberMe
                        ? now.AddDays(30)
                        : now.AddHours(12)
            };

        properties.StoreTokens(
            [
                new AuthenticationToken
                {
                    Name =
                        "access_token",

                    Value =
                        tokenResponse.AccessToken
                },

                new AuthenticationToken
                {
                    Name =
                        "refresh_token",

                    Value =
                        tokenResponse.RefreshToken
                },

                new AuthenticationToken
                {
                    Name =
                        "expires_at",

                    Value =
                        accessTokenExpiresAt
                            .ToString("O")
                },

                new AuthenticationToken
                {
                    Name =
                        "token_type",

                    Value =
                        tokenResponse.TokenType
                }
            ]);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults
                .AuthenticationScheme,
            principal,
            properties);
    }

    private static async Task<bool>
        IsTwoFactorRequiredAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        var body =
            await response.Content
                .ReadAsStringAsync(
                    cancellationToken);

        if (string.IsNullOrWhiteSpace(
                body))
        {
            return false;
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    body);

            if (!document.RootElement
                    .TryGetProperty(
                        "detail",
                        out var detail))
            {
                return false;
            }

            return string.Equals(
                detail.GetString(),
                "RequiresTwoFactor",
                StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string?
        NormalizeOptionalCode(
            string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value.Trim();
    }

    private static string
        GetSafeLoginError(
            HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest =>
                "Email or password is incorrect.",

            HttpStatusCode.Unauthorized =>
                "Email or password is incorrect.",

            HttpStatusCode.Forbidden =>
                "Email or password is incorrect.",

            HttpStatusCode.TooManyRequests =>
                "Too many sign-in attempts. Wait a minute and try again.",

            _ =>
                "FullWorth could not sign you in right now."
        };
    }

    private static string
        GetSafeExternalLoginError(
            HttpStatusCode statusCode)
    {
        return statusCode ==
            HttpStatusCode.TooManyRequests
            ? "Too many sign-in attempts. Wait a minute and try again."
            : "External sign-in could not be completed. Sign in with email and password or try again.";
    }

    private static async Task<string?>
        ReadPasswordValidationErrorAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        var body =
            await response.Content
                .ReadAsStringAsync(
                    cancellationToken);

        if (string.IsNullOrWhiteSpace(
                body))
        {
            return null;
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    body);

            if (!document.RootElement.TryGetProperty(
                    "errors",
                    out var errors) ||
                errors.ValueKind !=
                    JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property
                in errors.EnumerateObject())
            {
                if (property.Name.Contains(
                        "InvalidToken",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (property.Value.ValueKind !=
                    JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item
                    in property.Value.EnumerateArray())
                {
                    var message =
                        item.GetString();

                    if (!string.IsNullOrWhiteSpace(
                            message) &&
                        !message.Contains(
                            "token",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return message;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static async Task<string>
        ReadRegistrationErrorAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        if (response.StatusCode ==
            HttpStatusCode.TooManyRequests)
        {
            return
                "Too many attempts. Wait a minute and try again.";
        }

        var body =
            await response.Content
                .ReadAsStringAsync(
                    cancellationToken);

        if (string.IsNullOrWhiteSpace(
                body))
        {
            return
                "FullWorth could not create the account.";
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    body);

            var root =
                document.RootElement;

            if (root.TryGetProperty(
                    "errors",
                    out var errors) &&
                errors.ValueKind ==
                    JsonValueKind.Object)
            {
                foreach (var property
                    in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind !=
                        JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var item
                        in property.Value
                            .EnumerateArray())
                    {
                        var message =
                            item.GetString();

                        if (!string.IsNullOrWhiteSpace(
                                message))
                        {
                            return message;
                        }
                    }
                }
            }

            if (root.TryGetProperty(
                    "detail",
                    out var detail))
            {
                var message =
                    detail.GetString();

                if (!string.IsNullOrWhiteSpace(
                        message))
                {
                    return message;
                }
            }

            if (root.TryGetProperty(
                    "title",
                    out var title))
            {
                var message =
                    title.GetString();

                if (!string.IsNullOrWhiteSpace(
                        message))
                {
                    return message;
                }
            }
        }
        catch (JsonException)
        {
            // Do not expose an unexpected raw server response.
        }

        return
            "FullWorth could not create the account. Check the information and try again.";
    }
}

public sealed record AuthOperationResult(
    bool Succeeded,
    string? ErrorMessage,
    bool RequiresTwoFactor = false)
{
    public static AuthOperationResult Success { get; } =
        new(
            true,
            null);

    public static AuthOperationResult TwoFactorRequired { get; } =
        new(
            false,
            null,
            true);
}

public sealed record AccessTokenResponse(
    string TokenType,
    string AccessToken,
    long ExpiresIn,
    string RefreshToken);
