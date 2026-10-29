using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("鼠标电量")]
[assembly: System.Reflection.AssemblyVersion("3.2.0.0")]
namespace MouseBattery {
internal static class Startup {
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string SettingsKey=@"Software\MouseBatteryTray";
    internal static bool Enabled {
        get { using(var key=Registry.CurrentUser.OpenSubKey(RunKey)) return key!=null && (key.GetValue("MouseBatteryTray") as string)==Command; }
    }
    internal static string Command { get { return "\""+Application.ExecutablePath+"\" --background"; } }
    internal static void Set(bool enabled) {
        using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {
            if(enabled) key.SetValue("MouseBatteryTray",Command);
            else key.DeleteValue("MouseBatteryTray",false);
        }
    }
    internal static void Initialize() {
        using(var settings=Registry.CurrentUser.CreateSubKey(SettingsKey)) {
            if(settings.GetValue("Initialized")==null) {
                Set(true); settings.SetValue("Initialized",1,RegistryValueKind.DWord);
            }
        }
    }
}
internal static class Program {
    internal const string EventName=@"Local\MouseBatteryTray.Show.1";
    internal const string ExitEventName=@"Local\MouseBatteryTray.Exit.2";
    internal const string ToggleEventName=@"Local\MouseBatteryTray.ToggleStartup.2";
    [STAThread] static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2 && args[0]=="--diagnose") {
            var trace=new StringBuilder(); var source=new BatterySource { Trace=delegate(string line) { trace.AppendLine(line); } };
            BatteryReading reading=source.Poll();
            trace.AppendLine(reading==null ? "ERROR="+source.LastError : string.Format("OK {0} {1}% {2} {3} unit={4}",reading.Name,reading.Percent,reading.ChargeText,reading.Connection,reading.UnitId));
            File.WriteAllText(args[1],trace.ToString(),Encoding.UTF8); return reading==null ? 1 : 0;
        }
        bool created;
        using(var single=new Mutex(true,@"Local\MouseBatteryTray.Instance.1",out created)) {
            if(!created) {
                string eventName=args.Length>0 && args[0]=="--exit" ? ExitEventName : args.Length>0 && args[0]=="--toggle-startup" ? ToggleEventName : EventName;
                using(var evt=new EventWaitHandle(false,EventResetMode.AutoReset,eventName)) evt.Set();
                return 0;
            }
            if(args.Length>0 && args[0]=="--exit") return 0;
            using(var show=new EventWaitHandle(false,EventResetMode.AutoReset,EventName)) {
                try { Startup.Initialize(); }
                catch(UnauthorizedAccessException e) { MessageBox.Show("无法设置开机启动："+e.Message,"鼠标电量"); }
                catch(SecurityException e) { MessageBox.Show("无法设置开机启动："+e.Message,"鼠标电量"); }
                catch(FileNotFoundException e) { MessageBox.Show("无法设置开机启动："+e.Message,"鼠标电量"); }
                try { using(var context=new TrayContext(show,args.Length>0 && args[0]=="--manage")) Application.Run(context); }
                catch(NotSupportedException e) { MessageBox.Show(e.Message,"鼠标电量",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1; }
                catch(Win32Exception e) { MessageBox.Show(e.Message,"鼠标电量",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1; }
            }
        }
        return 0;
    }
}
internal sealed class TrayContext : ApplicationContext {
    readonly BatterySource source=new BatterySource();
    readonly DisplayState state=new DisplayState();
    readonly Stopwatch uptime=Stopwatch.StartNew();
    readonly TaskbarPublisher publisher=new TaskbarPublisher();
    NativeHost native;
    NotifyIcon trayIcon; Icon trayIconImage;
    bool degraded,trayIconLight,startupEnabled,degradedAnnounced,trayPromoted;
    string degradedReason;
    int trayPercent=int.MinValue,trayPromoteAttempts;
    long nextTrayPromote;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly Form dispatcher=new Form();
    readonly EventWaitHandle show;
    readonly EventWaitHandle quit=new EventWaitHandle(false,EventResetMode.AutoReset,Program.ExitEventName);
    readonly EventWaitHandle toggleStartup=new EventWaitHandle(false,EventResetMode.AutoReset,Program.ToggleEventName);
    readonly string statusPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","status.txt");
    Form manager; Label managerDevice, managerConnection, managerPercent, managerCharge; CheckBox managerStartup; ComboBox positionChoice;
    Panel managerTrack;
    bool managerLight;
    int managerLevel;
    bool busy,closing,updatingSettings,disposed;
    long nextPoll;
    string lastStatus="",failure="尚未读取";
    int position;
    bool showOnStart;
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    internal TrayContext(EventWaitHandle signal,bool manage) {
        show=signal; showOnStart=manage;
        // A taskbar.dll fingerprint mismatch degrades to tray-icon display
        // instead of exiting; every other native failure stays fatal.
        try { native=new NativeHost(); }
        catch(NotSupportedException e) { EnterDegraded(e.Message); }
        startupEnabled=Startup.Enabled;
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\MouseBatteryTray")) {
            object value=key==null ? null : key.GetValue("TaskbarPosition");
            position=value is int ? (int)value : 0;
            if(position<0 || position>3) throw new InvalidDataException("Invalid TaskbarPosition setting");
        }
        // Establish the UI synchronization context before starting asynchronous HID work.
        var handle=dispatcher.Handle;
        timer.Interval=250; timer.Tick+=Tick; timer.Start();
        Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
    }
    private void Tick(object sender,EventArgs e) {
        if(quit.WaitOne(0)) { ExitThread(); return; }
        if(toggleStartup.WaitOne(0)) SetStartup(!Startup.Enabled);
        if(show.WaitOne(0) || showOnStart) { showOnStart=false; ShowManager(); }
        if(degraded) TryPromoteTrayIcon();
        RefreshDisplay();
        if(closing) return;
        if(!busy && uptime.ElapsedMilliseconds>=nextPoll) Poll();
    }
    private async void Poll() {
        busy=true;
        try {
            BatteryReading result=await Task.Run(delegate { return source.Poll(); });
            if(closing) return;
            state.Apply(result,uptime.ElapsedMilliseconds);
            if(result!=null) publisher.MarkSuccess();
            failure=source.LastError;
        } catch(Exception e) {
            // An unexpected escape from the poll task must not tear down the
            // resident process; route it to the same visible error surface as
            // ordinary device failures.
            failure="读取线程异常："+e.Message;
        }
        if(closing) return;
        nextPoll=uptime.ElapsedMilliseconds+5000; busy=false;
        RefreshDisplay();
    }
    private void RefreshDisplay() {
        long now=uptime.ElapsedMilliseconds;
        bool visible=state.Visible(now);
        string text=state.Tooltip(now);
        if(degraded) UpdateTrayIcon(visible,text);
        else {
            publisher.Publish(state,now,position,startupEnabled);
            try { native.Maintain(); }
            catch(Win32Exception e) { ShowNativeError(e.Message);return; }
            catch(NotSupportedException e) { EnterDegraded(e.Message);return; }
        }
        string status=visible ? text : "未连接或电量暂不可用\n"+failure;
        if(manager!=null && !manager.IsDisposed) UpdateManagerDisplay(visible);
        string diagnostic="TaskbarDataVisible="+visible+"\nPosition="+position+"\n"+status+"\nStartup="+startupEnabled+"\nReadError="+failure+"\nNative="+(native!=null ? native.Status : "降级托盘（"+degradedReason+"）");
        if(diagnostic!=lastStatus) {
            try { File.WriteAllText(statusPath,DateTimeOffset.Now.ToString("o")+"\n"+diagnostic,Encoding.UTF8); }
            catch(IOException ex) { ShowFileError(ex.Message); }
            catch(UnauthorizedAccessException ex) { ShowFileError(ex.Message); }
            lastStatus=diagnostic;
        }
    }
    private void ShowFileError(string message) {
        timer.Stop();
        MessageBox.Show("无法写入运行状态："+message,"鼠标电量",MessageBoxButtons.OK,MessageBoxIcon.Error);
        ExitThread();
    }
    private void ShowNativeError(string message) {
        timer.Stop();
        MessageBox.Show(message,"鼠标电量",MessageBoxButtons.OK,MessageBoxIcon.Error);
        ExitThread();
    }
    // Fallback display for an unadapted taskbar.dll: keep reading the mouse and
    // show the percentage as a tray icon until the taskbar module is ported.
    private void EnterDegraded(string reason) {
        degraded=true; degradedReason=reason;
        if(native!=null) { native.Dispose(); native=null; }
        if(trayIcon==null) {
            trayIconLight=AppsUseLightTheme();
            trayIcon=new NotifyIcon { Text="鼠标电量",Visible=true };
            var menu=new ContextMenuStrip();
            menu.Items.Add("打开设置",null,delegate { ShowManager(); });
            menu.Items.Add("退出工具",null,delegate { ExitThread(); });
            trayIcon.ContextMenuStrip=menu;
            trayIcon.DoubleClick+=delegate { ShowManager(); };
        }
        UpdateTrayIcon(state.Visible(uptime.ElapsedMilliseconds),state.Tooltip(uptime.ElapsedMilliseconds));
        if(!degradedAnnounced) { degradedAnnounced=true; trayIcon.ShowBalloonTip(8000,"鼠标电量",reason,ToolTipIcon.Warning); }
    }
    private void UpdateTrayIcon(bool visible,string text) {
        string tip=visible ? text.Replace("\n"," · ") : "鼠标电量不可用";
        if(tip.Length>63) tip=tip.Substring(0,63);
        trayIcon.Text=tip;
        int percent=visible && state.Last!=null ? state.Last.Percent : -1;
        if(percent==trayPercent) return;
        trayPercent=percent;
        if(trayIconImage!=null) trayIconImage.Dispose();
        trayIconImage=TrayIcon.Create(percent,trayIconLight,SystemInformation.SmallIconSize.Width);
        trayIcon.Icon=trayIconImage;
    }
    // Windows 11 parks new tray icons in the overflow flyout; a degraded fallback
    // must be visible at a glance, so promote the entry the shell already created.
    private void TryPromoteTrayIcon() {
        if(trayPromoted || trayPromoteAttempts>=10) return;
        long now=uptime.ElapsedMilliseconds;
        if(now<nextTrayPromote) return;
        nextTrayPromote=now+2000; trayPromoteAttempts++;
        try {
            using(var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings",true)) {
                if(key==null) { trayPromoted=true; return; }
                string exe=Application.ExecutablePath;
                foreach(string name in key.GetSubKeyNames()) {
                    string path;
                    using(var sub=key.OpenSubKey(name)) { if(sub==null) continue; path=sub.GetValue("ExecutablePath") as string; }
                    if(!string.Equals(path,exe,StringComparison.OrdinalIgnoreCase)) continue;
                    using(var sub=key.OpenSubKey(name,true)) if(sub!=null) sub.SetValue("IsPromoted",1,RegistryValueKind.DWord);
                    trayPromoted=true; return;
                }
            }
        }
        // Promotion is a courtesy; an inaccessible key must not break the fallback.
        catch(SecurityException) { trayPromoted=true; }
        catch(UnauthorizedAccessException) { trayPromoted=true; }
        catch(IOException) { trayPromoted=true; }
    }
    internal static bool AppsUseLightTheme() {
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return key!=null && (key.GetValue("AppsUseLightTheme") is int) && (int)key.GetValue("AppsUseLightTheme")!=0;
    }
    private void SetStartup(bool value) {
        try { Startup.Set(value); }
        catch(UnauthorizedAccessException e) { MessageBox.Show("无法修改开机启动："+e.Message,"鼠标电量"); }
        catch(SecurityException e) { MessageBox.Show("无法修改开机启动："+e.Message,"鼠标电量"); }
        catch(FileNotFoundException e) { MessageBox.Show("无法修改开机启动："+e.Message,"鼠标电量"); }
        startupEnabled=Startup.Enabled;
        updatingSettings=true;
        if(managerStartup!=null && !managerStartup.IsDisposed) managerStartup.Checked=startupEnabled;
        updatingSettings=false;
    }
    private void UpdateManagerDisplay(bool visible) {
        Color normalText=managerLight ? Color.FromArgb(35,50,68) : Color.FromArgb(237,243,250);
        Color amberText=managerLight ? Color.FromArgb(145,96,18) : Color.FromArgb(235,193,111);
        Color redText=managerLight ? Color.FromArgb(178,58,67) : Color.FromArgb(247,150,155);

        if(visible && state.Last!=null) {
            managerDevice.Text=state.Last.Name;
            managerPercent.Text=state.Last.Percent+"%";
            managerPercent.ForeColor=state.Last.Percent<=10 ? redText : state.Last.Percent<=20 ? amberText : normalText;
            managerCharge.Text=state.Last.ChargeText;
            managerConnection.Text=state.Failed ? "正在重连 · 上次读数" : ("已连接 · "+(state.Last.Connection=="有线" ? "USB 有线" : "2.4G 无线"));
            managerLevel=state.Last.Percent;
        } else {
            managerDevice.Text="鼠标设备未连接";
            managerPercent.Text="--%";
            managerPercent.ForeColor=normalText;
            managerCharge.Text="暂无电量";
            managerConnection.Text=string.IsNullOrEmpty(failure) ? "等待设备连接" : failure;
            managerLevel=0;
        }
        managerTrack.Invalidate();
    }
    private void ShowManager() {
        if(manager==null || manager.IsDisposed) {
            bool light=AppsUseLightTheme();
            managerLight=light;
            Color background=light ? Color.FromArgb(243,246,250) : Color.FromArgb(24,31,41);
            Color surface=light ? Color.White : Color.FromArgb(34,44,57);
            Color foreground=light ? Color.FromArgb(35,50,68) : Color.FromArgb(237,243,250);
            Color muted=light ? Color.FromArgb(113,128,148) : Color.FromArgb(159,177,198);
            Color accent=light ? Color.FromArgb(66,107,148) : Color.FromArgb(159,189,222);
            Color track=light ? Color.FromArgb(229,237,246) : Color.FromArgb(49,64,82);
            manager=new Form { Text="鼠标电量", AutoScaleMode=AutoScaleMode.None, Font=new Font("Microsoft YaHei UI",9f), ClientSize=new Size(432,356), BackColor=background,ForeColor=foreground, FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, StartPosition=FormStartPosition.CenterScreen };
            var heading=new Label { Text="鼠标电量",Font=new Font("Microsoft YaHei UI",15f,FontStyle.Bold),Location=new Point(20,16),AutoSize=true };
            var subtitle=new Label { Text="任务栏与启动设置",ForeColor=muted,Location=new Point(21,49),AutoSize=true };
            var statusCard=new Panel { Location=new Point(20,82),Size=new Size(392,104),BackColor=surface };
            managerDevice=new Label { Location=new Point(16,14),Size=new Size(220,24),Font=new Font("Segoe UI Variable Text",11f,FontStyle.Bold) };
            managerConnection=new Label { Location=new Point(16,47),Size=new Size(230,32),ForeColor=muted,AutoEllipsis=true };
            managerPercent=new Label { Location=new Point(248,10),Size=new Size(128,36),Font=new Font("Segoe UI Variable Display",22f,FontStyle.Bold),TextAlign=ContentAlignment.MiddleRight };
            managerCharge=new Label { Location=new Point(248,51),Size=new Size(128,22),ForeColor=muted,TextAlign=ContentAlignment.MiddleRight };
            managerTrack=new Panel { Location=new Point(16,86),Size=new Size(360,3),BackColor=track,AccessibleName="当前电量刻度" };
            managerTrack.Paint+=delegate(object sender,PaintEventArgs e) {
                Color color=managerLevel<=20 ? (managerLevel<=10 ? (light ? Color.FromArgb(178,58,67) : Color.FromArgb(247,150,155)) : (light ? Color.FromArgb(145,96,18) : Color.FromArgb(235,193,111))) : accent;
                using(var brush=new SolidBrush(color)) e.Graphics.FillRectangle(brush,0,0,managerTrack.Width*managerLevel/100f,managerTrack.Height);
            };
            statusCard.Controls.AddRange(new Control[]{managerDevice,managerConnection,managerPercent,managerCharge,managerTrack});
            var positionLabel=new Label { Text="显示位置",Location=new Point(20,209),AutoSize=true };
            positionChoice=new ComboBox { Location=new Point(128,202),Size=new Size(284,28),DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,DrawMode=DrawMode.OwnerDrawFixed,BackColor=surface,ForeColor=foreground,AccessibleName="任务栏显示位置" };
            positionChoice.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                if(e.Index<0) return;
                using(var brush=new SolidBrush(surface)) e.Graphics.FillRectangle(brush,e.Bounds);
                var bounds=new Rectangle(e.Bounds.X+6,e.Bounds.Y,e.Bounds.Width-12,e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics,positionChoice.Items[e.Index].ToString(),e.Font,bounds,foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
            positionChoice.Items.AddRange(new object[]{"系统托盘与时钟区域左侧","紧邻时钟左侧","时钟右侧","任务栏最左侧"});
            positionChoice.SelectedIndex=position;
            positionChoice.SelectedIndexChanged+=delegate {
                int selected=positionChoice.SelectedIndex;
                try { using(var key=Registry.CurrentUser.CreateSubKey(@"Software\MouseBatteryTray")) key.SetValue("TaskbarPosition",selected,RegistryValueKind.DWord); position=selected; }
                catch(UnauthorizedAccessException e) { MessageBox.Show("无法保存位置："+e.Message,"鼠标电量"); }
                catch(SecurityException e) { MessageBox.Show("无法保存位置："+e.Message,"鼠标电量"); }
            };
            managerStartup=new CheckBox { Text="登录 Windows 后自动启动",AccessibleName="登录 Windows 后自动启动",AccessibleDescription="控制鼠标电量工具是否在登录后后台运行",Location=new Point(20,247),AutoSize=true,Checked=Startup.Enabled };
            managerStartup.CheckedChanged+=delegate { if(!updatingSettings) SetStartup(managerStartup.Checked); };
            var note=new Label { Text="关闭窗口后继续在任务栏显示",Font=new Font("Microsoft YaHei UI",8.5f),ForeColor=muted,Location=new Point(21,282),AutoSize=true };
            var exit=new Button { Text="退出工具",Location=new Point(20,310),Size=new Size(96,30),FlatStyle=FlatStyle.Flat,BackColor=background,ForeColor=muted };
            exit.FlatAppearance.BorderSize=0; exit.Click+=delegate { ExitThread(); };
            var close=new Button { Text="完成",AccessibleName="完成设置并关闭窗口",Location=new Point(316,310),Size=new Size(96,30),FlatStyle=FlatStyle.Flat,BackColor=accent,ForeColor=light ? Color.White : background };
            close.FlatAppearance.BorderSize=0; close.Click+=delegate { manager.Close(); };
            manager.Controls.AddRange(new Control[]{heading,subtitle,statusCard,positionLabel,positionChoice,managerStartup,note,close,exit});
            // This programmatic layout is measured in 96-DPI units. Scale its
            // bounds once; the font already uses the current display DPI.
            using(var graphics=manager.CreateGraphics()) {
                float uiScale=graphics.DpiX/96f;
                manager.Scale(new SizeF(uiScale,uiScale));
            }
            int darkCaption=light ? 0 : 1;
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(manager.Handle,20,ref darkCaption,4));
        }
        RefreshDisplay(); manager.Show();
        // A background launcher can suppress the first native ShowWindow call.
        // Explicitly show again after WinForms has created/shown the form.
        ShowWindow(manager.Handle,1); manager.Activate();
    }
    protected override void ExitThreadCore() {
        closing=true; timer.Stop();
        if(manager!=null) manager.Close();
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing) {
        if(disposing && !disposed) {
            disposed=true; timer.Dispose();
            if(trayIcon!=null) { trayIcon.Visible=false; trayIcon.Dispose(); }
            if(trayIconImage!=null) trayIconImage.Dispose();
            publisher.Dispose(); if(native!=null) native.Dispose(); quit.Dispose(); toggleStartup.Dispose(); dispatcher.Dispose(); if(manager!=null) manager.Dispose();
        }
        base.Dispose(disposing);
    }
}
}
