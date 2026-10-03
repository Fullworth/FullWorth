using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using FullWorth.Web.Components;
using FullWorth.Web.Infrastructure;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.AspNetCore.RateLimiting;

const long StatementMultipartBodyLimit =
    16L * 1024 * 1024;

const long DefaultRequestBodyLimit =
    1L * 1024 * 1024;

const int MaximumRequestLineBytes =
    8 * 1024;

const int MaximumRequestHeaderBytes =
    32 * 1024;

var webCulture =
    CultureInfo.GetCultureInfo(
        "en-US");

CultureInfo.DefaultThreadCurrentCulture =
    webCulture;

CultureInfo.DefaultThreadCurrentUICulture =
    webCulture;

var builder =
    WebApplication.CreateBuilder(args);

builder.Configuration.AddKeyPerFile(
    "/run/secrets",
    optional: true);

builder.WebHost.ConfigureKestrel(
    options =>
    {
        options.AddServerHeader =
            false;

        // Keep ordinary request payloads bounded. Statement uploads set their own 16 MiB endpoint limit.
        options.Limits.MaxRequestBodySize =
            DefaultRequestBodyLimit;

        options.Limits.MaxRequestLineSize =
            MaximumRequestLineBytes;

        options.Limits.MaxRequestHeadersTotalSize =
            MaximumRequestHeaderBytes;
    });

builder.Services.AddLocalization(
    options =>
    {
        options.ResourcesPath =
            "Resources";
    });

builder.Services.Configure<RequestLocalizationOptions>(
    options =>
    {
        var spanishCulture =
            CultureInfo.GetCultureInfo(
                "es");

        options.DefaultRequestCulture =
            new RequestCulture(
                culture: webCulture,
                uiCulture: webCulture);

        /*
         * Keep formatting culture on en-US for now.
         *
         * FullWorth still has several USD values formatted through the
         * current culture. Allowing a browser language to change
         * CurrentCulture could make a USD amount display with the wrong
         * currency symbol. UI language is therefore localized independently
         * through CurrentUICulture until all money presentation is explicitly
         * currency-code-aware.
         */
        options.SupportedCultures =
            [
                webCulture
            ];

        options.SupportedUICultures =
            [
                webCulture,
                spanishCulture
            ];

        options.ApplyCurrentCultureToResponseHeaders =
            true;

        options.RequestCultureProviders.Clear();

        options.RequestCultureProviders.Add(
            new CookieRequestCultureProvider());

        options.RequestCultureProviders.Add(
            new CustomRequestCultureProvider(
                context =>
                {
                    var uiCulture =
                        ResolveUiCulture(
                            context.Request.Headers
                                .AcceptLanguage
                                .ToString());

                    return Task.FromResult<ProviderCultureResult?>(
                        new ProviderCultureResult(
                            culture: webCulture.Name,
                            uiCulture: uiCulture));
                }));
    });

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddCascadingAuthenticationState();

var authenticationBuilder =
    builder.Services
        .AddAuthentication(
            CookieAuthenticationDefaults
                .AuthenticationScheme)
        .AddCookie(
            options =>
            {
                options.Cookie.Name =
                    "__Host-BillWatch.Web.Auth";

                options.Cookie.HttpOnly =
                    true;

                options.Cookie.SecurePolicy =
                    CookieSecurePolicy.Always;

                options.Cookie.SameSite =
                    SameSiteMode.Lax;

                options.Cookie.Path =
                    "/";

                options.LoginPath =
                    "/login";

                options.AccessDeniedPath =
                    "/login";

                options.SlidingExpiration =
                    false;

                options.Events.OnRedirectToLogin =
                    context =>
                    {
                        if (context.Request.Path
                            .StartsWithSegments(
                                "/bff"))
                        {
                            context.Response.StatusCode =
                                StatusCodes
                                    .Status401Unauthorized;

                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(
                            context.RedirectUri);

                        return Task.CompletedTask;
                    };

                options.Events.OnRedirectToAccessDenied =
                    context =>
                    {
                        if (context.Request.Path
                            .StartsWithSegments(
                                "/bff"))
                        {
                            context.Response.StatusCode =
                                StatusCodes
                                    .Status403Forbidden;

                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(
                            context.RedirectUri);

                        return Task.CompletedTask;
                    };
            });

authenticationBuilder
    .AddFullWorthExternalAuthentication(
        builder.Configuration);

builder.Services.AddFullWorthWebSessionStore(
    builder.Configuration,
    builder.Environment);

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();

builder.Services.AddAntiforgery(
    options =>
    {
        options.HeaderName =
            "X-CSRF-TOKEN";

        options.Cookie.Name =
            "__Host-BillWatch.Web.Antiforgery";

        options.Cookie.HttpOnly =
            true;

        options.Cookie.SecurePolicy =
            CookieSecurePolicy.Always;

        options.Cookie.SameSite =
            SameSiteMode.Strict;

        options.Cookie.Path =
            "/";

        options.Cookie.IsEssential =
            true;
    });

builder.Services.Configure<FormOptions>(
    options =>
    {
        options.MultipartBodyLengthLimit =
            StatementMultipartBodyLimit;
    });

builder.Services.AddRateLimiter(
    options =>
    {
        options.RejectionStatusCode =
            StatusCodes.Status429TooManyRequests;

        options.OnRejected =
            static (
                context,
                _) =>
            {
                if (context.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var retryAfter))
                {
                    var seconds =
                        Math.Max(
                            1d,
                            Math.Ceiling(
                                retryAfter.TotalSeconds));

                    context.HttpContext.Response.Headers[
                        "Retry-After"] =
                        seconds.ToString(
                            CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

        options.AddPolicy(
            BffEndpointMappings.StatementUploadRateLimitPolicy,
            httpContext =>
                CreateFixedWindowPartition(
                    GetRateLimitPartitionKey(
                        httpContext),
                    permitLimit:
                        12,
                    window:
                        TimeSpan.FromMinutes(
                            10)));

        options.AddPolicy(
            AuthEndpointMappings.AuthenticationRateLimitPolicy,
            httpContext =>
                CreateFixedWindowPartition(
                    GetRateLimitPartitionKey(
                        httpContext,
                        preferAuthenticatedUser:
                            false),
                    permitLimit:
                        20,
                    window:
                        TimeSpan.FromMinutes(
                            1)));

        options.AddPolicy(
            BffEndpointMappings.FinancialRefreshRateLimitPolicy,
            httpContext =>
                CreateFixedWindowPartition(
                    GetRateLimitPartitionKey(
                        httpContext),
                    permitLimit:
                        6,
                    window:
                        TimeSpan.FromMinutes(
                            10)));

        options.AddPolicy(
            BffEndpointMappings.FinancialProviderRateLimitPolicy,
            httpContext =>
                CreateFixedWindowPartition(
                    GetRateLimitPartitionKey(
                        httpContext),
                    permitLimit:
                        20,
                    window:
                        TimeSpan.FromMinutes(
                            10)));
    });

var hostingConfiguration =
    builder.ConfigureFullWorthWebHosting();

builder.Services.AddHttpClient(
    "FullWorthApi",
    client =>
    {
        client.BaseAddress =
            hostingConfiguration.ApiBaseUri;

        if (!string.IsNullOrWhiteSpace(
                hostingConfiguration.ApiHostHeader))
        {
            client.DefaultRequestHeaders.Host =
                hostingConfiguration.ApiHostHeader;
        }

        client.Timeout =
            TimeSpan.FromSeconds(30);
    });

builder.Services.AddScoped<
    WebAuthenticationService>();

builder.Services.AddScoped<
    FullWorthBffProxyService>();

builder.Services.AddScoped<
    AdminBffWriteProxyService>();

var app =
    builder.Build();

if (hostingConfiguration.UseForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseFullWorthRequestCorrelation();
app.UseRequestLocalization();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseFullWorthWebSecurityHeaders();

app.UseWhen(
    context =>
        !context.Request.Path.StartsWithSegments(
            "/health"),
    branch =>
        branch.UseHttpsRedirection());

/*
 * Auth and BFF mutations intentionally consume JSON, HTML form, or multipart
 * bodies only. Reject ambiguous/unsupported media types before authentication,
 * antiforgery body processing, proxy work, or endpoint handlers.
 */
app.Use(
    async (
        context,
        next) =>
    {
        var request = context.Request;
        var isUnsafeMethod =
            HttpMethods.IsPost(request.Method) ||
            HttpMethods.IsPut(request.Method) ||
            HttpMethods.IsPatch(request.Method) ||
            HttpMethods.IsDelete(request.Method);

        var canHaveBody =
            context.Features
                .Get<
                    Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>()?
                .CanHaveBody ==
            true;

        var isProtectedSurface =
            request.Path.StartsWithSegments("/auth") ||
            request.Path.StartsWithSegments("/bff");

        if (isProtectedSurface &&
            isUnsafeMethod &&
            canHaveBody &&
            !IsAllowedRequestContentType(request.ContentType))
        {
            context.Response.StatusCode =
                StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        await next();
    });

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseFullWorthAntiforgeryBoundary();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapFullWorthHealthEndpoints();
app.MapFullWorthAuthEndpoints();
app.MapFullWorthExternalAuthenticationEndpoints();
app.MapFullWorthExternalIdentityManagementEndpoints();
app.MapFullWorthBffEndpoints();
app.MapFullWorthAdminBffEndpoints();
app.MapFullWorthAccountPreferenceBffEndpoints();
app.MapFullWorthAccountSecurityBffEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ResolveUiCulture(
    string? acceptLanguageHeader)
{
    if (string.IsNullOrWhiteSpace(
            acceptLanguageHeader))
    {
        return "en-US";
    }

    var preferences =
        acceptLanguageHeader
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(
                (entry, index) =>
                    ParseLanguagePreference(
                        entry,
                        index))
            .Where(
                preference =>
                    preference.Quality > 0m)
            .OrderByDescending(
                preference =>
                    preference.Quality)
            .ThenBy(
                preference =>
                    preference.Index);

    foreach (var preference in
             preferences)
    {
        if (preference.Language.Equals(
                "es",
                StringComparison.OrdinalIgnoreCase) ||
            preference.Language.StartsWith(
                "es-",
                StringComparison.OrdinalIgnoreCase))
        {
            return "es";
        }

        if (preference.Language.Equals(
                "en",
                StringComparison.OrdinalIgnoreCase) ||
            preference.Language.StartsWith(
                "en-",
                StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }
    }

    return "en-US";
}

static (
    string Language,
    decimal Quality,
    int Index)
    ParseLanguagePreference(
        string value,
        int index)
{
    var segments =
        value.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

    var language =
        segments.Length == 0
            ? string.Empty
            : segments[0];

    var quality =
        1m;

    foreach (var segment in
             segments.Skip(1))
    {
        if (!segment.StartsWith(
                "q=",
                StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (decimal.TryParse(
                segment[2..],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsedQuality))
        {
            quality =
                Math.Clamp(
                    parsedQuality,
                    0m,
                    1m);
        }
    }

    return (
        language,
        quality,
        index);
}

static bool IsAllowedRequestContentType(
    string? contentType)
{
    if (string.IsNullOrWhiteSpace(contentType))
    {
        return false;
    }

    var parameterSeparator =
        contentType.IndexOf(';');

    var mediaType =
        (parameterSeparator >= 0
            ? contentType[..parameterSeparator]
            : contentType)
        .Trim();

    return mediaType.Equals(
               "application/json",
               StringComparison.OrdinalIgnoreCase) ||
           (mediaType.StartsWith(
                "application/",
                StringComparison.OrdinalIgnoreCase) &&
            mediaType.EndsWith(
                "+json",
                StringComparison.OrdinalIgnoreCase)) ||
           mediaType.Equals(
               "application/x-www-form-urlencoded",
               StringComparison.OrdinalIgnoreCase) ||
           mediaType.Equals(
               "multipart/form-data",
               StringComparison.OrdinalIgnoreCase);
}

static string GetRateLimitPartitionKey(
    HttpContext httpContext,
    bool preferAuthenticatedUser = true)
{
    ArgumentNullException.ThrowIfNull(
        httpContext);

    if (preferAuthenticatedUser &&
        httpContext.User.Identity?.IsAuthenticated ==
            true)
    {
        var userId =
            httpContext.User.FindFirst(
                    ClaimTypes.NameIdentifier)?
                .Value;

        if (!string.IsNullOrWhiteSpace(
                userId))
        {
            return
                $"user:{userId}";
        }
    }

    var remoteIpAddress =
        httpContext.Connection
            .RemoteIpAddress?
            .ToString();

    return string.IsNullOrWhiteSpace(
            remoteIpAddress)
        ? "ip:unknown"
        : $"ip:{remoteIpAddress}";
}

static RateLimitPartition<string>
    CreateFixedWindowPartition(
        string partitionKey,
        int permitLimit,
        TimeSpan window)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(
        partitionKey);

    if (permitLimit <=
        0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(permitLimit));
    }

    if (window <=
        TimeSpan.Zero)
    {
        throw new ArgumentOutOfRangeException(
            nameof(window));
    }

    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey:
            partitionKey,

        factory:
            _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit =
                        permitLimit,

                    Window =
                        window,

                    QueueLimit =
                        0,

                    AutoReplenishment =
                        true
                });
}
