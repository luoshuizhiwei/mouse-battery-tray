using System;
using System.IO.MemoryMappedFiles;
using System.Text;
namespace MouseBattery {
static class SnapshotDump {
    static int Main() {
        try {
            using(var m=MemoryMappedFile.OpenExisting(@"Local\MouseBatteryTaskbar.v4",MemoryMappedFileRights.Read))
            using(var v=m.CreateViewAccessor(0,512,MemoryMappedFileAccess.Read)) {
                byte[] d=new byte[512];
                v.ReadArray(0,d,0,512);
                uint magic=BitConverter.ToUInt32(d,0);
                int seq=BitConverter.ToInt32(d,4);
                ulong tick=BitConverter.ToUInt64(d,8);
                int st=BitConverter.ToInt32(d,16),pct=BitConverter.ToInt32(d,20),chg=BitConverter.ToInt32(d,24),tr=BitConverter.ToInt32(d,28),pid=BitConverter.ToInt32(d,32),pos=BitConverter.ToInt32(d,36),startup=BitConverter.ToInt32(d,40),res=BitConverter.ToInt32(d,44),ovr=BitConverter.ToInt32(d,480);
                string tip=Encoding.Unicode.GetString(d,48,432).Split('\0')[0];
                Console.WriteLine("magic=0x"+magic.ToString("X")+" seq="+seq);
                Console.WriteLine("tick="+tick+" now="+((ulong)(Environment.TickCount&0x7FFFFFFF)));
                Console.WriteLine("state="+st+" percent="+pct+" charging="+chg+" transport="+tr+" pid="+pid+" pos="+pos+" startup="+startup+" reserved="+res+" override="+ovr);
                Console.WriteLine("tooltip="+tip.Substring(0,Math.Min(70,tip.Length)));
                Console.WriteLine("native checks: magic="+(magic==0x3254424D)+" percentRange="+(pct>=0&&pct<=100)+" stateRange="+(st>=0&&st<=2)+" posRange="+(pos>=0&&pos<=3));
                return 0;
            }
        } catch(Exception e) { Console.WriteLine("ERR "+e.Message); return 1; }
    }
}
}
