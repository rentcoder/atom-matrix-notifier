using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AtomNotifier;

public enum BadgePresence { Unknown, Present, Absent }
public record BadgeReading(BadgePresence Presence, string AppId, string Detail);

public static class BadgeText {
    public static BadgePresence Parse(string helpText, string itemStatus) {
        string text = (helpText + " " + itemStatus).Normalize(NormalizationForm.FormKC).Trim();
        if (text.Length == 0) return BadgePresence.Absent;
        if (!Regex.IsMatch(text, "未読|お知らせ|通知|unread|notification", RegexOptions.IgnoreCase))
            return BadgePresence.Unknown;
        var numbers = Regex.Matches(text, "[0-9]+").Cast<Match>().ToArray();
        if (numbers.Length == 0) return BadgePresence.Present;
        return numbers.Any(n => n.Value.Any(c => c != '0')) ? BadgePresence.Present : BadgePresence.Absent;
    }
}

public sealed class BadgeClearPolicy {
    DateTimeOffset? absentSince;
    int absentSamples;
    public bool Observe(BadgePresence presence, DateTimeOffset now, DateTimeOffset? lastNotification) {
        if (presence != BadgePresence.Absent) { absentSince = null; absentSamples = 0; return false; }
        absentSince ??= now;
        absentSamples++;
        // Avoid brief metadata gaps and a toast arriving before its badge is painted.
        return absentSamples >= 3 && now - absentSince.Value >= TimeSpan.FromSeconds(2)
            && (lastNotification == null || now - lastNotification.Value >= TimeSpan.FromSeconds(3));
    }
}
