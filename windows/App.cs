using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Panel = System.Windows.Controls.Panel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace AtomNotifier;
public record BluetoothChoice(string Id, string Name);

public class Settings {
    public string DeviceId { get; set; } = "";
    public string Apps { get; set; } = "ChatGPT|Codex";
    public string Codex { get; set; } = @"\bcodex\b|コーデックス";
    public string Dots { get; set; } = @"\bdots?\b|ドット";
}

public class App : Application {
    [STAThread] public static void Main(string[] args) {
        using var mutex = new Mutex(false, @"Local\AtomNotifier.Tray.Instance");
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AtomNotifier.Tray.Show");
        using var testRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AtomNotifier.Tray.Test");
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex) { if (args.Contains("--test")) testRequest.Set(); else showRequest.Set(); return; }
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        var registration = ThreadPool.RegisterWaitForSingleObject(showRequest,
            (_, _) => window.Dispatcher.BeginInvoke(new Action(window.ShowSettings)), null, Timeout.Infinite, false);
        var testRegistration = ThreadPool.RegisterWaitForSingleObject(testRequest,
            (_, _) => window.Dispatcher.BeginInvoke(new Action(window.QueueDisplayTest)), null, Timeout.Infinite, false);
        try { app.Run(window); }
        finally { registration.Unregister(null); testRegistration.Unregister(null); mutex.ReleaseMutex(); }
    }
}

public class MainWindow : Window {
    readonly ComboBox ports = new() { Width = 220, DisplayMemberPath = "Name" };
    readonly TextBox apps = new(), codex = new(), dots = new();
    readonly TextBlock state = new() { Text = "WindowsでATOM-NotifyをペアリングしてからBluetooth検索を押してください。", TextWrapping = TextWrapping.Wrap };
    readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 240 };
    readonly CheckBox details = new() { Content = "分類確認用にアプリ名・タイトルを表示する（この画面内のみ）" };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Queue<string> outbound = new();
    HashSet<string> previous = new();
    StreamSocket? socket;
    DataReader? reader;
    DataWriter? writer;
    RfcommDeviceService? service;
    bool connected;
    string preferredDeviceId = "";
    UserNotificationListener? listener;
    Regex? appRule, codexRule, dotsRule;
    bool busy, initialized;
    DateTimeOffset nextConnectAttempt = DateTimeOffset.MinValue;
    DateTimeOffset? lastSuccessfulExchange;
    int completedSleepCycles, messagesSent;
    string firmwarePowerStatus = "未取得";
    string lastTransportError = "";
    readonly Forms.NotifyIcon tray;
    readonly Forms.ContextMenuStrip trayMenu;
    readonly Forms.ToolStripMenuItem trayStatus;
    readonly System.Drawing.Icon trayImage;
    bool exiting, started;
    readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomNotifier", "settings.json");

    public MainWindow() {
        Title = "ATOM Matrix 通知ランプ — Bluetooth"; Width = 710; Height = 680;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowState = WindowState.Minimized;
        trayImage = LoadTrayIcon();
        trayMenu = new Forms.ContextMenuStrip();
        trayStatus = new Forms.ToolStripMenuItem("接続を確認しています…") { Enabled = false };
        trayMenu.Items.Add(trayStatus);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("設定を開く", null, (_, _) => ShowSettings());
        trayMenu.Items.Add("Bluetooth再接続", null, async (_, _) => { if (busy) return; await RefreshPorts(); await Connect(); });
        trayMenu.Items.Add("通知表示テスト", null, (_, _) => QueueDisplayTest());
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("終了", null, (_, _) => ExitApp());
        tray = new Forms.NotifyIcon { Icon = trayImage, Text = "ATOM 通知ブリッジ — 接続を確認中", ContextMenuStrip = trayMenu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel };
        panel.Children.Add(new TextBlock { Text = "Chat / Codex / Dots → ATOM Matrix", FontSize = 23, Margin = new Thickness(0, 0, 0, 16) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(ports);
        AddButton(row, "Bluetooth検索", async () => await RefreshPorts());
        AddButton(row, "接続・通知を許可", async () => await Connect());
        panel.Children.Add(row);
        AddField(panel, "対象アプリ名（正規表現）", apps);
        AddField(panel, "Codex識別語：アプリ名・タイトル・本文を照合", codex);
        AddField(panel, "Dots識別語：アプリ名・タイトル・本文を照合", dots);
        panel.Children.Add(new TextBlock { Text = "優先順：Dots → Codex → Chat。識別語なしの対象通知はChatに分類。\n同じアプリ名だけでは3種類を区別できません。通知に実際に出る語を設定してください。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var tests = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var channel in new[] { "CHAT", "CODEX", "DOTS" }) {
            string captured = channel;
            AddButton(tests, $"{channel} テスト", () => { outbound.Enqueue(captured); AddLog("テストをキューに追加: " + captured); });
        }
        panel.Children.Add(tests);
        AddButton(panel, "設定を保存（反映には再接続）", Save);
        AddButton(panel, "トレイに戻す", HideToTray);
        panel.Children.Add(details); panel.Children.Add(state); panel.Children.Add(log);
        panel.Children.Add(new TextBlock { Text = "省電力：20秒スリープ／新着を約4秒表示。通常20〜30秒程度で転送。\nスリープ中の短押し：再表示。表示中に再度短押し：確認済み。長押し1.5秒：全確認。\n通知本文はPC内の分類だけに使い、ATOMへは種類だけ送信します。\nWindows通知そのものは削除しません。PC稼働中・このアプリ起動中に動作します。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        Load();
        timer.Tick += async (_, _) => await Tick();
        Loaded += async (_, _) => {
            if (started) return;
            started = true;
            HideToTray();
            await RefreshPorts(); await Connect();
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && started) HideToTray(); };
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; HideToTray(); } };
        Application.Current.SessionEnding += (_, _) => exiting = true;
        Closed += (_, _) => {
            timer.Stop(); Disconnect();
            tray.Visible = false;
            WriteOperationalStatus();
            tray.Dispose(); trayMenu.Dispose(); trayImage.Dispose();
        };
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    static System.Drawing.Icon LoadTrayIcon() {
        try {
            using var bitmap = new System.Drawing.Bitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "SmallLogo.png"));
            IntPtr handle = bitmap.GetHicon();
            try { using var icon = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)icon.Clone(); }
            finally { DestroyIcon(handle); }
        } catch { return (System.Drawing.Icon)System.Drawing.SystemIcons.Information.Clone(); }
    }
    public void ShowSettings() {
        if (exiting) return;
        ShowInTaskbar = true;
        ShowActivated = true;
        WindowState = WindowState.Normal;
        Show(); Activate();
        WriteOperationalStatus();
    }
    void HideToTray() {
        ShowInTaskbar = false;
        Hide();
        WriteOperationalStatus();
    }
    void ExitApp() {
        if (exiting) return;
        exiting = true;
        Application.Current.Shutdown();
    }
    public void QueueDisplayTest() {
        foreach (string channel in new[] { "CHAT", "CODEX", "DOTS" })
            if (!outbound.Contains(channel)) outbound.Enqueue(channel);
        AddLog("3種類の表示テストを追加しました。ATOMが起きるまで保持します。");
        WriteOperationalStatus();
    }
    static void AddButton(Panel p, string label, Action action) {
        var b = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(8) };
        b.Click += (_, _) => action(); p.Children.Add(b);
    }
    static void AddField(Panel p, string label, TextBox box) {
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 3) }); p.Children.Add(box);
    }
    async Task RefreshPorts() {
        try {
            string selected = (ports.SelectedItem as BluetoothChoice)?.Id ?? preferredDeviceId;
            var devices = await DeviceInformation.FindAllAsync(RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort));
            var found = new List<BluetoothChoice>();
            foreach (var candidate in devices) {
                try {
                    using var candidateService = await RfcommDeviceService.FromIdAsync(candidate.Id);
                    string name = candidateService?.Device.Name ?? candidate.Name;
                    if (name.StartsWith("ATOM-Notify", StringComparison.OrdinalIgnoreCase)) found.Add(new(candidate.Id, name));
                } catch { }
            }
            var matches = found.ToArray();
            ports.ItemsSource = matches;
            ports.SelectedItem = matches.FirstOrDefault(d => d.Id == selected) ?? matches.FirstOrDefault();
            if (matches.Length == 0) SetState("ATOM-Notifyがありません。WindowsのBluetooth設定でペアリング後、再検索してください。");
        } catch (Exception e) { SetState("Bluetooth検索失敗: " + e.Message); }
    }
    void Load() {
        Settings s = new();
        try { if (File.Exists(configPath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(configPath)) ?? s; } catch { }
        apps.Text = s.Apps; codex.Text = s.Codex; dots.Text = s.Dots;
        preferredDeviceId = s.DeviceId;
    }
    void Save() {
        try {
            _ = MakeRegex(apps.Text); _ = MakeRegex(codex.Text); _ = MakeRegex(dots.Text);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(new Settings { DeviceId = (ports.SelectedItem as BluetoothChoice)?.Id ?? preferredDeviceId, Apps = apps.Text, Codex = codex.Text, Dots = dots.Text }, new JsonSerializerOptions { WriteIndented = true }));
            AddLog("設定を保存しました。反映には再接続してください。");
        } catch (Exception e) { state.Text = "保存失敗: " + e.Message; }
    }
    static Regex MakeRegex(string text) {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("識別語は空にできません。");
        return new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
    void AddLog(string text) {
        if (log.Text.Length > 16000) log.Text = log.Text[^8000..];
        log.AppendText($"{DateTime.Now:HH:mm:ss} {text}\n"); log.ScrollToEnd();
    }
    void SetState(string text) {
        state.Text = text;
        string caption = timer.IsEnabled ? "省電力監視中（ATOMの起床待ち）" : text;
        trayStatus.Text = caption;
        string tooltip = "ATOM 通知ブリッジ — " + caption;
        tray.Text = tooltip.Length <= 63 ? tooltip : tooltip[..60] + "…";
        WriteOperationalStatus();
    }
    void WriteOperationalStatus() {
        // Operational status only. Notification titles/body are never written.
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(configPath)!, "status.json"),
                JsonSerializer.Serialize(new { Time = DateTimeOffset.Now, Message = state.Text, Connected = connected, Monitoring = timer.IsEnabled, Transport = "Bluetooth Classic SPP", PowerMode = "DeepSleep20s", PendingChannels = outbound.Count, LastSuccessfulExchange = lastSuccessfulExchange, CompletedSleepCycles = completedSleepCycles, MessagesSent = messagesSent, FirmwarePowerStatus = firmwarePowerStatus, LastTransportError = lastTransportError, TrayVisible = tray.Visible, WindowVisible = IsVisible, InTaskbar = ShowInTaskbar, ProcessId = Environment.ProcessId }));
        } catch { }
    }
    void Disconnect() {
        connected = false;
        reader?.Dispose(); reader = null;
        writer?.Dispose(); writer = null;
        socket?.Dispose(); socket = null;
        service?.Dispose(); service = null;
    }
    async Task<string> ReadResponse(string line) {
        if (!connected || reader == null || writer == null) throw new IOException("Bluetooth未接続");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        writer.WriteString(line + "\n");
        await writer.StoreAsync().AsTask(timeout.Token);
        var response = new System.Text.StringBuilder();
        while (response.Length < 64) {
            if (await reader.LoadAsync(1).AsTask(timeout.Token) == 0) throw new IOException("Bluetooth切断");
            char c = (char)reader.ReadByte();
            if (c == '\n') return response.ToString().Trim();
            response.Append(c);
        }
        throw new IOException("Bluetooth応答が長すぎます");
    }
    async Task<bool> Exchange(string line, string expected) => await ReadResponse(line) == expected;
    async Task Connect() {
        if (busy) return;
        busy = true; timer.Stop();
        try {
            appRule = MakeRegex(apps.Text); codexRule = MakeRegex(codex.Text); dotsRule = MakeRegex(dots.Text);
            Disconnect();
            listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
                throw new Exception("通知アクセスが許可されていません。Windows設定の通知アクセスと、Install.ps1での登録を確認してください。");
            // Baseline only on first connection. On reconnect, new retained toasts are captured.
            if (!initialized) {
                previous = (await listener.GetNotificationsAsync(NotificationKinds.Toast)).Select(Key).ToHashSet();
                initialized = true;
            }
            Save(); nextConnectAttempt = DateTimeOffset.MinValue; timer.Start();
            SetState("省電力監視中。通知はATOMの起床時に転送します。");
            AddLog("省電力監視を開始しました。ATOMのスリープ中も通知を保持します。");
        } catch (Exception e) { Disconnect(); SetState("接続失敗: " + e.Message); }
        finally { busy = false; }
    }
    async Task DeliverAndSleep() {
        if (DateTimeOffset.UtcNow < nextConnectAttempt) return;
        try {
            if (ports.SelectedItem is not BluetoothChoice device) {
                await RefreshPorts();
                if (ports.SelectedItem is not BluetoothChoice found) return;
                device = found;
            }
            service = await RfcommDeviceService.FromIdAsync(device.Id) ?? throw new IOException("Bluetoothサービス待ち");
            socket = new StreamSocket();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(timeout.Token);
            reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            writer = new DataWriter(socket.OutputStream);
            connected = true;
            if (!await Exchange("PING", "PONG ATOM_NOTIFIER_BT_LP_V3")) throw new IOException("省電力版ファームウェアの応答ではありません");
            firmwarePowerStatus = await ReadResponse("STATUS");
            while (outbound.Count > 0) {
                string channel = outbound.Peek();
                if (!await Exchange("N " + channel, "OK")) throw new IOException("通知の受領確認なし");
                outbound.Dequeue(); messagesSent++;
            }
            if (!await Exchange("SLEEP", "SLEEPING 20")) throw new IOException("スリープの受領確認なし");
            lastSuccessfulExchange = DateTimeOffset.Now;
            completedSleepCycles++;
            lastTransportError = "";
            nextConnectAttempt = DateTimeOffset.UtcNow.AddSeconds(18);
        } catch (Exception e) {
            // Being unreachable while asleep is normal. Keep monitoring and queue.
            lastTransportError = e.Message;
            nextConnectAttempt = DateTimeOffset.UtcNow.AddSeconds(1);
        } finally {
            Disconnect();
            SetState("省電力監視中。通知はATOMの起床時に転送します。");
        }
    }
    static string Key(UserNotification n) => n.Id + ":" + n.CreationTime.UtcTicks;
    async Task Tick() {
        if (busy || listener == null) return;
        busy = true;
        try {
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                throw new Exception("Windowsの通知アクセスが解除されました。");
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var current = new HashSet<string>();
            foreach (var n in notifications.OrderBy(n => n.CreationTime)) {
                string key = Key(n); current.Add(key);
                if (previous.Contains(key)) continue;
                try {
                    string app = n.AppInfo.DisplayInfo.DisplayName;
                    if (!appRule!.IsMatch(app)) continue;
                    var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var texts = binding?.GetTextElements().Select(t => t.Text).ToArray() ?? Array.Empty<string>();
                    string searchable = app + "\n" + string.Join("\n", texts);
                    string channel = dotsRule!.IsMatch(searchable) ? "DOTS" : codexRule!.IsMatch(searchable) ? "CODEX" : "CHAT";
                    if (outbound.Count >= 100) { AddLog("キューが満杯です。新しい通知を破棄しました。"); continue; }
                    if (!outbound.Contains(channel)) outbound.Enqueue(channel);
                    AddLog("通知受信: " + channel + (details.IsChecked == true ? " / " + app + " / " + texts.FirstOrDefault() : ""));
                } catch (Exception e) { AddLog("通知の分類失敗: " + e.Message); }
            }
            previous = current;
            await DeliverAndSleep();
        } catch (Exception e) {
            timer.Stop(); Disconnect();
            SetState("監視停止: " + e.Message + "。「接続・通知を許可」で再開してください。");
        } finally { busy = false; }
    }
}

