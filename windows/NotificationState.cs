using System;
using System.Collections.Generic;
using System.Linq;

namespace AtomNotifier;

// Track notification identities so removing one toast cannot clear another.
public sealed class NotificationState {
    public static readonly string[] Channels = { "CHAT", "CODEX", "DOTS" };
    Dictionary<string, string> active = new();
    readonly Dictionary<string, string> queued = new();
    readonly HashSet<string> acknowledged = new();
    bool initialized;

    public string[] Observe(Dictionary<string, string> snapshot) {
        var added = new List<string>();
        foreach (string key in queued.Keys.ToArray())
            if (!snapshot.TryGetValue(key, out string? channel) || queued[key] != channel)
                queued.Remove(key);
        acknowledged.RemoveWhere(key => !snapshot.ContainsKey(key));
        if (initialized)
            foreach (var item in snapshot)
                if (!active.TryGetValue(item.Key, out string? previous) || previous != item.Value) {
                    queued[item.Key] = item.Value;
                    added.Add(item.Key);
                }
        active = new Dictionary<string, string>(snapshot);
        initialized = true;
        return added.ToArray();
    }

    public string[] QueuedChannels => queued.Values.Distinct().ToArray();
    public int ClearMask => Channels.Select((channel, index) =>
        active.Any(item => item.Value == channel && !acknowledged.Contains(item.Key)) ? 0 : 1 << index).Sum();

    public void MarkSent(string channel) {
        foreach (string key in queued.Where(item => item.Value == channel).Select(item => item.Key).ToArray())
            queued.Remove(key);
    }

    public bool DismissChannel(string channel) {
        bool changed = false;
        foreach (var item in active.Where(item => item.Value == channel))
            changed |= acknowledged.Add(item.Key);
        MarkSent(channel);
        return changed;
    }
}
