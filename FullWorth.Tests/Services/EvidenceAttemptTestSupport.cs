using System.Collections;
using System.Text.Json;
using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

internal static class EvidenceAttemptTestSupport
{
    public sealed record EntryEvidence(EntityState State, string Current, string Original, string[] Modified,
        Dictionary<string, object?> References, Dictionary<string, (object? Instance, object[] Items)> Collections);
    public static Dictionary<object, EntryEvidence> Capture(FullWorthDbContext db) =>
        db.ChangeTracker.Entries().ToDictionary(entry => entry.Entity, entry => new EntryEvidence(
            entry.State,
            JsonSerializer.Serialize(entry.CurrentValues.Properties.OrderBy(p => p.Name)
                .ToDictionary(p => p.Name, p => entry.CurrentValues[p])),
            JsonSerializer.Serialize(entry.OriginalValues.Properties.OrderBy(p => p.Name)
                .ToDictionary(p => p.Name, p => entry.OriginalValues[p])),
            entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order().ToArray(),
            entry.Navigations.Where(n => !n.Metadata.IsCollection).ToDictionary(n => n.Metadata.Name, n => n.CurrentValue),
            entry.Navigations.Where(n => n.Metadata.IsCollection).ToDictionary(n => n.Metadata.Name,
                n => (n.CurrentValue, n.CurrentValue is IEnumerable values ? values.Cast<object>().ToArray() : []))));

    public static void AssertRestored(FullWorthDbContext db, Dictionary<object, EntryEvidence> before)
    {
        var after = Capture(db);
        Assert.Equal(before.Count, after.Count);
        foreach (var (entity, expected) in before)
        {
            Assert.True(after.TryGetValue(entity, out var actual));
            Assert.Equal(expected.State, actual!.State);
            Assert.Equal(expected.Current, actual.Current);
            Assert.Equal(expected.Original, actual.Original);
            Assert.Equal(expected.Modified, actual.Modified);
            foreach (var (name, reference) in expected.References)
                Assert.Same(reference, actual.References[name]);
            foreach (var (name, collection) in expected.Collections)
            {
                Assert.Same(collection.Instance, actual.Collections[name].Instance);
                Assert.True(collection.Items.ToHashSet(ReferenceEqualityComparer.Instance)
                    .SetEquals(actual.Collections[name].Items));
            }
        }
    }
}
