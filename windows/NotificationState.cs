using System;
using System.Collections.Generic;
using System.Linq;

namespace AtomNotifier;

public readonly record struct ClearRequest(int Mask, long Revision);

// Windows toasts trigger delivery; badge absence or explicit acknowledgement clears retention.
public sealed class NotificationState {
    public static readonly string[] Channels = { "CHAT", "CODEX", "DOTS" };
    Dictionary<string, string> previous = new();
    readonly Dictionary<string, string> queued = new();
    bool initialized;
    long clearRevision;
    public int ClearMask { get; private set; }

    public string[] Observe(Dictionary<string, string> snapshot) {
        var added = new List<string>();
        if (initialized)
            foreach (var item in snapshot)
                if (!previous.TryGetValue(item.Key, out string? channel) || channel != item.Value) {
                    queued[item.Key] = item.Value;
                    added.Add(item.Key);
                }
        // Removing a toast does not mean the badge/read state changed.
        previous = new Dictionary<string, string>(snapshot);
        initialized = true;
        return added.ToArray();
    }

    public string[] QueuedChannels => queued.Values.Distinct().ToArray();
    public ClearRequest CaptureClear() => new(ClearMask, clearRevision);
    public void ConfirmClear(ClearRequest request) {
        // Preserve any newer acknowledgement received during a Bluetooth await.
        if (request.Revision == clearRevision) ClearMask &= ~request.Mask;
    }

    public void MarkSent(string channel) {
        foreach (string key in queued.Where(item => item.Value == channel).Select(item => item.Key).ToArray())
            queued.Remove(key);
    }

    public bool DismissChannel(string channel) {
        int bit = 1 << Array.IndexOf(Channels, channel);
        bool changed = (ClearMask & bit) == 0 || queued.Values.Contains(channel);
        MarkSent(channel);
        if (changed) { ClearMask |= bit; clearRevision++; }
        return changed;
    }
}
