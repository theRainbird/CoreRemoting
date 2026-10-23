using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.RemoteDelegates;
using Xunit;

namespace CoreRemoting.Tests.RemoteDelegates;

public class SubscriptionTrackerTests
{
    [Fact]
    public void New_tracker_is_empty()
    {
        var tracker = new SubscriptionTracker();

        Assert.Equal(0, tracker.Count);
        Assert.Equal("0:0000000000000000-0000000000000000", tracker.Checksum);
    }

    [Fact]
    public void Add_single_increments_count()
    {
        var tracker = new SubscriptionTracker();

        tracker.Add(Guid.NewGuid());

        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void Add_multiple_increments_count_by_their_number()
    {
        var tracker = new SubscriptionTracker();

        tracker.Add([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Equal(3, tracker.Count);
    }

    [Fact]
    public void Remove_single_decrements_count()
    {
        var tracker = new SubscriptionTracker();
        var key = Guid.NewGuid();

        tracker.Add(key);
        tracker.Remove(key);

        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Remove_multiple_decrements_count_by_their_number()
    {
        var tracker = new SubscriptionTracker();

        tracker.Remove([Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Equal(-2, tracker.Count);
    }

    [Fact]
    public void Adding_same_key_twice_counts_twice()
    {
        // Multicast semantics: += on the same handler produces two subscriptions.
        var tracker = new SubscriptionTracker();
        var key = Guid.NewGuid();

        tracker.Add(key);
        tracker.Add(key);

        Assert.Equal(2, tracker.Count);
    }

    [Fact]
    public void Removing_same_key_twice_counts_twice()
    {
        var tracker = new SubscriptionTracker();
        var key = Guid.NewGuid();

        tracker.Add(key);
        tracker.Add(key);
        tracker.Remove(key);
        tracker.Remove(key);

        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Removing_unsubscribed_key_goes_negative()
    {
        // -= on a handler that was never subscribed is a legal no-op for .NET events,
        // but it must be reflected by the tracker so that both endpoints stay in sync.
        var tracker = new SubscriptionTracker();

        tracker.Remove(Guid.NewGuid());

        Assert.Equal(-1, tracker.Count);
    }

    [Fact]
    public void Add_then_remove_same_key_returns_to_initial_checksum()
    {
        var tracker = new SubscriptionTracker();
        var key = Guid.NewGuid();

        var before = tracker.Checksum;

        tracker.Add(key);
        tracker.Remove(key);

        Assert.Equal(before, tracker.Checksum);
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Checksum_changes_when_subscriptions_change()
    {
        var tracker = new SubscriptionTracker();

        var empty = tracker.Checksum;
        tracker.Add(Guid.NewGuid());
        var one = tracker.Checksum;

        Assert.NotEqual(empty, one);
    }

    [Fact]
    public void Checksum_is_deterministic_for_same_operations()
    {
        var key1 = Guid.NewGuid();
        var key2 = Guid.NewGuid();
        var key3 = Guid.NewGuid();

        var tracker1 = new SubscriptionTracker();
        tracker1.Add(key1);
        tracker1.Add(key2);
        tracker1.Add(key3);

        var tracker2 = new SubscriptionTracker();
        tracker2.Add(key1);
        tracker2.Add(key2);
        tracker2.Add(key3);

        Assert.Equal(tracker1.Checksum, tracker2.Checksum);
    }

    [Fact]
    public void Checksum_is_order_independent()
    {
        var key1 = Guid.NewGuid();
        var key2 = Guid.NewGuid();
        var key3 = Guid.NewGuid();

        var tracker1 = new SubscriptionTracker();
        tracker1.Add(key1);
        tracker1.Add(key2);
        tracker1.Add(key3);

        var tracker2 = new SubscriptionTracker();
        tracker2.Add(key3);
        tracker2.Add(key1);
        tracker2.Add(key2);

        Assert.Equal(tracker1.Checksum, tracker2.Checksum);
    }

    [Fact]
    public void Checksum_differs_for_single_vs_duplicate_subscription()
    {
        // This is the key property that distinguishes an operation-counting
        // tracker from a set-based one.
        var key = Guid.NewGuid();

        var tracker1 = new SubscriptionTracker();
        tracker1.Add(key);

        var tracker2 = new SubscriptionTracker();
        tracker2.Add(key);
        tracker2.Add(key);

        Assert.NotEqual(tracker1.Checksum, tracker2.Checksum);
    }

    [Fact]
    public void Checksum_format_is_count_colon_hex_pair()
    {
        var tracker = new SubscriptionTracker();
        tracker.Add(Guid.NewGuid());

        var checksum = tracker.Checksum;

        Assert.StartsWith("1:", checksum);

        var parts = checksum.Split(':');
        Assert.Equal(2, parts.Length);

        var halves = parts[1].Split('-');
        Assert.Equal(2, halves.Length);
        Assert.Equal(16, halves[0].Length);
        Assert.Equal(16, halves[1].Length);
    }

    // --- Golden checksum tests -------------------------------------------------
    //
    // These pin the checksum format and the arithmetic to fixed inputs.
    // All bytes of the chosen Guids are 0xFF or 0x00, so each half reads as
    // either -1 or 0 regardless of how Guid.ToByteArray and BitConverter
    // are implemented. Any change to the algorithm (XOR instead of Add,
    // swapped halves, different format) will fail these tests.

    [Fact]
    public void Checksum_is_stable_for_repeated_add_of_ones()
    {
        var allOnes = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var tracker = new SubscriptionTracker();

        Assert.Equal("0:0000000000000000-0000000000000000", tracker.Checksum);

        tracker.Add(allOnes);
        Assert.Equal("1:ffffffffffffffff-ffffffffffffffff", tracker.Checksum);

        tracker.Add(allOnes);
        Assert.Equal("2:fffffffffffffffe-fffffffffffffffe", tracker.Checksum);

        tracker.Add(allOnes);
        Assert.Equal("3:fffffffffffffffd-fffffffffffffffd", tracker.Checksum);

        tracker.Remove(allOnes);
        Assert.Equal("2:fffffffffffffffe-fffffffffffffffe", tracker.Checksum);

        tracker.Reset();
        Assert.Equal("0:0000000000000000-0000000000000000", tracker.Checksum);
    }

    [Fact]
    public void Checksum_tracks_sequence_of_operations()
    {
        // These Guids are chosen so that each half varies independently:
        //   a -> lo = -1, hi = -1
        //   b -> lo = -1, hi =  0
        //   c -> lo =  0, hi = -1
        var a = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var b = new Guid("ffffffff-ffff-ffff-0000-000000000000");
        var c = new Guid("00000000-0000-0000-ffff-ffffffffffff");

        var tracker = new SubscriptionTracker();

        Assert.Equal("0:0000000000000000-0000000000000000", tracker.Checksum);

        tracker.Add(a);
        Assert.Equal("1:ffffffffffffffff-ffffffffffffffff", tracker.Checksum);

        tracker.Add(b);
        Assert.Equal("2:fffffffffffffffe-ffffffffffffffff", tracker.Checksum);

        tracker.Add(c);
        Assert.Equal("3:fffffffffffffffe-fffffffffffffffe", tracker.Checksum);

        tracker.Remove(b);
        Assert.Equal("2:ffffffffffffffff-fffffffffffffffe", tracker.Checksum);

        tracker.Remove(a);
        Assert.Equal("1:0000000000000000-ffffffffffffffff", tracker.Checksum);

        tracker.Remove(c);
        Assert.Equal("0:0000000000000000-0000000000000000", tracker.Checksum);
    }

    // --- Null and empty inputs -----------------------------------------------

    [Fact]
    public void Add_null_enumeration_is_noop()
    {
        var tracker = new SubscriptionTracker();

        tracker.Add(null);

        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Remove_null_enumeration_is_noop()
    {
        var tracker = new SubscriptionTracker();

        tracker.Remove(null);

        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Add_empty_array_is_noop()
    {
        var tracker = new SubscriptionTracker();

        tracker.Add([]);

        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Remove_empty_array_is_noop()
    {
        var tracker = new SubscriptionTracker();

        tracker.Remove([]);

        Assert.Equal(0, tracker.Count);
    }

    // --- Reset ---------------------------------------------------------------

    [Fact]
    public void Reset_clears_state()
    {
        var tracker = new SubscriptionTracker();
        tracker.Add(Guid.NewGuid());
        tracker.Add(Guid.NewGuid());

        tracker.Reset();

        Assert.Equal(0, tracker.Count);
        Assert.Equal("0:0000000000000000-0000000000000000", tracker.Checksum);
    }

    [Fact]
    public void Reset_with_null_clears_state()
    {
        var tracker = new SubscriptionTracker();
        tracker.Add(Guid.NewGuid());

        tracker.Reset(null);

        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Reset_with_seed_replaces_state()
    {
        var tracker = new SubscriptionTracker();
        tracker.Add(Guid.NewGuid());
        tracker.Add(Guid.NewGuid());
        tracker.Add(Guid.NewGuid());

        var key1 = Guid.NewGuid();
        var key2 = Guid.NewGuid();
        tracker.Reset([key1, key2]);

        Assert.Equal(2, tracker.Count);

        var reference = new SubscriptionTracker();
        reference.Add(key1);
        reference.Add(key2);
        Assert.Equal(reference.Checksum, tracker.Checksum);
    }

    [Fact]
    public void Reset_with_duplicates_counts_each_occurrence()
    {
        // Unlike a set-based tracker, our operation-counting tracker must
        // preserve duplicate occurrences in Reset, because the server side
        // invokes a handler as many times as it was subscribed.
        var key = Guid.NewGuid();
        var tracker = new SubscriptionTracker();

        tracker.Reset([key, key, key]);

        Assert.Equal(3, tracker.Count);

        var reference = new SubscriptionTracker();
        reference.Add(key);
        reference.Add(key);
        reference.Add(key);

        Assert.Equal(reference.Checksum, tracker.Checksum);
    }

    // --- Concurrency ---------------------------------------------------------

    [Fact]
    public async Task Count_can_be_read_concurrently_with_mutations()
    {
        var tracker = new SubscriptionTracker();
        var stop = false;

        var reader = Task.Run(() =>
        {
            while (!stop)
            {
                // Just touch both properties; we don't assert exact values.
                _ = tracker.Count;
                _ = tracker.Checksum;
            }
        });

        for (var i = 0; i < 1000; i++)
            tracker.Add(Guid.NewGuid());

        stop = true;
        await reader;

        Assert.Equal(1000, tracker.Count);
    }

    [Fact]
    public async Task Concurrent_adds_result_in_expected_count()
    {
        var tracker = new SubscriptionTracker();

        await Task.WhenAll(
            Enumerable.Range(0, 1000).Select(_ => Task.Run(() =>
                tracker.Add(Guid.NewGuid()))));

        Assert.Equal(1000, tracker.Count);
    }

    [Fact]
    public async Task Concurrent_adds_and_removes_of_same_key_are_consistent()
    {
        var tracker = new SubscriptionTracker();
        var key = Guid.NewGuid();

        // 500 adds and 500 removes of the same key, in any interleaving.
        // The counter should be 0 and the checksum should match the initial state.
        var initial = tracker.Checksum;

        await Task.WhenAll(
            Enumerable.Range(0, 500).SelectMany(_ => new[]
            {
                Task.Run(() => tracker.Add(key)),
                Task.Run(() => tracker.Remove(key)),
            }));

        Assert.Equal(0, tracker.Count);
        Assert.Equal(initial, tracker.Checksum);
    }

    [Fact]
    public async Task Concurrent_mutations_keep_count_and_sum_consistent()
    {
        // The invariant we care about: after all mutations settle, the
        // checksum matches a fresh tracker that applied the same net ops.
        var key1 = Guid.NewGuid();
        var key2 = Guid.NewGuid();
        var key3 = Guid.NewGuid();

        var tracker = new SubscriptionTracker();

        await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ => Task.Run(() => tracker.Add(key1)))
                .Concat(Enumerable.Range(0, 100).Select(_ => Task.Run(() => tracker.Add(key2))))
                .Concat(Enumerable.Range(0, 50).Select(_ => Task.Run(() => tracker.Add(key3))))
                .Concat(Enumerable.Range(0, 30).Select(_ => Task.Run(() => tracker.Remove(key1)))));

        // 100 + 100 + 50 - 30 = 220
        Assert.Equal(220, tracker.Count);

        var reference = new SubscriptionTracker();
        for (var i = 0; i < 70; i++) reference.Add(key1);
        for (var i = 0; i < 100; i++) reference.Add(key2);
        for (var i = 0; i < 50; i++) reference.Add(key3);

        Assert.Equal(reference.Checksum, tracker.Checksum);
    }
}