using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace FullWorth.API.Services.Identity;

// Password proof only: never stores a password or grants access to account data.
// Single API process; restart, expiry and cross-replica requests fail closed.
public sealed class PendingPasswordLoginStore(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);
    private const int Capacity = 2048;
    private readonly object gate = new();
    private readonly Dictionary<string, Proof> pending = new(StringComparer.Ordinal);

    public sealed record Proof(Guid UserId, string SecurityStamp, DateTimeOffset ExpiresAt, int AttemptsLeft);
    public sealed record Issued(string LoginId, DateTimeOffset ExpiresAt);

    public Issued? Create(Guid userId, string stamp) =>
        Issue(new(userId, stamp, clock.GetUtcNow() + Lifetime, 3));

    public Issued? Retry(Proof proof) =>
        proof.AttemptsLeft > 1 ? Issue(proof with { AttemptsLeft = proof.AttemptsLeft - 1 }) : null;

    private Issued? Issue(Proof proof)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var id in pending.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray())
                pending.Remove(id);
            if (proof.ExpiresAt <= now || pending.Count >= Capacity) return null;
            var idValue = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            pending.Add(idValue, proof);
            return new(idValue, proof.ExpiresAt);
        }
    }

    public Proof? Read(string? id)
    {
        if (id?.Length != 43) return null;
        lock (gate)
            return pending.TryGetValue(id, out var proof) && proof.ExpiresAt > clock.GetUtcNow()
                ? proof : null;
    }

    public Proof? Consume(string? id)
    {
        if (id?.Length != 43) return null;
        lock (gate)
            return pending.Remove(id, out var proof) && proof.ExpiresAt > clock.GetUtcNow()
                ? proof : null;
    }
}
