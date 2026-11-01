using System;
using System.Collections.Generic;
using System.Text;
using MouseBattery;

// Feature census per paired device: enumerate the HID++ features that carry
// personality data (DPI, wheel, report rate, lighting, kind) so the差异化
// display design can rest on what each mouse actually exposes.
internal static class FeatureProbe {
    static string Name(HidConnection h,byte addr) {
        try {
            byte nf=h.Feature(addr,5);
            if(nf==0) return "?";
            int len=h.Query(addr,nf,0)[0];
            var bytes=new List<byte>();
            while(bytes.Count<len) { var p=h.Query(addr,nf,0x10,(byte)bytes.Count); int n=Math.Min(p.Length,len-bytes.Count); for(int i=0;i<n;i++) bytes.Add(p[i]); }
            return Encoding.UTF8.GetString(bytes.ToArray());
        } catch { return "?"; }
    }
    static void Try(HidConnection h,byte addr,ushort id,string label) {
        try {
            byte idx=h.Feature(addr,id);
            if(idx==0) { Console.WriteLine("  {0} (0x{1:X4}): absent",label,id); return; }
            Console.WriteLine("  {0} (0x{1:X4}): idx={2}",label,id,idx);
            switch(id) {
                case 0x0005: { // kind byte is in get-kind call: query 0x10 sub? Use caps instead
                    break; }
                case 0x1000: try { Console.WriteLine("    CAPS="+BitConverter.ToString(h.Query(addr,idx,0))); Console.WriteLine("    STATE="+BitConverter.ToString(h.Query(addr,idx,0x10))); } catch { }
                    break;
                case 0x1001: try { Console.WriteLine("    VOLT="+BitConverter.ToString(h.Query(addr,idx,0x10))); } catch { }
                    break;
                case 0x1004: try { Console.WriteLine("    CAPS="+BitConverter.ToString(h.Query(addr,idx,0))); Console.WriteLine("    STATE="+BitConverter.ToString(h.Query(addr,idx,0x10))); } catch { }
                    break;
                case 0x2206: try { Console.WriteLine("    DPI INFO="+BitConverter.ToString(h.Query(addr,idx,0))); } catch { } // MOUSE_POINTER
                    break;
                case 0x2205: try { Console.WriteLine("    POINTER="+BitConverter.ToString(h.Query(addr,idx,0))); } catch { }
                    break;
                case 0x2121: try { Console.WriteLine("    HIRES="+BitConverter.ToString(h.Query(addr,idx,0))); } catch { }
                    break;
                case 0x2130: try { Console.WriteLine("    WHEEL="+BitConverter.ToString(h.Query(addr,idx,0))); } catch { }
                    break;
                case 0x2120: try { Console.WriteLine("    VSCROLL="+BitConverter.ToString(h.Query(addr,idx,0))); } catch { }
                    break;
                case 0x0007: try { var r=h.Query(addr,idx,0); int len=r.Length>0?r[0]:0; var bytes=new List<byte>(); for(int o=0;o<len&&o<60;o+=14) { var p=h.Query(addr,idx,0x10,(byte)o); int n=Math.Min(p.Length,len-o); for(int i=0;i<n;i++) bytes.Add(p[i]); } Console.WriteLine("    FRIENDLY="+Encoding.UTF8.GetString(bytes.ToArray())); } catch { }
                    break;
                case 0x0021: try { Console.WriteLine("    RRATE="+BitConverter.ToString(h.Query(addr,idx,0))); } catch { }
                    break;
            }
        } catch(Exception e) { Console.WriteLine("  {0}: err {1}",label,e.Message); }
    }
    static int Main() {
        foreach(var d in Native.Enumerate(null)) {
            if(d.Pid!=0xC54D && d.Pid!=0xC548 && d.Pid!=0xB02C) continue;
            byte addr=d.Pid==0xC54D ? (byte)1 : (byte)255;
            // On Bolt receivers the paired mouse may sit at index 2; sweep.
            if(d.Pid==0xC548) {
                bool ok=false;
                for(byte a=1;a<=3 && !ok;a++) {
                    try { using(var h=new HidConnection(d)) { byte fw=h.Feature(a,3); if(fw!=0) { byte[] id=h.Query(a,fw,0); if(id.Length>=13 && id[7]==0xB0 && id[8]==0x2C) { Console.WriteLine("== M750L via Bolt idx {0} ==",a); Probe(h,a); ok=true; } } } } catch { }
                }
                continue;
            }
            try {
                using(var h=new HidConnection(d)) {
                    Console.WriteLine("== device PID={0:X4} via {1} ==",d.Pid,d.Pid==0xB02C?"BLE":"Lightspeed");
                    byte fw=h.Feature(addr,3);
                    byte[] identity=h.Query(addr,fw,0);
                    Console.WriteLine("  identity="+BitConverter.ToString(identity,7,4));
                    Probe(h,addr);
                }
            } catch(Exception e) { Console.WriteLine("  offline: "+e.Message); }
        }
        return 0;
    }
    static void Probe(HidConnection h,byte addr) {
        Console.WriteLine("  name="+Name(h,addr));
        foreach(var pair in new[]{new ushort[]{0x0005,0},new ushort[]{0x0007,0},new ushort[]{0x1000,0},new ushort[]{0x1001,0},new ushort[]{0x1004,0},new ushort[]{0x2205,0},new ushort[]{0x2206,0},new ushort[]{0x2120,0},new ushort[]{0x2121,0},new ushort[]{0x2130,0},new ushort[]{0x0021,0},new ushort[]{0x8070,0}}) {
            Try(h,addr,pair[0],pair[0]==0x2205?"MOUSE_POINTER":pair[0]==0x2206?"ADJUSTABLE_DPI":pair[0]==0x2120?"VERTICAL_SCROLL":pair[0]==0x2121?"HIRES_SCROLL":pair[0]==0x2130?"HIRES_WHEEL":pair[0]==0x0021?"REPORT_RATE":pair[0]==0x8070?"RGB_EFFECTS":pair[0]==0x0007?"FRIENDLY_NAME":pair[0]==0x1000?"BATTERY_STATUS":pair[0]==0x1001?"BATTERY_VOLTAGE":pair[0]==0x1004?"UNIFIED_BATTERY":"DEVICE_NAME");
        }
    }
}
