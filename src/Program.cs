using System;
using System.Collections.Generic;
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
[assembly: System.Reflection.AssemblyVersion("4.1.0.0")]
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
            var readings=source.Poll(0);
            trace.AppendLine(readings.Count==0 ? "ERROR="+source.LastError : string.Join("\n",Array.ConvertAll(readings.ToArray(),delegate(BatteryReading r) { return string.Format("OK {0} {1}% {2} {3} unit={4}",r.Name,r.Percent,r.ChargeText,r.Connection,r.UnitId); })));
            File.WriteAllText(args[1],trace.ToString(),Encoding.UTF8); return readings.Count==0 ? 1 : 0;
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
// Mini-taskbar schematic: the four position presets are spatial anchors on a
// drawn taskbar, replacing a text dropdown for a spatial concept. Keyboard and
// an accessible name/description keep parity with the previous combo box.
internal sealed class PositionPicker : Control {
    internal static readonly string[] PositionNames={"系统托盘与时钟区域左侧","紧邻时钟左侧","时钟右侧","任务栏最左侧"};
    static readonly int[] VisualOrder={3,0,1,2};
    int position,hover=-1,percent=-1;
    bool light=true;
    Color barFill,lineColor,blockColor,pillFill,pillText,anchorColor,hoverFill,captionColor;
    internal event Action PositionChanged;
    public PositionPicker() {
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
        BackColor=Color.Transparent; TabStop=true; Cursor=Cursors.Hand;
        AccessibleName="任务栏显示位置";
        AccessibleDescription="迷你任务栏示意图：点击四个落点，或用左右方向键切换电量显示位置";
    }
    internal static System.Drawing.Drawing2D.GraphicsPath RoundPath(RectangleF bounds,float radius) {
        var path=new System.Drawing.Drawing2D.GraphicsPath();
        float d=radius*2;
        path.AddArc(bounds.Left,bounds.Top,d,d,180,90);
        path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
        path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);
        path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);
        path.CloseFigure();
        return path;
    }
    internal int Position { get { return position; } set { position=Math.Max(0,Math.Min(3,value)); Invalidate(); } }
    internal int Percent { set { percent=value; Invalidate(); } }
    internal void SetTheme(bool lightTheme,Color foreground,Color muted,Color accent,Color track) {
        light=lightTheme; barFill=track; captionColor=muted; pillText=foreground;
        lineColor=Ui.GetColor(light, Ui.BaseLight, Ui.BaseDark);
        blockColor=Ui.GetSurfaceColor(light);
        pillFill=light ? Color.FromArgb(46,66,107,148) : Color.FromArgb(58,159,189,222);
        anchorColor=light ? Color.FromArgb(160,113,128,148) : Color.FromArgb(160,159,177,198);
        hoverFill=light ? Color.FromArgb(28,66,107,148) : Color.FromArgb(40,159,189,222);
        Invalidate();
    }
    // Zones are indexed by position value; artwork is drawn in the gaps so a
    // drawn element never sits under a clickable slot.
    Rectangle[] Zones() {
        float w=Width,h=Height*0.55f;
        var zones=new Rectangle[4]; float m=Math.Max(4,w*0.02f);
        zones[3]=new Rectangle((int)m,0,(int)(w*0.085f),(int)h);
        float appL=m+w*0.11f,appW=w*0.26f;
        zones[0]=new Rectangle((int)(appL+appW+w*0.015f),0,(int)(w*0.10f),(int)h);
        float trayL=zones[0].Right+w*0.015f,trayW=w*0.10f;
        zones[1]=new Rectangle((int)(trayL+trayW+w*0.015f),0,(int)(w*0.105f),(int)h);
        float clockL=zones[1].Right+w*0.02f;
        zones[2]=new Rectangle((int)(clockL+w*0.015f),0,(int)Math.Max(24,w-(clockL+w*0.015f)-m),(int)h);
        return zones;
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var zones=Zones(); float h=zones[0].Height;
        var bar=new RectangleF(zones[3].Left-6,0,zones[2].Right-zones[3].Left+12,h);
        using(var path=RoundPath(bar,7)) {
            using(var b=new SolidBrush(barFill)) g.FillPath(b,path);
            using(var p=new Pen(lineColor)) g.DrawPath(p,path);
        }
        float appL=zones[3].Right+8,appR=zones[0].Left-8,bw=(appR-appL-10)/6f;
        using(var b=new SolidBrush(blockColor)) for(int i=0;i<6;i++) {
            using(var path=RoundPath(new RectangleF(appL+i*(bw+2),h*0.30f,bw,h*0.40f),2)) g.FillPath(b,path);
        }
        float sq=Math.Max(5,h*0.24f),trayL=zones[0].Right+8;
        using(var b=new SolidBrush(blockColor)) for(int i=0;i<3;i++) g.FillRectangle(b,trayL+i*(sq+4),h*0.5f-sq/2f,sq,sq);
        var clock=new RectangleF(zones[1].Right+8,h*0.24f,zones[2].Left-8-(zones[1].Right+8),h*0.52f);
        using(var path=RoundPath(clock,3)) using(var b=new SolidBrush(blockColor)) g.FillPath(b,path);
        for(int i=0;i<4;i++) {
            if(i==position) continue;
            float cx=zones[i].Left+zones[i].Width/2f,cy=zones[i].Top+zones[i].Height/2f;
            using(var p=new Pen(anchorColor)) g.DrawEllipse(p,cx-3.5f,cy-3.5f,7,7);
        }
        if(hover>=0&&hover!=position) using(var b=new SolidBrush(hoverFill)) using(var path=RoundPath(new RectangleF(zones[hover].X,zones[hover].Y,zones[hover].Width,zones[hover].Height),5)) g.FillPath(b,path);
        var slot=zones[position]; float pw=Math.Min(36,slot.Width-6),ph=h-12;
        var pill=new RectangleF(slot.X+(slot.Width-pw)/2f,slot.Y+(slot.Height-ph)/2f,pw,ph);
        using(var path=RoundPath(pill,5)) {
            using(var b=new SolidBrush(pillFill)) g.FillPath(b,path);
            using(var p=new Pen(lineColor)) g.DrawPath(p,path);
        }
        string label=percent<0 ? "--" : percent.ToString();
        using(var font=new Font(Font.FontFamily,7.5f,FontStyle.Bold,GraphicsUnit.Point))
            TextRenderer.DrawText(g,label,font,Rectangle.Round(pill),pillText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        if(Focused) ControlPaint.DrawFocusRectangle(g,slot);
        using(var font=new Font(Font.FontFamily,8.25f,FontStyle.Regular,GraphicsUnit.Point))
            TextRenderer.DrawText(g,"位置："+PositionNames[position],font,new Point((int)bar.X,(int)(h+5)),captionColor);
    }
    int ZoneAt(Point location) { var zones=Zones(); for(int i=0;i<4;i++) if(zones[i].Contains(location)) return i; return -1; }
    void Apply(int value) {
        bool changed=value!=position;
        position=value; Focus(); Invalidate();
        if(changed && PositionChanged!=null) PositionChanged();
    }
    protected override void OnMouseMove(MouseEventArgs e) { int z=ZoneAt(e.Location); if(z!=hover) { hover=z; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { if(hover!=-1) { hover=-1; Invalidate(); } }
    protected override void OnMouseClick(MouseEventArgs e) { int z=ZoneAt(e.Location); if(z>=0) Apply(z); base.OnMouseClick(e); }
    protected override bool IsInputKey(Keys keyData) { var k=keyData&Keys.KeyCode; if(k==Keys.Left||k==Keys.Right||k==Keys.Up||k==Keys.Down) return true; return base.IsInputKey(keyData); }
    protected override void OnKeyDown(KeyEventArgs e) {
        int idx=Array.IndexOf(VisualOrder,position);
        if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Up) idx=(idx+VisualOrder.Length-1)%VisualOrder.Length;
        else if(e.KeyCode==Keys.Right||e.KeyCode==Keys.Down) idx=(idx+1)%VisualOrder.Length;
        else { base.OnKeyDown(e); return; }
        e.Handled=true; Apply(VisualOrder[idx]);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { hover=-1; Invalidate(); base.OnLostFocus(e); }
}
// Shared rounded surface for cards and the degraded banner: soft fill with a
// 1 px mist-blue stroke, matching the taskbar pill geometry.
internal sealed class RoundedPanel : Panel {
    readonly Color fillColor,strokeColor;
    public RoundedPanel(Color fill,Color stroke) { fillColor=fill; strokeColor=stroke; SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true); }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var path=PositionPicker.RoundPath(new RectangleF(0.5f,0.5f,Width-1,Height-1),MouseBattery.Ui.CornerButton)) {
            using(var b=new SolidBrush(fillColor)) g.FillPath(b,path);
            using(var p=new Pen(strokeColor)) g.DrawPath(p,path);
        }
    }
}
// Owner-drawn switch on CheckBox semantics: Checked stays authoritative, so
// WinForms automation keeps reporting the real ToggleState while the paint
// becomes a proper toggle instead of the legacy check box.
internal sealed class ToggleSwitch : CheckBox {
    readonly Color onColor,offColor,knobColor,textColor;
    public ToggleSwitch(Color on,Color off,Color knob,Color text) {
        onColor=on; offColor=off; knobColor=knob; textColor=text;
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);
        Size=new Size(232,26);
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;
        g.Clear(Parent!=null ? Parent.BackColor : SystemColors.Control);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float trackY=(Height-18)/2f;
        using(var path=PositionPicker.RoundPath(new RectangleF(0,trackY,36,18),9)) {
            using(var b=new SolidBrush(Checked ? onColor : offColor)) g.FillPath(b,path);
        }
        using(var b=new SolidBrush(knobColor)) g.FillEllipse(b,Checked ? 20 : 2,trackY+2,14,14);
        if(!string.IsNullOrEmpty(Text)) TextRenderer.DrawText(g,Text,Font,new Point(46,(Height-Font.Height)/2),textColor);
        if(Focused) ControlPaint.DrawFocusRectangle(g,new Rectangle(0,(int)trackY-2,40,22));
    }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
}
// Never shown; exists to receive WM_INPUT while other windows have focus
// (RIDEV_INPUTSINK) so the tool can tell which mouse the user is moving.
internal sealed class MessageWindow : Form {
    public Action<Message> OnMessage;
    protected override void WndProc(ref Message m) { if(OnMessage!=null) OnMessage(m); base.WndProc(ref m); }
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
    readonly MessageWindow dispatcher=new MessageWindow();
    readonly Dictionary<IntPtr,string> rawNames=new Dictionary<IntPtr,string>();
    bool rawInputOk;
    readonly EventWaitHandle show;
    readonly EventWaitHandle quit=new EventWaitHandle(false,EventResetMode.AutoReset,Program.ExitEventName);
    readonly EventWaitHandle toggleStartup=new EventWaitHandle(false,EventResetMode.AutoReset,Program.ToggleEventName);
    readonly string statusPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","status.txt");
    Form manager; Label managerDevice, managerConnection, managerPercent, managerUnit, managerCharge, managerCompanion; Panel chip; CheckBox managerStartup; PositionPicker positionPicker;
    Panel managerTrack;
    const float NotchHalf=0.75f; // notch centers on the 20%/10% mark (width 1.5)
    bool managerLight;
    int managerLevel;
    bool busy,closing,updatingSettings,disposed;
    long nextPoll; int pollInterval=5000;
    string lastStatus="",failure="尚未读取";
    int position;
    bool showOnStart;
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [StructLayout(LayoutKind.Sequential)] struct RawInputDevice { public ushort UsagePage,Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] struct RawInputHeader { public uint Type,Size; public IntPtr Device,WParam; }
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(RawInputDevice[] devices,uint count,uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr hRawInput,uint command,byte[] data,ref uint size,uint headerSize);
    [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceInfoW(IntPtr hDevice,uint command,IntPtr data,ref uint size);
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
        // Raw input sink: every mouse movement reports its device so the pill
        // can follow whichever mouse the user is actually holding.
        var rid=new RawInputDevice[1];
        rid[0]=new RawInputDevice { UsagePage=1,Usage=2,Flags=0x100,Target=handle }; // usagePage 1 generic desktop, usage 2 mouse, INPUTSINK
        rawInputOk=RegisterRawInputDevices(rid,1,(uint)Marshal.SizeOf(typeof(RawInputDevice)));
        dispatcher.OnMessage=delegate(Message m) { if(m.Msg==0x00FF) HandleRawInput(m); };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged+=OnPreferenceChanged;
        timer.Interval=250; timer.Tick+=Tick; timer.Start();
        Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
    }
    // The settings window and the degraded tray icon pick their palette once;
    // follow a live light/dark switch by rebuilding both from the fresh theme.
    private void OnPreferenceChanged(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs e) {
        dispatcher.BeginInvoke((MethodInvoker)delegate {
            bool light=AppsUseLightTheme();
            if(light==managerLight) return;
            managerLight=light;
            if(trayIcon!=null) { trayIconLight=light; trayPercent=int.MinValue; UpdateTrayIcon(state.Visible(uptime.ElapsedMilliseconds),state.Tooltip(uptime.ElapsedMilliseconds)); }
            if(manager!=null && !manager.IsDisposed) { manager.Close(); ShowManager(); }
        });
    }
    void HandleRawInput(Message m) {
        try {
            // RID_INPUT needs header + RAWMOUSE, not just the header; an
            // undersized buffer plus a busy UI thread (settings opening) is a
            // heap-corruption recipe this build actually hit (0xC0000374).
            uint headerSize=(uint)Marshal.SizeOf(typeof(RawInputHeader));
            var data=new byte[256]; uint got=(uint)data.Length;
            if(GetRawInputData(m.LParam,0x10000005,data,ref got,headerSize)==unchecked((uint)-1)) return;
            if(got<headerSize) return;
            IntPtr hDevice=(IntPtr)Marshal.ReadInt64(data,8);
            if(hDevice==IntPtr.Zero) return;
            string path;
            if(!rawNames.TryGetValue(hDevice,out path)) {
                uint size=0;
                if(GetRawInputDeviceInfoW(hDevice,0x20000007,IntPtr.Zero,ref size)==unchecked((uint)-1) || size<2) { rawNames[hDevice]=null; return; }
                var buf=Marshal.AllocHGlobal((int)size+16);
                try {
                    uint cap=size+16;
                    if(GetRawInputDeviceInfoW(hDevice,0x20000007,buf,ref cap)==unchecked((uint)-1) || cap>size+16) { rawNames[hDevice]=null; return; }
                    path=Marshal.PtrToStringUni(buf);
                } finally { Marshal.FreeHGlobal(buf); }
                rawNames[hDevice]=null;
                if(path==null) return;
                rawNames[hDevice]=path;
            }
            if(path==null) return;
            string key=state.KeyForRawPath(path.ToLowerInvariant());
            if(key!=null) state.SetActive(key);
        } catch { }
    }
    internal static string ShortHash(string reason) {
        if(reason==null) return "详见 data\\status.txt";
        var match=System.Text.RegularExpressions.Regex.Match(reason,"[0-9A-Fa-f]{64}");
        if(!match.Success) return "详见 data\\status.txt";
        string hash=match.Value.ToUpperInvariant();
        return hash.Substring(0,8)+"…"+hash.Substring(60,4);
    }
    static System.Drawing.Color GetColor(bool light, System.Drawing.Color c1, System.Drawing.Color c2) {
        return light ? c1 : c2;
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
            List<BatteryReading> result=await Task.Run(delegate { return source.Poll(uptime.ElapsedMilliseconds); });
            if(closing) return;
            state.Apply(result.ToArray(),uptime.ElapsedMilliseconds);
            state.MarkFailed(source.FailedPaths.ToArray(),uptime.ElapsedMilliseconds);
            if(result.Count>0) publisher.MarkSuccess();
            failure=source.LastError;
            // Per-device backoff lives inside BatterySource; the outer cadence
            // only reacts to whether anything at all answered this cycle.
            pollInterval=result.Count>0 ? 5000 : Math.Min(10000,pollInterval+5000);
        } catch(Exception e) {
            // An unexpected escape from the poll task must not tear down the
            // resident process; route it to the same visible error surface as
            // ordinary device failures.
            failure="读取线程异常："+e.Message;
        }
        if(closing) return;
        nextPoll=uptime.ElapsedMilliseconds+pollInterval; busy=false;
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
        string diagnostic="TaskbarDataVisible="+visible+"\nPosition="+position+"\n"+status+"\nStartup="+startupEnabled+"\nReadError="+failure+"\nActiveKey="+(state.ActiveKey??"(none)")+"\nRawInput="+(rawInputOk?"on":"off")+"\nDevices="+state.Describe(now)+"\nPollInterval="+pollInterval+"\nNative="+(native!=null ? native.Status : "降级托盘（"+degradedReason+"）");
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
        int percent=visible && state.Displayed(uptime.ElapsedMilliseconds)!=null ? state.Displayed(uptime.ElapsedMilliseconds).Percent : -1;
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
        Color normalText=GetColor(managerLight, MouseBattery.Ui.TextLight, MouseBattery.Ui.TextDark);
        Color amberText=GetColor(managerLight, MouseBattery.Ui.YellowLight, MouseBattery.Ui.YellowDark);
        Color redText=GetColor(managerLight, MouseBattery.Ui.RedLight, MouseBattery.Ui.RedDark);

        var shown=visible ? state.Displayed(uptime.ElapsedMilliseconds) : null;
        if(shown!=null) {
            managerDevice.Text=shown.Name;
            managerPercent.Text=shown.Percent+"";
            managerPercent.ForeColor=shown.Percent<=10 ? redText : shown.Percent<=20 ? amberText : normalText;
            managerCharge.Text=shown.Gaming ? shown.ChargeText : "AA 电池供电";
            managerConnection.Text=state.DisplayedFailed(uptime.ElapsedMilliseconds) ? "正在重连 · 上次读数" : shown.Connection; // F8: product line lives in the chip, not the connection string
            managerUnit.Visible=true;
            managerLevel=shown.Percent;
        } else {
            managerDevice.Text="鼠标设备未连接";
            managerPercent.Text="--";
            managerPercent.ForeColor=normalText;
            managerCharge.Text="暂无电量";
            managerConnection.Text=string.IsNullOrEmpty(failure) ? "等待设备连接" : failure;
            managerUnit.Visible=false; // F7: "--" alone, never "-- %"
            managerLevel=-1;
        }
        // Companion device line (design doc §3.1 priority 5): tooltip line 2.
        string tip=state.Tooltip(uptime.ElapsedMilliseconds);
        int nl=tip.IndexOf('\n');
        bool hasCompanion=visible && nl>=0 && nl+1<tip.Length;
        managerCompanion.Visible=hasCompanion;
        if(hasCompanion) managerCompanion.Text=tip.Substring(nl+1);
        managerTrack.Invalidate();
        if(positionPicker!=null && !positionPicker.IsDisposed) { positionPicker.Percent=managerLevel; positionPicker.Invalidate(); }
        if(manager!=null && !manager.IsDisposed) chip.Invalidate(); // product-line chip tracks the displayed device
    }
    private void ShowManager() {
        if(manager==null || manager.IsDisposed) {
            bool light=AppsUseLightTheme();
            managerLight=light;
            Color background=MouseBattery.Ui.Base(light);
            Color surface=MouseBattery.Ui.Surface(light);
            Color foreground=MouseBattery.Ui.Text(light);
            Color muted=MouseBattery.Ui.Subtext(light);
            Color accent=MouseBattery.Ui.Accent(light);
            Color border=MouseBattery.Ui.Border(light);
            Color scrim=MouseBattery.Ui.Scrim(light);
            manager=new Form { Text="鼠标电量", AutoScaleMode=AutoScaleMode.None, Font=new Font("Microsoft YaHei UI",9f), ClientSize=new Size(MouseBattery.Ui.WidthWindow,degraded ? MouseBattery.Ui.HeightWindowDegraded : MouseBattery.Ui.HeightWindowNormal), BackColor=background,ForeColor=foreground, FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, StartPosition=FormStartPosition.CenterScreen };
            var controls=new List<Control>();
            int headerY=20;
            var mark=new Panel { Location=new Point(20,headerY),Size=new Size(26,26) };
            mark.Paint+=delegate(object sender,PaintEventArgs e) {
                var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int level=Math.Max(0,managerLevel);
                Color shell=level<=0 ? border : level<=10 ? MouseBattery.Ui.Red(light) : level<=20 ? MouseBattery.Ui.Yellow(light) : accent;
                using(var pen=new Pen(shell,1.6f)) using(var path=PositionPicker.RoundPath(new RectangleF(1,2,20,20),5f)) g.DrawPath(pen,path);
                using(var b=new SolidBrush(shell)) { g.FillRectangle(b,22,8,3,8); if(level>0) { int h=(int)(16*level/100f); if(h>1) using(var path=PositionPicker.RoundPath(new RectangleF(4.5f,3+16-h,13,h),2f)) g.FillPath(b,path); } }
            };
            var heading=new Label { Text="鼠标电量",Font=new Font("Microsoft YaHei UI",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeDisplayTitle),FontStyle.Bold),Location=new Point(58,headerY-2),AutoSize=true };
            var subtitle=new Label { Text="任务栏与启动设置",Font=new Font("Microsoft YaHei UI",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeMeta)),ForeColor=MouseBattery.Ui.WithAlpha(muted,210),Location=new Point(58,headerY+24),AutoSize=true };
            controls.Add(mark); controls.Add(heading); controls.Add(subtitle);
            int bodyY=76;
            if(degraded) {
                Color bannerBg=MouseBattery.Ui.Overlay(background,255,MouseBattery.Ui.WithAlpha(MouseBattery.Ui.Yellow(light),light?22:26));
                Color bannerLine=MouseBattery.Ui.WithAlpha(MouseBattery.Ui.Yellow(light),105);
                Color bannerText=light ? MouseBattery.Ui.YellowLight : MouseBattery.Ui.WithAlpha(MouseBattery.Ui.YellowDark,245);
                var banner=new RoundedPanel(bannerBg,bannerLine) { Location=new Point(20,bodyY),Size=new Size(MouseBattery.Ui.WidthCardWindow,58) };
                var rail=new Panel { Location=new Point(0,0),Size=new Size(3,58),BackColor=MouseBattery.Ui.WithAlpha(MouseBattery.Ui.Yellow(light),210) };
                banner.Controls.Add(rail);
                var bannerLabel=new Label { Text="任务栏版本未适配，当前以托盘图标显示电量（指纹 "+ShortHash(degradedReason)+"），适配后自动恢复",Location=new Point(20,6),Size=new Size(470,46),Font=new Font("Microsoft YaHei UI",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeMeta)),ForeColor=bannerText };
                banner.Controls.Add(bannerLabel);
                controls.Add(banner);
                bodyY+=70;
            }
            var statusCard=new RoundedPanel(surface,border) { Location=new Point(20,bodyY),Size=new Size(MouseBattery.Ui.WidthCardWindow,172) };
            managerDevice=new Label { Location=new Point(20,20),Size=new Size(320,24),Font=new Font("Segoe UI Variable Text",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeHeading),FontStyle.Bold) };
            chip=new Panel { Location=new Point(380,18),Size=new Size(120,20) };
            chip.Paint+=delegate(object sender,PaintEventArgs e) {
                var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var shownNow=state.Displayed(uptime.ElapsedMilliseconds);
                bool gamingNow=shownNow!=null && shownNow.Gaming;
                Color id=MouseBattery.Ui.Identity(gamingNow,light);
                using(var b=new SolidBrush(MouseBattery.Ui.Overlay(surface,255,MouseBattery.Ui.WithAlpha(id,26)))) using(var path=PositionPicker.RoundPath(new RectangleF(0.5f,0.5f,chip.Width-1,chip.Height-1),10f)) g.FillPath(b,path);
                using(var p=new Pen(MouseBattery.Ui.WithAlpha(id,110))) using(var path=PositionPicker.RoundPath(new RectangleF(0.5f,0.5f,chip.Width-1,chip.Height-1),10f)) g.DrawPath(p,path);
                g.FillEllipse(new SolidBrush(id),8,7,6,6);
                TextRenderer.DrawText(g,gamingNow?"PRO X":"Signature",new Font("Segoe UI",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeFaint),FontStyle.Bold),new Rectangle(18,0,chip.Width-20,chip.Height),id,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            };
            managerPercent=new Label { Location=new Point(20,46),Size=new Size(160,52),Font=new Font("Segoe UI Variable Display",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeNumeral),FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft };
            managerUnit=new Label { Text="%",Location=new Point(180,72),Size=new Size(30,24),Font=new Font("Segoe UI Variable Display",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeUnit)),ForeColor=MouseBattery.Ui.WithAlpha(muted,205),TextAlign=ContentAlignment.MiddleLeft };
            managerCharge=new Label { Location=new Point(300,55),Size=new Size(200,18),Font=new Font("Segoe UI Variable Text",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeBody)),TextAlign=ContentAlignment.MiddleRight };
            managerConnection=new Label { Location=new Point(300,75),Size=new Size(200,16),Font=new Font("Segoe UI Variable Text",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeMeta)),ForeColor=MouseBattery.Ui.WithAlpha(muted,190),TextAlign=ContentAlignment.MiddleRight,AutoEllipsis=true };
            managerTrack=new Panel { Location=new Point(20,114),Size=new Size(MouseBattery.Ui.WidthCardContent,MouseBattery.Ui.HeightTrack),BackColor=border,AccessibleName="当前电量刻度" };
            managerCompanion=new Label { Location=new Point(20,134),Size=new Size(480,17),Font=new Font("Segoe UI Variable Text",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeMeta)),ForeColor=MouseBattery.Ui.WithAlpha(muted,190),Visible=false };
            managerTrack.Paint+=delegate(object sender,PaintEventArgs e) {
                int level=Math.Max(0,managerLevel);
                var shownNow=state.Displayed(uptime.ElapsedMilliseconds);
                bool gamingNow=shownNow!=null && shownNow.Gaming;
                Color fill=level<0 ? muted : level<=10 ? MouseBattery.Ui.Red(light) : level<=20 ? MouseBattery.Ui.Yellow(light) : MouseBattery.Ui.Identity(gamingNow,light);
                using(var brush=new SolidBrush(fill)) e.Graphics.FillRectangle(brush,0,0,managerTrack.Width*level/100f,managerTrack.Height);
                Color neutralTick=MouseBattery.Ui.WithAlpha(muted,110);
                Color highTick=level>=0&&level<=20 ? MouseBattery.Ui.Yellow(light) : neutralTick;
                Color critTick=level>=0&&level<=10 ? MouseBattery.Ui.Red(light) : highTick;
                using(var b=new SolidBrush(highTick)) e.Graphics.FillRectangle(b,managerTrack.Width*0.2f-NotchHalf,0,MouseBattery.Ui.NotchWidth,managerTrack.Height);
                using(var b=new SolidBrush(critTick)) e.Graphics.FillRectangle(b,managerTrack.Width*0.1f-NotchHalf,0,MouseBattery.Ui.NotchWidth,managerTrack.Height);
            };
            statusCard.Controls.AddRange(new Control[]{managerDevice,chip,managerPercent,managerUnit,managerCharge,managerConnection,managerTrack,managerCompanion});
            controls.Add(statusCard);
            bodyY+=172+12;
            int rowY;
            if(!degraded) {
                var cardB=new RoundedPanel(scrim,MouseBattery.Ui.WithAlpha(border,150)) { Location=new Point(20,bodyY),Size=new Size(MouseBattery.Ui.WidthCardWindow,134) };
                var pickerLabel=new Label { Text="显示位置",Font=new Font("Microsoft YaHei UI",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeBody)),Location=new Point(20,16),AutoSize=true };
                cardB.Controls.Add(pickerLabel);
                positionPicker=new PositionPicker { Location=new Point(20,40),Size=new Size(480,68) };
                positionPicker.SetTheme(light,foreground,muted,accent,scrim);
                positionPicker.Position=position;
                positionPicker.PositionChanged+=delegate {
                    int selected=positionPicker.Position;
                    try { using(var key=Registry.CurrentUser.CreateSubKey(@"Software\MouseBatteryTray")) key.SetValue("TaskbarPosition",selected,RegistryValueKind.DWord); position=selected; }
                    catch(UnauthorizedAccessException e) { MessageBox.Show("无法保存位置："+e.Message,"鼠标电量"); }
                    catch(SecurityException e) { MessageBox.Show("无法保存位置："+e.Message,"鼠标电量"); }
                };
                cardB.Controls.Add(positionPicker);
                controls.Add(cardB);
                rowY=bodyY+134+14;
            } else rowY=bodyY+10;
            managerStartup=new ToggleSwitch(accent,border,MouseBattery.Ui.OnAccent(light),foreground) { Text="登录 Windows 后自动启动",AccessibleName="登录 Windows 后自动启动",AccessibleDescription="控制鼠标电量工具是否在登录后后台运行",Location=new Point(20,rowY),Checked=Startup.Enabled };
            managerStartup.CheckedChanged+=delegate { if(!updatingSettings) SetStartup(managerStartup.Checked); };
            controls.Add(managerStartup);
            var hairline=new Panel { Location=new Point(20,rowY+40),Size=new Size(MouseBattery.Ui.WidthCardWindow,1),BackColor=MouseBattery.Ui.WithAlpha(border,115) };
            controls.Add(hairline);
            var note=new Label { Text=degraded ? "关闭窗口后继续在托盘显示" : "关闭窗口后继续在任务栏显示",Font=new Font("Microsoft YaHei UI",MouseBattery.Ui.PtFromDip(MouseBattery.Ui.SizeMicro)),ForeColor=muted,Location=new Point(20,rowY+50),AutoSize=true };
            int buttonY=rowY+78;
            var exit=new Button { Text="退出工具",Location=new Point(MouseBattery.Ui.WidthWindow-20-96-12-96,buttonY),Size=new Size(96,34),FlatStyle=FlatStyle.Flat,BackColor=background,ForeColor=muted };
            exit.FlatAppearance.BorderSize=0; exit.FlatAppearance.MouseOverBackColor=MouseBattery.Ui.GhostHover(light); exit.FlatAppearance.MouseDownBackColor=MouseBattery.Ui.GhostPressed(light); exit.Click+=delegate { ExitThread(); };
            var close=new Button { Text="完成",AccessibleName="完成设置并关闭窗口",Location=new Point(MouseBattery.Ui.WidthWindow-20-96,buttonY),Size=new Size(96,34),FlatStyle=FlatStyle.Flat,BackColor=accent,ForeColor=MouseBattery.Ui.OnAccent(light) };
            close.FlatAppearance.BorderSize=0; close.FlatAppearance.MouseOverBackColor=MouseBattery.Ui.AccentPlus(light); close.FlatAppearance.MouseDownBackColor=MouseBattery.Ui.Overlay(MouseBattery.Ui.Accent(light),255,MouseBattery.Ui.WithAlpha(foreground,54)); close.Click+=delegate { manager.Close(); };
            controls.Add(note); controls.Add(exit); controls.Add(close);
            manager.Controls.AddRange(controls.ToArray());
            // This programmatic layout is measured in 96-DPI units. Scale its
            // bounds once; the font already uses the current display DPI.
            using(var graphics=manager.CreateGraphics()) {
                float uiScale=graphics.DpiX/96f;
                manager.Scale(new SizeF(uiScale,uiScale));
            }
            RefreshDisplay(); manager.Show();
            ShowWindow(manager.Handle,1); manager.Activate();
        }
        RefreshDisplay(); manager.Show();
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
            Microsoft.Win32.SystemEvents.UserPreferenceChanged-=OnPreferenceChanged;
            if(trayIcon!=null) { trayIcon.Visible=false; trayIcon.Dispose(); }
            if(trayIconImage!=null) trayIconImage.Dispose();
            publisher.Dispose(); if(native!=null) native.Dispose(); quit.Dispose(); toggleStartup.Dispose(); dispatcher.Dispose(); if(manager!=null) manager.Dispose();
        }
        base.Dispose(disposing);
    }
}
}
