using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Subscriptions;

public sealed class StripeBillingService(
    HttpClient httpClient,
    StripeBillingOptions options,
    FullWorthDbContext dbContext,
    IIdentityUserExistenceGateway userExistenceGateway,
    TimeProvider timeProvider)
{
    private const string StripeApiBaseUrl = "https://api.stripe.com/v1/";
    private const string UserMetadataKey = "billwatch_user_id";
    private static readonly TimeSpan WebhookTolerance = TimeSpan.FromMinutes(5);

    public bool IsConfigured => options.IsConfigured;

    public async Task<IReadOnlyList<StripeBillingPlan>> GetPlansAsync(
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var monthly = await GetPriceAsync(
            "monthly",
            options.MonthlyPriceId,
            cancellationToken);

        var yearly = await GetPriceAsync(
            "yearly",
            options.YearlyPriceId,
            cancellationToken);

        return [monthly, yearly];
    }

    public async Task<string> CreateCheckoutUrlAsync(
        Guid userId,
        string email,
        string billingInterval,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var normalizedInterval = NormalizeBillingInterval(billingInterval);
        var priceId = normalizedInterval == "yearly"
            ? options.YearlyPriceId
            : options.MonthlyPriceId;

        // The direct API path must enforce the same price validation as the
        // plan display. A stale or miswired Stripe Price must not reach Checkout.
        await GetPriceAsync(
            normalizedInterval,
            priceId,
            cancellationToken);

        var customerId = await GetOrCreateCustomerIdAsync(
            userId,
            email,
            cancellationToken);

        var current = await GetCurrentSubscriptionAsync(
            userId,
            email,
            cancellationToken);

        if (current?.IsEntitled(timeProvider.GetUtcNow()) == true)
        {
            throw new StripeBillingException(
                "An active paid subscription already exists for this account.");
        }

        var successUrl =
            $"{options.PublicWebBaseUrl}/app/subscription?checkout=success&session_id={{CHECKOUT_SESSION_ID}}";

        var cancelUrl =
            $"{options.PublicWebBaseUrl}/app/subscription?checkout=cancelled";

        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["customer"] = customerId,
            ["client_reference_id"] = userId.ToString("D"),
            ["line_items[0][price]"] = priceId,
            ["line_items[0][quantity]"] = "1",
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            [$"metadata[{UserMetadataKey}]"] = userId.ToString("D"),
            [$"subscription_data[metadata][{UserMetadataKey}]"] = userId.ToString("D")
        };

        using var document = await SendFormAsync(
            HttpMethod.Post,
            "checkout/sessions",
            form,
            cancellationToken);

        var url = GetString(document.RootElement, "url");

        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new StripeBillingException(
                "The billing provider did not return a valid checkout URL.");
        }

        return url;
    }

    public async Task<string> CreatePortalUrlAsync(
        Guid userId,
        string? email,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var customerId = await FindCustomerIdAsync(
            userId,
            email,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new StripeBillingException(
                "No paid billing profile exists for this account.");
        }

        var form = new Dictionary<string, string>
        {
            ["customer"] = customerId,
            ["return_url"] = $"{options.PublicWebBaseUrl}/app/subscription"
        };

        using var document = await SendFormAsync(
            HttpMethod.Post,
            "billing_portal/sessions",
            form,
            cancellationToken);

        var url = GetString(document.RootElement, "url");

        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new StripeBillingException(
                "The billing provider did not return a valid management URL.");
        }

        return url;
    }

    public async Task<StripeSubscriptionState?> GetCurrentSubscriptionAsync(
        Guid userId,
        string? email,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var customerId = await FindCustomerIdAsync(
            userId,
            email,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(customerId))
        {
            return null;
        }

        using var document = await SendAsync(
            HttpMethod.Get,
            $"subscriptions?customer={Uri.EscapeDataString(customerId)}&status=all&limit=10",
            content: null,
            cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        StripeSubscriptionState? fallback = null;
        var now = timeProvider.GetUtcNow();

        foreach (var item in data.EnumerateArray())
        {
            var state = ParseSubscription(item);

            if (!state.IsFullWorthPlan)
            {
                continue;
            }

            fallback ??= state;

            if (state.IsEntitled(now))
            {
                return state;
            }
        }

        return fallback;
    }

    public async Task SyncCurrentSubscriptionAsync(
        Guid userId,
        string? email,
        CancellationToken cancellationToken)
    {
        var state = await GetCurrentSubscriptionAsync(
            userId,
            email,
            cancellationToken);

        await SyncPaidEntitlementAsync(
            userId,
            state,
            cancellationToken);
    }

    public async Task HandleWebhookAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (!VerifyWebhookSignature(payload, signatureHeader))
        {
            throw new StripeWebhookSignatureException();
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var eventType = GetString(root, "type") ?? string.Empty;

        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("object", out var objectElement))
        {
            return;
        }

        if (string.Equals(
                eventType,
                "checkout.session.completed",
                StringComparison.Ordinal) ||
            string.Equals(
                eventType,
                "checkout.session.async_payment_succeeded",
                StringComparison.Ordinal))
        {
            var userId = GetUserId(objectElement);
            var subscriptionId = GetString(objectElement, "subscription");

            if (userId is null || string.IsNullOrWhiteSpace(subscriptionId))
            {
                return;
            }

            var state = await GetSubscriptionByIdAsync(
                subscriptionId,
                cancellationToken);

            if (!state.IsFullWorthPlan)
            {
                return;
            }

            await SyncPaidEntitlementAsync(
                userId.Value,
                state,
                cancellationToken);

            return;
        }

        if (eventType.StartsWith(
                "customer.subscription.",
                StringComparison.Ordinal))
        {
            var userId = GetUserId(objectElement);

            if (userId is null)
            {
                return;
            }

            // Reconcile provider state instead of trusting delivery order.
            // A delayed active event must not restore a canceled entitlement.
            var state = await GetCurrentSubscriptionAsync(
                userId.Value,
                email: null,
                cancellationToken: cancellationToken);

            await SyncPaidEntitlementAsync(
                userId.Value,
                state,
                cancellationToken);
        }
    }

    private async Task SyncPaidEntitlementAsync(
        Guid userId,
        StripeSubscriptionState? state,
        CancellationToken cancellationToken)
    {
        var userExists =
            await userExistenceGateway.ExistsAsync(
                userId,
                cancellationToken);

        if (!userExists)
        {
            return;
        }

        var paidEntitlements = await dbContext.SubscriptionEntitlements
            .Where(entitlement =>
                entitlement.UserId == userId &&
                entitlement.Source == SubscriptionEntitlementSource.Paid)
            .OrderByDescending(entitlement => entitlement.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var isEntitled = state?.IsEntitled(now) == true;

        if (!isEntitled || state?.CurrentPeriodEndUtc is null)
        {
            foreach (var entitlement in paidEntitlements.Where(candidate => !candidate.IsRevoked))
            {
                entitlement.IsRevoked = true;
                entitlement.RevokedAtUtc = now;
                entitlement.UpdatedAtUtc = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var startsAt = state.CurrentPeriodStartUtc ?? now;
        var endsAt = state.CurrentPeriodEndUtc.Value;

        if (endsAt <= startsAt)
        {
            throw new StripeBillingException(
                "The billing provider returned an invalid subscription period.");
        }

        var effective = paidEntitlements.FirstOrDefault();

        if (effective is null)
        {
            effective = new SubscriptionEntitlementEntity
            {
                UserId = userId,
                Tier = FullWorthSubscriptionTier.Standard,
                Source = SubscriptionEntitlementSource.Paid,
                StartsAtUtc = startsAt,
                EndsAtUtc = endsAt,
                IsRevoked = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            dbContext.SubscriptionEntitlements.Add(effective);
        }
        else
        {
            effective.Tier = FullWorthSubscriptionTier.Standard;
            effective.StartsAtUtc = startsAt;
            effective.EndsAtUtc = endsAt;
            effective.IsRevoked = false;
            effective.RevokedAtUtc = null;
            effective.UpdatedAtUtc = now;
        }

        foreach (var duplicate in paidEntitlements.Skip(1))
        {
            if (!duplicate.IsRevoked)
            {
                duplicate.IsRevoked = true;
                duplicate.RevokedAtUtc = now;
                duplicate.UpdatedAtUtc = now;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<StripeBillingPlan> GetPriceAsync(
        string billingInterval,
        string priceId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(
            HttpMethod.Get,
            $"prices/{Uri.EscapeDataString(priceId)}",
            content: null,
            cancellationToken);

        var root = document.RootElement;
        var amount = GetInt64(root, "unit_amount");
        var currency = GetString(root, "currency");
        var expectedInterval = billingInterval == "yearly" ? "year" : "month";

        if (!string.Equals(GetString(root, "id"), priceId, StringComparison.Ordinal) ||
            GetBoolean(root, "active") != true ||
            amount is null or <= 0 ||
            string.IsNullOrWhiteSpace(currency) ||
            !root.TryGetProperty("recurring", out var recurring) ||
            recurring.ValueKind != JsonValueKind.Object ||
            !string.Equals(
                GetString(recurring, "interval"),
                expectedInterval,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new StripeBillingException(
                "The configured billing plan is unavailable or does not match its billing interval.");
        }

        return new StripeBillingPlan(
            billingInterval,
            amount.Value,
            currency.ToUpperInvariant());
    }

    private async Task<string> GetOrCreateCustomerIdAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerIdAsync(
            userId,
            email,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var form = new Dictionary<string, string>
        {
            ["email"] = email,
            [$"metadata[{UserMetadataKey}]"] = userId.ToString("D")
        };

        using var document = await SendFormAsync(
            HttpMethod.Post,
            "customers",
            form,
            cancellationToken);

        var customerId = GetString(document.RootElement, "id");

        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new StripeBillingException(
                "The billing provider did not create a customer profile.");
        }

        return customerId;
    }

    private async Task<string?> FindCustomerIdAsync(
        Guid userId,
        string? email,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(
            $"metadata['{UserMetadataKey}']:'{userId:D}'");

        using (var searchDocument = await SendAsync(
                   HttpMethod.Get,
                   $"customers/search?query={query}&limit=1",
                   content: null,
                   cancellationToken))
        {
            if (TryGetFirstMatchingCustomerId(
                    searchDocument.RootElement,
                    userId,
                    out var searchCustomerId))
            {
                return searchCustomerId;
            }
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        using var listDocument = await SendAsync(
            HttpMethod.Get,
            $"customers?email={Uri.EscapeDataString(email.Trim())}&limit=100",
            content: null,
            cancellationToken);

        return TryGetFirstMatchingCustomerId(
            listDocument.RootElement,
            userId,
            out var listCustomerId)
                ? listCustomerId
                : null;
    }

    private static bool TryGetFirstMatchingCustomerId(
        JsonElement root,
        Guid userId,
        out string? customerId)
    {
        customerId = null;

        if (!root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var customer in data.EnumerateArray())
        {
            if (!HasUserMetadata(customer, userId))
            {
                continue;
            }

            var candidateId = GetString(customer, "id");

            if (string.IsNullOrWhiteSpace(candidateId))
            {
                continue;
            }

            customerId = candidateId;
            return true;
        }

        return false;
    }

    private static bool HasUserMetadata(
        JsonElement element,
        Guid userId)
    {
        return element.TryGetProperty("metadata", out var metadata) &&
               metadata.ValueKind == JsonValueKind.Object &&
               metadata.TryGetProperty(UserMetadataKey, out var userMetadata) &&
               Guid.TryParse(userMetadata.GetString(), out var metadataUserId) &&
               metadataUserId == userId;
    }

    private async Task<StripeSubscriptionState> GetSubscriptionByIdAsync(
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(
            HttpMethod.Get,
            $"subscriptions/{Uri.EscapeDataString(subscriptionId)}",
            content: null,
            cancellationToken);

        return ParseSubscription(document.RootElement);
    }

    private StripeSubscriptionState ParseSubscription(JsonElement subscription)
    {
        var status = GetString(subscription, "status") ?? "unknown";
        var currentPeriodStart = FromUnixSeconds(GetInt64(subscription, "current_period_start"));
        var currentPeriodEnd = FromUnixSeconds(GetInt64(subscription, "current_period_end"));
        var cancelAtPeriodEnd = GetBoolean(subscription, "cancel_at_period_end") ?? false;
        var billingInterval = "monthly";
        var isFullWorthPlan = false;

        if (subscription.TryGetProperty("items", out var items) &&
            items.TryGetProperty("data", out var itemData) &&
            itemData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemData.EnumerateArray())
            {
                currentPeriodStart ??=
                    FromUnixSeconds(GetInt64(item, "current_period_start"));

                currentPeriodEnd ??=
                    FromUnixSeconds(GetInt64(item, "current_period_end"));

                if (!item.TryGetProperty("price", out var price))
                {
                    continue;
                }

                var priceId = GetString(price, "id");

                if (string.Equals(priceId, options.YearlyPriceId, StringComparison.Ordinal))
                {
                    isFullWorthPlan = true;
                    billingInterval = "yearly";
                    break;
                }

                if (string.Equals(priceId, options.MonthlyPriceId, StringComparison.Ordinal))
                {
                    isFullWorthPlan = true;
                    billingInterval = "monthly";
                    break;
                }
            }
        }

        return new StripeSubscriptionState(
            status,
            billingInterval,
            currentPeriodStart,
            currentPeriodEnd,
            cancelAtPeriodEnd,
            isFullWorthPlan);
    }

    private Guid? GetUserId(JsonElement element)
    {
        if (element.TryGetProperty("metadata", out var metadata) &&
            metadata.ValueKind == JsonValueKind.Object &&
            metadata.TryGetProperty(UserMetadataKey, out var userMetadata) &&
            Guid.TryParse(userMetadata.GetString(), out var metadataUserId))
        {
            return metadataUserId;
        }

        if (element.TryGetProperty("client_reference_id", out var clientReference) &&
            Guid.TryParse(clientReference.GetString(), out var referenceUserId))
        {
            return referenceUserId;
        }

        return null;
    }

    private bool VerifyWebhookSignature(
        string payload,
        string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        long? timestamp = null;
        var signatures = new List<string>();

        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);

            if (pieces.Length != 2)
            {
                continue;
            }

            if (string.Equals(pieces[0], "t", StringComparison.Ordinal) &&
                long.TryParse(
                    pieces[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }
            else if (string.Equals(pieces[0], "v1", StringComparison.Ordinal))
            {
                signatures.Add(pieces[1]);
            }
        }

        if (timestamp is null || signatures.Count == 0)
        {
            return false;
        }

        DateTimeOffset eventTime;

        try
        {
            eventTime = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        var difference = (timeProvider.GetUtcNow() - eventTime).Duration();

        if (difference > WebhookTolerance)
        {
            return false;
        }

        var signedPayload = $"{timestamp.Value}.{payload}";
        var secretBytes = Encoding.UTF8.GetBytes(options.WebhookSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(signedPayload);
        var expected = HMACSHA256.HashData(secretBytes, payloadBytes);

        foreach (var candidate in signatures)
        {
            byte[] actual;

            try
            {
                actual = Convert.FromHexString(candidate);
            }
            catch (FormatException)
            {
                continue;
            }

            if (actual.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<JsonDocument> SendFormAsync(
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);

        return await SendAsync(
            method,
            relativePath,
            content,
            cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(
            method,
            StripeApiBaseUrl + relativePath);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", options.SecretKey);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        request.Content = content;

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new StripeBillingException(
                "The billing provider is temporarily unavailable.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new StripeBillingException(
                "The billing provider did not respond in time.",
                exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new StripeBillingException(
                    "The billing provider could not complete that request.");
            }

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException exception)
            {
                throw new StripeBillingException(
                    "The billing provider returned an invalid response.",
                    exception);
            }
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new StripeBillingException(
                "Paid billing is not configured for this FullWorth environment.");
        }
    }

    private static string NormalizeBillingInterval(string? value) =>
        string.Equals(value, "yearly", StringComparison.OrdinalIgnoreCase)
            ? "yearly"
            : string.Equals(value, "monthly", StringComparison.OrdinalIgnoreCase)
                ? "monthly"
                : throw new StripeBillingException("Choose a monthly or yearly billing plan.");

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static long? GetInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.TryGetInt64(out var value)
            ? value
            : null;

    private static bool? GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            ? property.GetBoolean()
            : null;

    private static DateTimeOffset? FromUnixSeconds(long? value) =>
        value is null
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(value.Value);
}

public sealed record StripeBillingPlan(
    string BillingInterval,
    long UnitAmount,
    string Currency);

public sealed record StripeSubscriptionState(
    string Status,
    string BillingInterval,
    DateTimeOffset? CurrentPeriodStartUtc,
    DateTimeOffset? CurrentPeriodEndUtc,
    bool CancelAtPeriodEnd,
    bool IsFullWorthPlan)
{
    public bool IsEntitled(DateTimeOffset nowUtc) =>
        IsFullWorthPlan &&
        (string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Status, "trialing", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Status, "past_due", StringComparison.OrdinalIgnoreCase)) &&
        CurrentPeriodEndUtc is not null &&
        CurrentPeriodEndUtc > nowUtc;
}

public sealed class StripeBillingException : Exception
{
    public StripeBillingException(string message)
        : base(message)
    {
    }

    public StripeBillingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class StripeWebhookSignatureException : Exception
{
    public StripeWebhookSignatureException()
        : base("The billing webhook signature was invalid.")
    {
    }
}
