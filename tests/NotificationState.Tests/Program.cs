using AtomNotifier;

int checks = 0;
void Check(bool condition, string name) {
    if (!condition) throw new Exception(name);
    checks++;
}
Dictionary<string, string> Snapshot(params (string id, string channel)[] entries) =>
    entries.ToDictionary(item => item.id, item => item.channel);

var state = new NotificationState();
state.Observe(Snapshot(("old-chat", "CHAT")));
Check(state.QueuedChannels.Length == 0, "Startup must not announce existing notifications");
Check(state.ClearMask == 6, "Existing Chat must protect retained Chat flag");
state.Observe(Snapshot(("old-chat", "CHAT"), ("new-chat", "CHAT"), ("code", "CODEX")));
Check(state.QueuedChannels.ToHashSet().SetEquals(new[] { "CHAT", "CODEX" }), "New notifications queue by channel");
state.MarkSent("CHAT"); state.MarkSent("CODEX");
state.Observe(Snapshot(("new-chat", "CHAT"), ("code", "CODEX")));
Check(state.ClearMask == 4, "Removing one Chat must preserve another unread Chat");
state.DismissChannel("CHAT");
Check(state.ClearMask == 5, "Foreground acknowledgement clears Chat while preserving Codex");
state.Observe(Snapshot(("new-chat", "CHAT"), ("code", "CODEX")));
Check(state.QueuedChannels.Length == 0 && state.ClearMask == 5, "Retained Windows toast must not resurrect acknowledged Chat");
state.Observe(Snapshot(("new-chat", "CHAT"), ("background-chat", "CHAT"), ("code", "CODEX")));
Check(state.QueuedChannels.SequenceEqual(new[] { "CHAT" }) && state.ClearMask == 4, "New background notification must rearm Chat");
state.Observe(Snapshot(("new-chat", "CHAT"), ("code", "CODEX")));
Check(state.QueuedChannels.Length == 0 && state.ClearMask == 5, "Removed sleeping-queue notification must never be sent");
state.Observe(Snapshot());
Check(state.ClearMask == 7, "Empty notification center must clear all retained flags");
state.Observe(Snapshot(("dot", "DOTS")));
state.DismissChannel("DOTS");
Check(state.ClearMask == 7 && state.QueuedChannels.Length == 0, "Manual acknowledgement removes unsent notification");
Console.WriteLine($"PASS: {checks} notification synchronization checks");
