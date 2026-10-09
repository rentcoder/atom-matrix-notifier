using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.Win32;

namespace AtomNotifier;

// Read Explorer metadata without images or operating the ChatGPT app UI.
public sealed class TaskbarBadgeReader {
    readonly HashSet<string> verifiedApps = new(StringComparer.OrdinalIgnoreCase);
    readonly string? calibrationPath;
    public TaskbarBadgeReader(string? calibrationPath = null) {
        this.calibrationPath = calibrationPath;
        try {
            if (calibrationPath != null && File.Exists(calibrationPath))
                foreach (string id in JsonSerializer.Deserialize<string[]>(File.ReadAllText(calibrationPath)) ?? Array.Empty<string>()) verifiedApps.Add(id);
        } catch { }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, uint length, out uint needed);

    public BadgeReading Read(string configuredAppId) {
        try {
            var desktop = OpenInputDesktop(0, false, 1);
            if (desktop == IntPtr.Zero) return Unknown("デスクトップを読み取れません");
            try {
                var desktopName = new StringBuilder(256);
                if (!GetUserObjectInformation(desktop, 2, desktopName, 512, out _) || desktopName.ToString() != "Default")
                    return Unknown("ロック画面または通常以外のデスクトップです");
            } finally { CloseDesktop(desktop); }
            using var advanced = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if (advanced?.GetValue("TaskbarBadges") is int enabled && enabled == 0)
                return Unknown("Windowsのタスクバーバッジ設定が無効です");
            var handle = FindWindow("Shell_TrayWnd", null);
            if (handle == IntPtr.Zero || !IsWindowVisible(handle)) return Unknown("タスクバーが見つかりません");
            var root = AutomationElement.FromHandle(handle);
            if (root.Current.IsOffscreen) return Unknown("タスクバーが非表示です");
            var buttons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            var matches = buttons.Cast<AutomationElement>().Where(element => {
                string id = AppId(element.Current.AutomationId);
                if (configuredAppId.Length > 0) return id.Equals(configuredAppId, StringComparison.OrdinalIgnoreCase);
                return id.StartsWith("OpenAI.", StringComparison.OrdinalIgnoreCase)
                    && (id.Contains("Codex", StringComparison.OrdinalIgnoreCase) || id.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            if (matches.Length != 1) return Unknown(matches.Length == 0 ? "対象のタスクバーボタンがありません" : "対象が複数あります。アプリIDを指定してください");
            var target = matches[0].Current;
            string targetId = AppId(target.AutomationId);
            // Explorer reports NotSupported for an empty overlay description too.
            // Trust that empty value only after this app has exposed a real badge.
            if (matches[0].GetCurrentPropertyValue(AutomationElement.HelpTextProperty, true) == AutomationElement.NotSupported
                && !verifiedApps.Contains(targetId))
                return new(BadgePresence.Unknown, targetId, "初回のバッジ読み取り待ちです（保持は消しません）");
            if (target.IsOffscreen || target.BoundingRectangle.IsEmpty) return new(BadgePresence.Unknown, targetId, "対象アイコンが非表示です");
            bool running = false;
            foreach (string name in new[] { "ChatGPT", "Codex" })
                foreach (var process in Process.GetProcessesByName(name))
                    using (process) { if (process.MainWindowHandle != IntPtr.Zero) running = true; }
            if (!running) return new(BadgePresence.Unknown, targetId, "対象アプリのウィンドウがありません");
            BadgePresence presence = BadgeText.Parse(target.HelpText, target.ItemStatus);
            if (presence == BadgePresence.Present && verifiedApps.Add(targetId) && calibrationPath != null) {
                try {
                    Directory.CreateDirectory(Path.GetDirectoryName(calibrationPath)!);
                    File.WriteAllText(calibrationPath, JsonSerializer.Serialize(verifiedApps));
                } catch { } // Still calibrated for this process; keep future launches conservative.
            }
            string detail = presence == BadgePresence.Present ? "バッジあり" : presence == BadgePresence.Absent ? "バッジなし" : "バッジの説明を判定できません";
            return new(presence, targetId, detail);
        } catch (Exception e) { return Unknown("読み取り失敗: " + e.GetType().Name); }
    }
    static string AppId(string id) => id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) ? id[6..].Trim() : id.Trim();
    static BadgeReading Unknown(string reason) => new(BadgePresence.Unknown, "", reason);
}
