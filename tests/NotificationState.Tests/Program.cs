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
Check(state.QueuedChannels.Length == 0 && state.ClearMask == 0, "Startup must neither announce old toasts nor clear retention");
state.Observe(Snapshot(("old-chat", "CHAT"), ("chat", "CHAT"), ("code", "CODEX"), ("dot", "DOTS")));
Check(state.QueuedChannels.ToHashSet().SetEquals(NotificationState.Channels), "Windows toasts queue three channels");
state.Observe(Snapshot());
Check(state.QueuedChannels.Length == 3 && state.ClearMask == 0, "Removing Windows toasts must not clear or cancel while badge remains");
state.DismissChannel("CHAT");
Check(state.ClearMask == 1 && state.QueuedChannels.ToHashSet().SetEquals(new[] { "CODEX", "DOTS" }), "Explicit Chat acknowledgement only clears Chat");
var request = state.CaptureClear();
state.DismissChannel("CODEX");
state.ConfirmClear(request);
Check(state.ClearMask == 3, "Acknowledgement during Bluetooth await must survive old reply");
state.ConfirmClear(state.CaptureClear());
Check(state.ClearMask == 0, "Confirmed clear request completes");
state.MarkSent("DOTS");
Check(state.QueuedChannels.Length == 0 && state.ClearMask == 0, "Sending must not rearm or clear retained flags");
state.Observe(Snapshot(("new-chat", "CHAT")));
foreach (string channel in NotificationState.Channels) state.DismissChannel(channel);
Check(state.ClearMask == 7 && state.QueuedChannels.Length == 0, "Badge disappearance clears all channels and unsent events");
state.ConfirmClear(state.CaptureClear());
state.Observe(Snapshot(("new-chat", "CHAT")));
Check(state.QueuedChannels.Length == 0, "Existing retained Windows toast must not resurrect acknowledged flag");
state.Observe(Snapshot(("new-chat", "CHAT"), ("later", "CODEX")));
Check(state.QueuedChannels.SequenceEqual(new[] { "CODEX" }), "Fresh Windows toast resumes notification delivery");

Check(BadgeText.Parse("未読のお知らせ 1 件", "") == BadgePresence.Present, "Actual Japanese badge metadata");
Check(BadgeText.Parse("", "") == BadgePresence.Absent, "Empty overlay metadata means absent on a valid visible button");
Check(BadgeText.Parse("0 unread notifications", "") == BadgePresence.Absent, "Explicit zero badge");
Check(BadgeText.Parse("１２ unread notifications", "") == BadgePresence.Present, "Full-width digits");
Check(BadgeText.Parse("Unread notifications", "") == BadgePresence.Present, "Symbolic unread indicator");
Check(BadgeText.Parse("Updating application", "") == BadgePresence.Unknown, "Unrecognized status must not clear");

var policy = new BadgeClearPolicy();
var t = DateTimeOffset.Parse("2026-10-10T00:00:00Z");
Check(!policy.Observe(BadgePresence.Absent, t, null), "One absent observation cannot clear");
Check(!policy.Observe(BadgePresence.Absent, t.AddSeconds(1), null), "Two observations cannot clear");
Check(policy.Observe(BadgePresence.Absent, t.AddSeconds(2), null), "Stable absence permits clear");
Check(!policy.Observe(BadgePresence.Present, t.AddSeconds(3), null), "Present badge never clears");
Check(!policy.Observe(BadgePresence.Absent, t.AddSeconds(4), null), "Presence resets absence confirmation");
Check(!policy.Observe(BadgePresence.Unknown, t.AddSeconds(5), null), "Read failure never clears");
Check(!policy.Observe(BadgePresence.Absent, t.AddSeconds(6), null), "Read failure resets absence confirmation");
policy.Observe(BadgePresence.Absent, t.AddSeconds(7), null);
Check(!policy.Observe(BadgePresence.Absent, t.AddSeconds(8), t.AddSeconds(7)), "Grace period protects a new toast awaiting badge paint");
Check(policy.Observe(BadgePresence.Absent, t.AddSeconds(10), t.AddSeconds(7)), "Persistent absence after grace permits clear");
Console.WriteLine($"PASS: {checks} badge and notification synchronization checks");
