using System.Runtime.CompilerServices;
using Google.Protobuf;

namespace Utxorpc.Sdk.Internal;

/// <summary>The clock and reconnect limit captured when a client enables resuming.</summary>
internal sealed record ResumeSettings(TimeProvider Time, int? Attempts);

/// <summary>A block identity and its known height; zero height means unknown.</summary>
internal readonly record struct ChainBlock(ulong Slot, ByteString Hash, ulong Height)
{
    internal bool Is(ChainBlock other) => Slot == other.Slot && Hash.Equals(other.Hash);
}

/// <summary>The chain actions used to track and place a stream.</summary>
internal enum StreamAction { Apply, Undo, Reset, Idle }

/// <summary>Registers opt-in clients and computes their reconnect waits.</summary>
internal static class StreamResume
{
    private static readonly ConditionalWeakTable<object, ResumeSettings> Clients = new();

    internal static void Enable(object client, ResumeSettings settings) => Clients.Add(client, settings);

    internal static ResumeSettings? For(object client) => Clients.TryGetValue(client, out ResumeSettings? settings) ? settings : null;

    internal static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(1.6, Math.Min(attempt - 1, 11)), 120) * (0.8 + 0.4 * Random.Shared.NextDouble()));
}

/// <summary>Keeps a bounded chain history, reusing entries and their transaction lists.</summary>
internal sealed class Window<TMessage>
{
    /// <summary>One retained block and its outstanding applied transactions.</summary>
    internal sealed class Entry
    {
        internal ChainBlock Block;
        internal readonly List<TMessage> Applied = new();
        internal bool Leaving;

        internal void Clear()
        {
            Block = default;
            Applied.Clear();
            Leaving = false;
        }
    }

    private readonly Entry[] entries = new Entry[10];

    internal Window()
    {
        for (int i = 0; i < entries.Length; i++) entries[i] = new();
    }

    internal int Count { get; private set; }
    internal Entry? Newest => Count == 0 ? null : entries[Count - 1];

    internal Entry Add(ChainBlock block)
    {
        if (Count == entries.Length) Remove(entries[0]);
        Entry entry = entries[Count++];
        entry.Block = block;
        return entry;
    }

    internal Entry? Find(ChainBlock block)
    {
        for (int i = Count - 1; i >= 0; i--)
            if (entries[i].Block.Is(block)) return entries[i];
        return null;
    }

    internal Entry? FindBelow(ulong height)
    {
        if (height == 0) return null;
        for (int i = Count - 1; i >= 0; i--)
            if (!entries[i].Leaving && entries[i].Block.Height != 0 && entries[i].Block.Height == height - 1) return entries[i];
        return null;
    }

    internal void CutAfter(Entry entry)
    {
        int keep = Array.IndexOf(entries, entry, 0, Count) + 1;
        while (Count > keep) entries[--Count].Clear();
    }

    internal void Remove(Entry entry)
    {
        int index = Array.IndexOf(entries, entry, 0, Count);
        Array.Copy(entries, index + 1, entries, index, Count - index - 1);
        entries[--Count] = entry;
        entry.Clear();
    }

    internal void Clear()
    {
        while (Count > 0) entries[--Count].Clear();
    }

    internal IEnumerable<Entry> EntriesAfter(Entry entry)
    {
        for (int i = Count - 1; i >= 0 && entries[i] != entry; i--) yield return entries[i];
    }

    internal int Intersect(Span<ChainBlock> points, bool leaveOutIncompleteNewest)
    {
        int count = 0;
        for (int i = Count - 1; i >= 0; i--)
        {
            Entry entry = entries[i];
            if (!entry.Leaving && !(leaveOutIncompleteNewest && i == Count - 1 && entry.Applied.Count > 0))
                points[count++] = entry.Block;
        }
        return count;
    }
}
