using System;
using System.Collections.Generic;
using System.IO;
using MouseBattery;

// Scripted HID boundary; BatterySource itself is the production implementation.
namespace MouseBattery {
internal sealed class HidDevice { public string Path; public ushort Pid; }
internal static class Native {
    internal static HidDevice Device;
    internal static bool Present=true;
    internal static List<HidDevice> Enumerate(List<string> rejected) {
        return Present ? new List<HidDevice>{Device} : new List<HidDevice>();
    }
}
internal sealed class HidConnection : IDisposable {
    internal Action<string> Trace;
    internal static bool Awake=true, FailFeatureOnce, FailCapsOnce;
    internal static int IdentityAttempts, InvalidQueries;
    readonly HidDevice device;
    internal HidConnection(HidDevice d) { device=d; }
    internal byte Feature(byte address,ushort id) {
        if(id==3) { IdentityAttempts++; if(!Awake) throw new TimeoutException("sleep"); return 3; }
        if(id==5) return 5;
        if(id==0x1004) { if(FailFeatureOnce) {FailFeatureOnce=false;throw new TimeoutException("feature");} return 6; }
        throw new Exception("Unexpected feature");
    }
    internal byte[] Query(byte address,byte feature,byte function,params byte[] args) {
        if(feature==3 && function==0) {
            var data=new byte[13]; data[1]=1; data[2]=2; data[3]=3; data[4]=4;
            if(device.Pid==0xC548) {data[7]=0xB0;data[8]=0x2C;}
            else {data[7]=0x40;data[8]=0xB8;data[9]=0xC0;data[10]=0xA0;}
            return data;
        }
        if(feature==5 && function==0) return new byte[]{1};
        if(feature==5 && function==0x10) return new byte[]{(byte)'M'};
        if(feature==6 && function==0) {
            if(FailCapsOnce) {FailCapsOnce=false;throw new TimeoutException("caps");}
            return new byte[]{0,2};
        }
        if(feature==6 && function==0x10) return new byte[]{80,4,0,0};
        InvalidQueries++; throw new IOException("Undiscovered feature");
    }
    public void Dispose() { }
}
}
internal static class RecoveryTests {
    static int count,failures;
    static void Check(bool valid,string name) {
        count++; if(!valid) failures++; Console.WriteLine((valid?"PASS ":"FAIL ")+name);
    }
    static int Main() {
        Native.Device=new HidDevice{Pid=0xC54D,Path="hid#vid_046d&pid_c54d#receiver"};
        HidConnection.FailFeatureOnce=true;
        var source=new BatterySource();
        Check(source.Poll(0).Count==0,"feature timeout is reported");
        Check(source.Poll(20000).Count==1,"feature timeout recovers on the same source");
        HidConnection.FailCapsOnce=true; source=new BatterySource();
        Check(source.Poll(0).Count==0,"capabilities timeout is reported");
        Check(source.Poll(20000).Count==1,"capabilities timeout recovers on the same source");
        Check(HidConnection.InvalidQueries==0,"partial discovery never queries feature zero");
        Native.Device=new HidDevice{Pid=0xC548,Path="hid#vid_046d&pid_c548#receiver"};
        HidConnection.Awake=false; source=new BatterySource();
        Check(source.Poll(0).Count==0,"sleeping Bolt scan is empty");
        int attempts=HidConnection.IdentityAttempts;
        Check(source.Poll(1000).Count==0 && HidConnection.IdentityAttempts==attempts,"negative Bolt result has bounded retry cadence");
        HidConnection.Awake=true;
        Check(source.Poll(20000).Count==1,"Bolt wake retries its negative discovery");
        HidConnection.Awake=false; source=new BatterySource(); source.Poll(0);
        Native.Present=false; source.Poll(100);
        Native.Present=true; HidConnection.Awake=true;
        Check(source.Poll(200).Count==1,"same-path receiver reconnect invalidates negative cache");
        Console.WriteLine(count+" recovery checks, "+failures+" failures");
        return failures==0 ? 0 : 1;
    }
}
