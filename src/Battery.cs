using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MouseBattery {
internal sealed class BatteryReading {
    public string Name, UnitId, Connection;
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
internal sealed class BatterySource {
    HidDevice cached;
    byte feature, address;
    string name, unit;
    internal string LastError="尚未读取";
    internal Action<string> Trace;
    private BatteryReading Read(HidDevice d, bool discover) {
        using(var h=new HidConnection(d)) {
            h.Trace=Trace;
            byte addr=(byte)(d.Pid==0xC0A0 ? 255 : 1);
            if(discover) {
                byte fw=h.Feature(addr,3);
                if(fw==0) throw new IOException("Device identity feature unavailable");
                byte[] identity=h.Query(addr,fw,0);
                if(identity.Length<13 || identity[7]!=0x40 || identity[8]!=0xB8 || identity[9]!=0xC0 || identity[10]!=0xA0) throw new IOException("Receiver is not paired to PRO X 2 DEX");
                string id=BitConverter.ToString(identity,1,4);
                if(unit!=null && unit!=id) throw new IOException("A different mouse is connected");
                byte nf=h.Feature(addr,5);
                if(nf==0) throw new IOException("Device name feature unavailable");
                int length=h.Query(addr,nf,0)[0];
                if(length==0 || length>100) throw new IOException("Invalid device name length");
                var bytes=new List<byte>();
                while(bytes.Count<length) {
                    byte[] part=h.Query(addr,nf,0x10,(byte)bytes.Count);
                    int n=Math.Min(part.Length,length-bytes.Count);
                    for(int i=0;i<n;i++) bytes.Add(part[i]);
                }
                string deviceName=Encoding.UTF8.GetString(bytes.ToArray());
                byte bf=h.Feature(addr,0x1004);
                if(bf==0) throw new IOException("Unified battery feature unavailable");
                byte[] caps=h.Query(addr,bf,0);
                if(caps.Length<2 || (caps[1]&2)==0) throw new IOException("Device does not report battery percentage");
                name=deviceName; unit=id; feature=bf; address=addr; cached=d;
            }
            return BatteryReading.Parse(h.Query(address,feature,0x10),name,unit,d.Pid==0xC0A0 ? "有线" : "2.4G");
        }
    }
    internal BatteryReading Poll() {
        var errors=new List<string>(); string failedPath=null;
        if(cached!=null) {
            try { var r=Read(cached,false); LastError=null; return r; }
            catch(TimeoutException e) { errors.Add(e.Message); }
            catch(IOException e) { errors.Add(e.Message); }
            catch(Win32Exception e) { errors.Add(e.Message); }
            failedPath=cached.Path; cached=null;
        }
        var rejected=new List<string>(); List<HidDevice> devices;
        try { devices=Native.Enumerate(rejected); }
        catch(Win32Exception e) { LastError=e.Message; return null; }
        catch(IOException e) { LastError=e.Message; return null; }
        devices.Sort(delegate(HidDevice a,HidDevice b) { return (a.Pid==0xC0A0 ? 0 : 1).CompareTo(b.Pid==0xC0A0 ? 0 : 1); });
        foreach(var d in devices) {
            if((d.Pid!=0xC0A0 && d.Pid!=0xC54D) || d.Path==failedPath) continue;
            try { var r=Read(d,true); LastError=null; return r; }
            catch(TimeoutException e) { errors.Add(d.Pid.ToString("X4")+": "+e.Message); }
            catch(IOException e) { errors.Add(d.Pid.ToString("X4")+": "+e.Message); }
            catch(Win32Exception e) { errors.Add(d.Pid.ToString("X4")+": "+e.Message); }
            cached=null;
        }
        errors.AddRange(rejected);
        LastError=errors.Count==0 ? "未检测到目标鼠标，请检查接收器或连接线" : string.Join("; ",errors);
        return null;
    }
}
internal sealed class DisplayState {
    internal BatteryReading Last;
    internal long LastSuccessMs;
    internal bool Failed;
    internal void Apply(BatteryReading reading,long nowMs) {
        Failed=reading==null;
        if(reading!=null) { Last=reading; LastSuccessMs=nowMs; }
    }
    internal bool Visible(long nowMs) { return Last!=null && nowMs-LastSuccessMs<15000; }
    internal string Tooltip(long nowMs) {
        if(!Visible(nowMs)) return "鼠标电量不可用";
        if(Failed) return Last.Name+"\n上次电量："+Last.Percent+"%\n正在重连（旧读数）";
        return Last.Name+"\n电量："+Last.Percent+"% · "+Last.ChargeText+"\n已连接 · "+Last.Connection;
    }
}
}
