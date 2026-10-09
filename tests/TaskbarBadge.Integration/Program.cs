using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using AtomNotifier;

static class Program {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
    [STAThread] static void Main() {
        const string id = "Local.AtomNotifier.BadgeReaderTest";
        SetCurrentProcessExplicitAppUserModelID(id);
        var app = new Application();
        var ready = new TaskCompletionSource();
        uint created = RegisterWindowMessage("TaskbarButtonCreated");
        var window = new Window { Title="ATOM Badge Reader Test", Width=180, Height=80, ShowActivated=false, WindowState=WindowState.Minimized };
        window.SourceInitialized += (_,_) => {
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) => {
                if ((uint)m == created) ready.TrySetResult();
                return IntPtr.Zero;
            });
        };
        window.Loaded += async (_,_) => {
            ITaskbarList3? taskbar = null;
            try {
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
                taskbar = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"))!)!;
                taskbar.HrInit();
                IntPtr handle = new WindowInteropHelper(window).Handle;
                string calibration = System.IO.Path.Combine(AppContext.BaseDirectory, "test-calibration.json");
                System.IO.File.Delete(calibration);
                var reader = new TaskbarBadgeReader(calibration);
                var initial = await Task.Run(() => reader.Read(id));
                Console.WriteLine("Before calibration: " + initial.Presence);
                if (initial.Presence != BadgePresence.Unknown) throw new Exception("Uncalibrated empty badge must not clear");
                taskbar.SetOverlayIcon(handle, System.Drawing.SystemIcons.Information.Handle, "未読のお知らせ 1 件");
                await Task.Delay(800);
                var present = await Task.Run(() => reader.Read(id));
                Console.WriteLine("Overlay set: " + present.Presence + " / " + present.Detail);
                taskbar.SetOverlayIcon(handle, IntPtr.Zero, "");
                await Task.Delay(800);
                var absent = await Task.Run(() => reader.Read(id));
                Console.WriteLine("Overlay removed: " + absent.Presence + " / " + absent.Detail);
                if (present.Presence != BadgePresence.Present || absent.Presence != BadgePresence.Absent)
                    throw new Exception("Taskbar badge metadata integration failed");
                var reloaded = new TaskbarBadgeReader(calibration);
                var persisted = await Task.Run(() => reloaded.Read(id));
                if (persisted.Presence != BadgePresence.Absent) throw new Exception("Calibration did not persist");
                System.IO.File.Delete(calibration);
                Console.WriteLine("Reloaded calibration: " + persisted.Presence);
                Console.WriteLine("PASS: actual Explorer overlay presence and absence read correctly");
            } catch (Exception e) { Console.WriteLine(e); Environment.ExitCode=1; }
            finally { if (taskbar != null) Marshal.FinalReleaseComObject(taskbar); window.Close(); }
        };
        app.Run(window);
    }
}

[ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITaskbarList3 {
    void HrInit(); void AddTab(IntPtr h); void DeleteTab(IntPtr h); void ActivateTab(IntPtr h); void SetActiveAlt(IntPtr h);
    void MarkFullscreenWindow(IntPtr h, [MarshalAs(UnmanagedType.Bool)] bool full);
    void SetProgressValue(IntPtr h, ulong value, ulong total); void SetProgressState(IntPtr h, uint state);
    void RegisterTab(IntPtr h, IntPtr parent); void UnregisterTab(IntPtr h); void SetTabOrder(IntPtr h, IntPtr before); void SetTabActive(IntPtr h, IntPtr parent, uint flags);
    void ThumbBarAddButtons(IntPtr h, uint count, IntPtr buttons); void ThumbBarUpdateButtons(IntPtr h, uint count, IntPtr buttons); void ThumbBarSetImageList(IntPtr h, IntPtr list);
    void SetOverlayIcon(IntPtr h, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
}
