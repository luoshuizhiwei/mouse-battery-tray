using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MouseBattery {
internal sealed class BatteryReading {
    public string Name, UnitId, Connection, Path;
    public int Percent, Charging;
    internal string ChargeText {
        get {
            switch(Charging) {
                case 0: return "未充电";
                case 1: return "正在充电";
                case 2: return "慢速充电";
                case 3: return "已充满";
                case 4: return "充电异常";
                default: return "未知充电状态 ("+Charging+")";
            }
        }
    }
    internal static BatteryReading Parse(byte[] data, string name, string unit, string connection) {
        if(data.Length<4 || data[0]>100) throw new IOException("Invalid battery response: "+BitConverter.ToString(data));
        return new BatteryReading { Name=name, UnitId=unit, Connection=connection, Percent=data[0], Charging=data[2] };
    }
}

// Reads every supported mouse that is currently reachable. Each interface path
// carries its own cache and failure backoff, so one sleeping mouse can never
// hold down the polling of another; a mouse that vanished (BLE sleep drops the
// HID node entirely) simply stops being a candidate until it reconnects.
internal sealed class BatterySource {
    sealed class Entry {
        public HidDevice Device;
        public byte Address, Feature;
        public string Name, Unit;
        public int Failures;
        public long NextTryMs;
        public bool WasPresent;
    }
    readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
    internal Action<string> Trace;
    internal string LastError="尚未读取";
    internal readonly List<string> FailedPaths=new List<string>();

    static byte AddressFor(HidDevice d) { return d.Pid==0xC54D||d.Pid==0xC548 ? (byte)1 : (byte)255; }
    internal static string ConnectionFor(HidDevice d) { return d.Pid==0xC0A0 ? "有线" : d.Pid==0xB02C ? "蓝牙" : "2.4G"; }
    // PRO X 2 DEX carries its pid at identity[9..10]; the M750 puts it at [7..8].
    static bool SupportedIdentity(byte[] identity) {
        if(identity.Length<13) return false;
        return (identity[7]==0x40 && identity[8]==0xB8 && identity[9]==0xC0 && identity[10]==0xA0)
            || (identity[7]==0xB0 && identity[8]==0x2C);
    }

    private BatteryReading Read(Entry e, bool discover) {
        using(var h=new HidConnection(e.Device)) {
            h.Trace=Trace;
            if(discover) {
                byte fw=h.Feature(e.Address,3);
                if(fw==0) throw new IOException("Device identity feature unavailable");
                byte[] identity=h.Query(e.Address,fw,0);
                if(!SupportedIdentity(identity)) throw new IOException("Unsupported paired device: "+BitConverter.ToString(identity,7,4));
                byte nf=h.Feature(e.Address,5);
                if(nf==0) throw new IOException("Device name feature unavailable");
                int length=h.Query(e.Address,nf,0)[0];
                if(length==0 || length>100) throw new IOException("Invalid device name length");
                var bytes=new List<byte>();
                while(bytes.Count<length) {
                    byte[] part=h.Query(e.Address,nf,0x10,(byte)bytes.Count);
                    int n=Math.Min(part.Length,length-bytes.Count);
                    for(int i=0;i<n;i++) bytes.Add(part[i]);
                }
                e.Name=Encoding.UTF8.GetString(bytes.ToArray());
                e.Unit=BitConverter.ToString(identity,1,4);
                byte bf=h.Feature(e.Address,0x1004);
                if(bf==0) throw new IOException("Unified battery feature unavailable");
                byte[] caps=h.Query(e.Address,bf,0);
                if(caps.Length<2 || (caps[1]&2)==0) throw new IOException("Device does not report battery percentage");
                e.Feature=bf;
            }
            return BatteryReading.Parse(h.Query(e.Address,e.Feature,0x10),e.Name,e.Unit,ConnectionFor(e.Device));
        }
    }

    internal List<BatteryReading> Poll(long nowMs) {
        var results=new List<BatteryReading>();
        FailedPaths.Clear();
        List<HidDevice> devices;
        try { devices=Native.Enumerate(null); }
        catch(Win32Exception e) { LastError=e.Message; return results; }
        catch(IOException e) { LastError=e.Message; return results; }
        var candidates=new List<HidDevice>();
        foreach(var d in devices) if(d.Pid==0xC54D || d.Pid==0xC0A0 || d.Pid==0xB02C || d.Pid==0xC548) candidates.Add(d);
        candidates.Sort(delegate(HidDevice a,HidDevice b){ return (a.Pid==0xC0A0 ? 0 : 1).CompareTo(b.Pid==0xC0A0 ? 0 : 1); });
        var present=new HashSet<string>();
        foreach(var d in candidates) present.Add(d.Path);
        foreach(var kv in entries) if(!present.Contains(kv.Key)) kv.Value.WasPresent=false; // BLE sleep removed it
        var errors=new List<string>();
        foreach(var d in candidates) {
            Entry e;
            if(!entries.TryGetValue(d.Path,out e)) { e=new Entry { Device=d, Address=AddressFor(d) }; entries[d.Path]=e; }
            else e.Device=d;
            if(!e.WasPresent) { e.Failures=0; e.NextTryMs=0; e.WasPresent=true; } // fresh (re)appearance retries at once
            if(nowMs<e.NextTryMs) continue;
            bool discover=e.Unit==null;
            try {
                var r=Read(e,discover);
                r.Path=d.Path;
                results.Add(r);
                e.Failures=0; e.NextTryMs=0;
            } catch(TimeoutException te) { NoteFailure(e,te.Message,nowMs,errors); }
            catch(IOException ie) { NoteFailure(e,ie.Message,nowMs,errors); }
            catch(Win32Exception we) { NoteFailure(e,we.Message,nowMs,errors); }
        }
        if(!anyCandidate(candidates)) LastError="未检测到受支持的鼠标，请检查接收器或蓝牙连接";
        else if(results.Count>0) LastError=null;
        else LastError=errors.Count==0 ? "未检测到受支持的鼠标" : string.Join("; ",errors.ToArray());
        return results;
    }
    static bool anyCandidate(List<HidDevice> candidates) { return candidates.Count>0; }
    void NoteFailure(Entry e,string message,long nowMs,List<string> errors) {
        e.Failures++;
        e.NextTryMs=nowMs+Math.Min(60000,10000*(1<<Math.Min(3,e.Failures-1)));
        FailedPaths.Add(e.Device.Path);
        errors.Add((e.Name ?? e.Device.Pid.ToString("X4"))+": "+message);
    }
}

// Multi-device display model. Readings are keyed by unit serial so they survive
// re-enumeration; the pill follows the raw-input active device when its reading
// is fresh and falls back to the freshest device otherwise.
internal sealed class DisplayState {
    sealed class View { public BatteryReading Last; public long LastSuccessMs; public bool Failed; public string Token; }
    readonly Dictionary<string,View> devices=new Dictionary<string,View>();
    internal string ActiveKey;
    internal void Apply(BatteryReading[] readings,long nowMs) {
        if(readings==null) return;
        foreach(var r in readings) {
            if(r==null || r.UnitId==null) continue;
            View v; if(!devices.TryGetValue(r.UnitId,out v)) { v=new View{Token=TokenOf(r.Path)}; devices[r.UnitId]=v; }
            v.Last=r; v.LastSuccessMs=nowMs; v.Failed=false; v.Token=TokenOf(r.Path);
        }
    }
    internal void MarkFailed(string[] paths,long nowMs) {
        if(paths==null) return;
        foreach(var p in paths) {
            string token=TokenOf(p);
            foreach(var v in devices.Values) if(v.Token==token) { v.Failed=true; break; }
        }
    }
    internal void SetActive(string unitKey) { if(unitKey!=null) ActiveKey=unitKey; }
    // Raw input names and device paths share the pid token even though their
    // instance formats differ (vid_046d vs the GATT-prefixed vid&02046d form).
    internal string KeyForRawPath(string rawPath) {
        string token=TokenOf(rawPath);
        if(token==null) return null;
        foreach(var v in devices.Values) if(v.Token==token && v.Last!=null) return v.Last.UnitId;
        return null;
    }
    static string TokenOf(string path) {
        if(path==null) return null;
        path=path.ToLowerInvariant();
        foreach(string marker in new string[]{"pid_c54d","pid_c548","pid_c0a0","pid&b02c"}) {
            int i=path.IndexOf(marker,StringComparison.Ordinal);
            if(i>=0) return marker.Substring(4);
        }
        int j=path.IndexOf("pid&",StringComparison.Ordinal);
        if(j>=0) return path.Substring(Math.Min(j+4,path.Length),Math.Min(4,Math.Max(0,path.Length-(j+4))));
        j=path.IndexOf("pid_",StringComparison.Ordinal);
        if(j>=0) return path.Substring(Math.Min(j+4,path.Length),Math.Min(4,Math.Max(0,path.Length-(j+4))));
        return path;
    }
    View DisplayedView(long nowMs) {
        View v;
        if(ActiveKey!=null && devices.TryGetValue(ActiveKey,out v) && nowMs-v.LastSuccessMs<15000) return v;
        View best=null;
        foreach(var kv in devices) {
            var x=kv.Value;
            if(x.Last!=null && nowMs-x.LastSuccessMs<15000 && (best==null || x.LastSuccessMs>=best.LastSuccessMs)) best=x;
        }
        return best;
    }
    internal BatteryReading Displayed(long nowMs) { var v=DisplayedView(nowMs); return v!=null ? v.Last : null; }
    internal bool DisplayedFailed(long nowMs) { var v=DisplayedView(nowMs); return v!=null && v.Failed; }
    internal bool Visible(long nowMs) { return Displayed(nowMs)!=null; }
    // Line 0 names the displayed device for the native card; line 1 carries the
    // idle companion so the hover card can list every mouse it knows.
    internal string Tooltip(long nowMs) {
        var v=DisplayedView(nowMs);
        if(v==null) return "鼠标电量不可用";
        string text=v.Last.Name;
        foreach(var kv in devices) {
            var x=kv.Value;
            if(x==v || x.Last==null || nowMs-x.LastSuccessMs>=900000) continue;
            text+="\n"+x.Last.Name+" · "+x.Last.Percent+"% · "+x.Last.Connection+((x.Failed || nowMs-x.LastSuccessMs>=15000) ? " · 旧读数" : "");
        }
        return text;
    }
    internal string Describe(long nowMs) {
        var lines=new List<string>();
        foreach(var kv in devices) {
            var x=kv.Value;
            if(x.Last==null) continue;
            lines.Add(x.Last.Name+" "+x.Last.Percent+"% "+x.Last.ChargeText+" "+x.Last.Connection+(x.Failed?" [失败]":"")+(kv.Key==ActiveKey?" [活动]":"")+" ("+((nowMs-x.LastSuccessMs)/1000)+"s前)");
        }
        return string.Join("\n",lines.ToArray());
    }
}
}
