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
[assembly: System.Reflection.AssemblyVersion("4.5.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("4.5.0.0")]
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
    const float BarWidth=480f,BarHeight=48f;
    float PaintScale() { return (float)Width/BarWidth; } // control is manager.Scale-d; artwork is not
    int position,hover=-1,percent=-1;
    bool light=true,gaming=false,pressed=false;
    internal event Action PositionChanged;
    public PositionPicker() {
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
        BackColor=Color.Transparent; TabStop=true; Cursor=Cursors.Hand;
        AccessibleName="任务栏显示位置";
        AccessibleDescription="四个任务栏位置选项，点击选择，或用左右方向键切换";
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
    internal bool Gaming { set { gaming=value; Invalidate(); } }
    internal void SetTheme(bool lightTheme) { light=lightTheme; Invalidate(); }
    // Zones follow the real taskbar order: right-most is the clock's right side.
    // Artwork sits in the gaps so a drawn element never covers a click target.
    RectangleF[] Zones() {
        return new RectangleF[]{
            new RectangleF(122,0,114,BarHeight),
            new RectangleF(244,0,114,BarHeight),
            new RectangleF(366,0,114,BarHeight),
            new RectangleF(0,0,114,BarHeight)
        };
    }
    static Color Mix(Color background,Color top,int alpha) { return Ui.Overlay(background,255,Ui.WithAlpha(top,alpha)); }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.ScaleTransform(PaintScale(),PaintScale());
        Color border=Ui.SettingsBorder(light),surface=Ui.SettingsSurface(light),text=Ui.SettingsText(light),muted=Ui.SettingsSubtext(light),accent=Ui.SettingsAccent(light);
        string[] names={"托盘左侧","时钟左侧","时钟右侧","任务栏左端"};
        var zones=Zones();
        for(int i=0;i<4;i++) {
            bool selected=i==position;
            var slot=zones[i]; slot.Inflate(-1,-1);
            Color fill=selected ? Mix(surface,accent,light?18:32) : hover==i ? Ui.SettingsHover(light) : surface;
            using(var path=RoundPath(slot,8)) {
                using(var b=new SolidBrush(fill)) g.FillPath(b,path);
                using(var pen=new Pen(selected?accent:border,selected?1.5f:1f)) g.DrawPath(pen,path);
            }
            using(var font=new Font("Microsoft YaHei UI",13f,selected?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(selected?Ui.SettingsAccentInk(light):text))
            using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center })
                g.DrawString(names[i],font,brush,slot,format);
            if(selected && Focused) using(var pen=new Pen(accent,1f)) {
                pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dot;
                var focus=slot; focus.Inflate(-4,-4);
                using(var path=RoundPath(focus,5)) g.DrawPath(pen,path);
            }
        }
        using(var font=new Font("Microsoft YaHei UI",11.5f,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var brush=new SolidBrush(muted))
            g.DrawString("选择后立即生效  ·  支持 ← → 键切换",font,brush,new PointF(0,56));
    }
    int ZoneAt(Point location) { float k=PaintScale(); if(k>0) location=new Point((int)(location.X/k),(int)(location.Y/k)); var zones=Zones(); for(int i=0;i<4;i++) if(zones[i].Contains(location)) return i; return -1; }
    void Apply(int value) {
        bool changed=value!=position;
        position=value; Focus(); Invalidate();
        if(changed && PositionChanged!=null) PositionChanged();
    }
    protected override void OnMouseMove(MouseEventArgs e) { int z=ZoneAt(e.Location); if(z!=hover) { hover=z; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { if(hover!=-1) { hover=-1; Invalidate(); } base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if(ZoneAt(e.Location)>=0) { pressed=true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if(pressed) { pressed=false; Invalidate(); } base.OnMouseUp(e); }
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
    protected override void OnLostFocus(EventArgs e) { hover=-1; pressed=false; Invalidate(); base.OnLostFocus(e); }
}
// Shared rounded surface for cards and the degraded banner: soft fill with a
// 1 px mist-blue stroke, matching the taskbar pill geometry.
internal sealed class BatteryTrack : Panel {
    public BatteryTrack() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
}
internal sealed class BatteryReadout : Label {
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float y=(Height-Font.GetHeight(g))/2;
        using(var brush=new SolidBrush(ForeColor)) {
            g.DrawString(Text,Font,brush,new PointF(0,y),StringFormat.GenericTypographic);
            float scale=Font.SizeInPoints/48f;
            if(Text!="--") using(var suffix=new Font(Font.FontFamily,18f*scale,FontStyle.Regular)) {
                float width=g.MeasureString(Text,Font,PointF.Empty,StringFormat.GenericTypographic).Width;
                g.DrawString("%",suffix,brush,new PointF(width+5*scale,y+Font.GetHeight(g)-suffix.GetHeight(g)-8*scale),StringFormat.GenericTypographic);
            }
        }
    }
}
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
// carries the full rest/hover/pressed/disabled/focus state machine.
internal sealed class ToggleSwitch : CheckBox {
    const int TrackW=40,TrackH=20,KnobSize=14;
    bool light,hover,pressed;
    public ToggleSwitch(bool lightTheme) {
        light=lightTheme;
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
        Size=new Size(232,26);
    }
    internal void SetTheme(bool lightTheme) { light=lightTheme; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;
        Color parent=Parent!=null?Parent.BackColor:SystemColors.Control;
        g.Clear(parent);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color accent=Ui.SettingsAccent(light),onKnob=(light?Ui.SettingsSurface(light):Ui.SettingsBase(light)),border=Ui.SettingsBorder(light),text=Ui.SettingsText(light),muted=Ui.SettingsSubtext(light);
        int textW=0;
        if(!string.IsNullOrEmpty(Text)) {
            textW=TextRenderer.MeasureText(Text,Font).Width;
            TextRenderer.DrawText(g,Text,Font,new Rectangle(0,0,textW+2,Height),Enabled?text:Ui.WithAlpha(text,110),TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        }
        int trackX=textW>0?Math.Min(textW+12,Width-TrackW):0;
        float trackY=(Height-TrackH)/2f;
        Color fill,stroke,knob;
        if(!Enabled) {
            fill=Ui.Overlay(parent,255,Ui.WithAlpha(border,45));
            stroke=Ui.WithAlpha(border,45);
            knob=Ui.WithAlpha(muted,45);
        } else if(Checked) {
            fill=pressed?Ui.Overlay(Ui.SettingsAccent(light),255,Ui.WithAlpha(text,20)):(hover?Ui.SettingsAccent(light):accent);
            stroke=fill; knob=onKnob;
        } else {
            fill=Ui.Overlay(parent,255,Ui.WithAlpha(border,pressed?205:150));
            stroke=hover?Ui.WithAlpha(muted,210):border;
            knob=hover?text:muted;
        }
        var track=new RectangleF(trackX,trackY,TrackW,TrackH);
        using(var path=PositionPicker.RoundPath(track,10)) {
            using(var b=new SolidBrush(fill)) g.FillPath(b,path);
            using(var p=new Pen(stroke)) g.DrawPath(p,path);
        }
        int knobSize=pressed?12:KnobSize;
        float knobX=Checked?trackX+TrackW-3-knobSize:trackX+3;
        float knobY=trackY+(TrackH-knobSize)/2f;
        using(var b=new SolidBrush(knob)) g.FillEllipse(b,knobX,knobY,knobSize,knobSize);
        if(Focused) using(var p=new Pen(Ui.WithAlpha(accent,235),1.5f))
            using(var path=PositionPicker.RoundPath(new RectangleF(trackX-3,trackY-3,TrackW+6,TrackH+6),13)) g.DrawPath(p,path);
    }
    protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover=false; pressed=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
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
    string rawInputError="";
    readonly EventWaitHandle show;
    readonly EventWaitHandle quit=new EventWaitHandle(false,EventResetMode.AutoReset,Program.ExitEventName);
    readonly EventWaitHandle toggleStartup=new EventWaitHandle(false,EventResetMode.AutoReset,Program.ToggleEventName);
    readonly string statusPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","status.txt");
    Form manager; Label managerDevice, managerConnection, managerPercent, managerCharge, managerCompanion; CheckBox managerStartup; PositionPicker positionPicker;
    Panel managerTrack;
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
    [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputData(IntPtr hRawInput,uint command,byte[] data,ref uint size,uint headerSize);
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
        if(!rawInputOk) rawInputError="RegisterRawInputDevices: "+Marshal.GetLastWin32Error();
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
            // RID_INPUT needs header + RAWMOUSE. RIDI_DEVICENAME below counts
            // UTF-16 characters; that separate allocation caused the old heap bug.
            uint headerSize=(uint)Marshal.SizeOf(typeof(RawInputHeader));
            var data=new byte[256]; uint got=(uint)data.Length;
            if(GetRawInputData(m.LParam,0x10000005,data,ref got,headerSize)==unchecked((uint)-1)) { rawInputError="GetRawInputData: "+Marshal.GetLastWin32Error(); return; }
            if(got<headerSize) { rawInputError="GetRawInputData: incomplete header"; return; }
            IntPtr hDevice=(IntPtr)Marshal.ReadInt64(data,8);
            if(hDevice==IntPtr.Zero) return;
            string path;
            if(!rawNames.TryGetValue(hDevice,out path)) {
                uint size=0;
                if(GetRawInputDeviceInfoW(hDevice,0x20000007,IntPtr.Zero,ref size)==unchecked((uint)-1)) { rawInputError="Raw device name size: "+Marshal.GetLastWin32Error(); return; }
                if(size<2) { rawInputError="Raw device name is empty"; return; }
                // RIDI_DEVICENAME counts UTF-16 characters; AllocHGlobal counts bytes.
                var buf=Marshal.AllocHGlobal(checked(((int)size+16)*sizeof(char)));
                try {
                    uint cap=size+16;
                    if(GetRawInputDeviceInfoW(hDevice,0x20000007,buf,ref cap)==unchecked((uint)-1)) { rawInputError="Raw device name: "+Marshal.GetLastWin32Error(); return; }
                    if(cap>size+16) { rawInputError="Raw device name exceeds capacity"; return; }
                    path=Marshal.PtrToStringUni(buf);
                } finally { Marshal.FreeHGlobal(buf); }
                if(path==null) { rawInputError="Raw device name is null"; return; }
                rawNames[hDevice]=path;
            }
            string key=state.KeyForRawPath(path.ToLowerInvariant());
            if(key!=null) state.SetActive(key);
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
        string diagnostic="TaskbarDataVisible="+visible+"\nPosition="+position+"\n"+status+"\nStartup="+startupEnabled+"\nReadError="+failure+"\nActiveKey="+(state.ActiveKey??"(none)")+"\nRawInput="+(rawInputOk?"on":"off")+"\nRawInputError="+rawInputError+"\nDevices="+state.Describe(now)+"\nPollInterval="+pollInterval+"\nNative="+(native!=null ? native.Status : "降级托盘（"+degradedReason+"）");
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
        string tip=state.TrayTooltip(uptime.ElapsedMilliseconds);
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
        int previousLevel=managerLevel;
        Color normalText=Ui.SettingsText(managerLight);
        Color amberText=MouseBattery.Ui.WarnInk(managerLight);
        Color redText=MouseBattery.Ui.CritInk(managerLight);

        var shown=visible ? state.Displayed(uptime.ElapsedMilliseconds) : null;
        if(shown!=null) {
            managerDevice.Text=shown.Name;
            managerPercent.Text=shown.Percent+"";
            managerPercent.ForeColor=shown.Percent<=10 ? redText : shown.Percent<=20 ? amberText : normalText;
            managerCharge.Text=shown.Gaming ? shown.ChargeText : "AA 电池供电";
            managerConnection.Text=state.DisplayedFailed(uptime.ElapsedMilliseconds) ? "正在重连 · 上次读数" : shown.Connection; // F8: product line lives in the chip, not the connection string
            managerLevel=shown.Percent;
        } else {
            managerDevice.Text="鼠标设备未连接";
            managerPercent.Text="--";
            managerPercent.ForeColor=normalText;
            managerCharge.Text="暂无电量";
            managerConnection.Text=string.IsNullOrEmpty(failure) ? "等待设备连接" : failure;
            managerLevel=-1;
        }
        managerPercent.AccessibleName=shown!=null ? shown.Percent+"%" : "暂无电量";
        // Companion device line (design doc §3.1 priority 5): tooltip line 2.
        string tip=state.Tooltip(uptime.ElapsedMilliseconds);
        int nl=tip.IndexOf('\n');
        bool hasCompanion=visible && nl>=0 && nl+1<tip.Length;
        managerCompanion.Visible=hasCompanion;
        if(hasCompanion) managerCompanion.Text=tip.Substring(nl+1);
        if(managerLevel!=previousLevel) managerTrack.Invalidate();
        if(positionPicker!=null && !positionPicker.IsDisposed) { positionPicker.Gaming=shown!=null&&shown.Gaming; positionPicker.Percent=managerLevel; positionPicker.Invalidate(); }
    }
    private void ShowManager() {
        if(manager==null || manager.IsDisposed) {
            bool light=AppsUseLightTheme();
            managerLight=light;
            Color background=MouseBattery.Ui.SettingsBase(light);
            Color surface=MouseBattery.Ui.SettingsSurface(light);
            Color foreground=MouseBattery.Ui.SettingsText(light);
            Color muted=MouseBattery.Ui.SettingsSubtext(light);
            Color accent=MouseBattery.Ui.SettingsAccent(light);
            Color border=MouseBattery.Ui.SettingsBorder(light);
            manager=new Form { Text="鼠标电量", AutoScaleMode=AutoScaleMode.None, Font=new Font("Microsoft YaHei UI",9f), ClientSize=new Size(MouseBattery.Ui.WidthWindow,degraded ? MouseBattery.Ui.HeightWindowDegraded : MouseBattery.Ui.HeightWindowNormal), BackColor=background,ForeColor=foreground, FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, StartPosition=FormStartPosition.CenterScreen };
            var controls=new List<Control>();
            var heading=new Label { Text="鼠标电量",Font=new Font("Microsoft YaHei UI",18f,FontStyle.Bold),Location=new Point(24,20),AutoSize=true };
            var subtitle=new Label { Text="设备状态与任务栏设置",Font=new Font("Microsoft YaHei UI",9.75f),ForeColor=muted,Location=new Point(25,54),AutoSize=true };
            controls.Add(heading); controls.Add(subtitle);
            int bodyY=92;
            if(degraded) {
                Color bannerBg=MouseBattery.Ui.Overlay(background,255,MouseBattery.Ui.WithAlpha(MouseBattery.Ui.Yellow(light),light?22:26));
                Color bannerLine=MouseBattery.Ui.WithAlpha(MouseBattery.Ui.Yellow(light),105);
                Color bannerText=MouseBattery.Ui.WarnInk(light);
                var banner=new RoundedPanel(bannerBg,bannerLine) { Location=new Point(20,bodyY),Size=new Size(MouseBattery.Ui.WidthCardWindow,58) };
                var rail=new Panel { Location=new Point(0,0),Size=new Size(3,58),BackColor=MouseBattery.Ui.WithAlpha(MouseBattery.Ui.Yellow(light),210) };
                banner.Controls.Add(rail);
                var bannerLabel=new Label { Text="当前使用托盘显示电量\n此 Windows 任务栏版本尚未适配。",Location=new Point(20,7),Size=new Size(470,46),Font=new Font("Microsoft YaHei UI",9.75f),ForeColor=bannerText ,BackColor=Color.Transparent};
                banner.Controls.Add(bannerLabel);
                controls.Add(banner);
                bodyY+=70;
            }
            var statusCard=new RoundedPanel(surface,border) { Location=new Point(20,bodyY),Size=new Size(MouseBattery.Ui.WidthCardWindow,184) };
            managerDevice=new Label { Location=new Point(24,20),Size=new Size(472,26),Font=new Font("Segoe UI",12f,FontStyle.Bold),AutoEllipsis=true,BackColor=Color.Transparent };
            managerPercent=new BatteryReadout { Location=new Point(24,48),Size=new Size(240,86),Font=new Font("Segoe UI Variable Display",48f,FontStyle.Bold),AccessibleName="鼠标电量",BackColor=Color.Transparent };
            managerCharge=new Label { Location=new Point(284,74),Size=new Size(212,23),Font=new Font("Microsoft YaHei UI",10.5f),TextAlign=ContentAlignment.MiddleRight,BackColor=Color.Transparent };
            managerConnection=new Label { Location=new Point(284,101),Size=new Size(212,22),Font=new Font("Microsoft YaHei UI",9.75f),ForeColor=muted,TextAlign=ContentAlignment.MiddleRight,AutoEllipsis=true,BackColor=Color.Transparent };
            managerTrack=new BatteryTrack { Location=new Point(24,143),Size=new Size(472,8),BackColor=surface,AccessibleName="当前电量刻度" };
            managerCompanion=new Label { Location=new Point(24,159),Size=new Size(472,20),Font=new Font("Microsoft YaHei UI",9f),ForeColor=muted,Visible=false,AutoEllipsis=true,BackColor=Color.Transparent };
            managerTrack.Paint+=delegate(object sender,PaintEventArgs e) {
                int level=Math.Max(0,managerLevel);
                Color fill=managerLevel<0 ? muted : level<=10 ? MouseBattery.Ui.Red(light) : level<=20 ? MouseBattery.Ui.Yellow(light) : accent;
                float segment=(managerTrack.Width-19*4)/20f;
                for(int i=0;i<20;i++) {
                    var bounds=new RectangleF(i*(segment+4),0,segment,managerTrack.Height);
                    using(var path=PositionPicker.RoundPath(bounds,2)) {
                        using(var brush=new SolidBrush(border)) e.Graphics.FillPath(brush,path);
                        float amount=Math.Max(0,Math.Min(1,level/5f-i));
                        if(amount>0) {
                            var saved=e.Graphics.Save();
                            e.Graphics.SetClip(new RectangleF(bounds.X,0,bounds.Width*amount,bounds.Height));
                            using(var brush=new SolidBrush(fill)) e.Graphics.FillPath(brush,path);
                            e.Graphics.Restore(saved);
                        }
                    }
                }
            };
            statusCard.Controls.AddRange(new Control[]{managerDevice,managerPercent,managerCharge,managerConnection,managerTrack,managerCompanion});
            controls.Add(statusCard);
            bodyY+=184+16;
            int rowY;
            if(!degraded) {
                var cardB=new RoundedPanel(surface,border) { Location=new Point(20,bodyY),Size=new Size(MouseBattery.Ui.WidthCardWindow,156) };
                var pickerLabel=new Label { Text="任务栏位置",Font=new Font("Microsoft YaHei UI",10.5f,FontStyle.Bold),Location=new Point(20,16),AutoSize=true,BackColor=Color.Transparent };
                var pickerValue=new Label { Text=PositionPicker.PositionNames[position],Location=new Point(20,42),Size=new Size(480,22),Font=new Font("Microsoft YaHei UI",9.75f),ForeColor=muted,BackColor=Color.Transparent };
                cardB.Controls.Add(pickerLabel); cardB.Controls.Add(pickerValue);
                positionPicker=new PositionPicker { Location=new Point(20,74),Size=new Size(480,76) };
                positionPicker.SetTheme(light);
                positionPicker.Position=position;
                positionPicker.PositionChanged+=delegate {
                    int selected=positionPicker.Position;
                    try { using(var key=Registry.CurrentUser.CreateSubKey(@"Software\MouseBatteryTray")) key.SetValue("TaskbarPosition",selected,RegistryValueKind.DWord); position=selected; }
                    catch(UnauthorizedAccessException e) { MessageBox.Show("无法保存位置："+e.Message,"鼠标电量"); }
                    catch(SecurityException e) { MessageBox.Show("无法保存位置："+e.Message,"鼠标电量"); }
                    pickerValue.Text=PositionPicker.PositionNames[selected];
                };
                cardB.Controls.Add(positionPicker);
                controls.Add(cardB);
                rowY=bodyY+156+20;
            } else rowY=bodyY+10;
            managerStartup=new ToggleSwitch(light) { Text="开机自动启动",AccessibleName="登录 Windows 后自动启动",AccessibleDescription="控制鼠标电量工具是否在登录后后台运行",Location=new Point(24,rowY),Font=new Font("Microsoft YaHei UI",10f),Size=new Size(248,28),Checked=Startup.Enabled };
            managerStartup.CheckedChanged+=delegate { if(!updatingSettings) SetStartup(managerStartup.Checked); };
            controls.Add(managerStartup);
            var hairline=new Panel { Location=new Point(20,rowY+42),Size=new Size(MouseBattery.Ui.WidthCardWindow,1),BackColor=MouseBattery.Ui.WithAlpha(border,115) };
            controls.Add(hairline);
            var note=new Label { Text=degraded ? "关闭窗口后继续在托盘显示" : "关闭窗口后继续在任务栏显示",Font=new Font("Microsoft YaHei UI",9f),ForeColor=muted,Location=new Point(24,rowY+56),AutoSize=true };
            int buttonY=rowY+82;
            var exit=new Button { Text="退出工具",Location=new Point(MouseBattery.Ui.WidthWindow-20-96-12-96,buttonY),Size=new Size(96,36),FlatStyle=FlatStyle.Flat,BackColor=background,ForeColor=muted };
            exit.FlatAppearance.BorderSize=0; exit.FlatAppearance.MouseOverBackColor=MouseBattery.Ui.SettingsHover(light); exit.FlatAppearance.MouseDownBackColor=MouseBattery.Ui.SettingsActionHover(light); exit.Click+=delegate { ExitThread(); };
            var close=new Button { Text="完成",AccessibleName="完成设置并关闭窗口",Location=new Point(MouseBattery.Ui.WidthWindow-20-96,buttonY),Size=new Size(96,36),FlatStyle=FlatStyle.Flat,BackColor=Ui.SettingsAction(light),ForeColor=Ui.SettingsAccentInk(light) };
            close.FlatAppearance.BorderSize=0; close.FlatAppearance.MouseOverBackColor=Ui.SettingsActionHover(light); close.FlatAppearance.MouseDownBackColor=Ui.SettingsActionHover(light); close.Click+=delegate { manager.Close(); };
            // Enter/Esc close the window only; exiting the resident tool stays an explicit click.
            manager.AcceptButton=close; manager.CancelButton=close;
            controls.Add(note); controls.Add(exit); controls.Add(close);
            manager.Controls.AddRange(controls.ToArray());
            // This programmatic layout is measured in 96-DPI units. Scale its
            // bounds once; the font already uses the current display DPI.
            using(var graphics=manager.CreateGraphics()) {
                float uiScale=graphics.DpiX/96f;
                manager.Scale(new SizeF(uiScale,uiScale));
                using(var path=PositionPicker.RoundPath(new RectangleF(0,0,close.Width,close.Height),8*uiScale)) close.Region=new Region(path);
                using(var path=PositionPicker.RoundPath(new RectangleF(0,0,exit.Width,exit.Height),8*uiScale)) exit.Region=new Region(path);
            }
            manager.AutoScroll=true;
            int availableHeight=Screen.FromControl(manager).WorkingArea.Height-32;
            if(manager.Height>availableHeight) manager.Height=availableHeight;
            manager.ActiveControl=close;
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
