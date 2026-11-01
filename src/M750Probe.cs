using System;
using System.Text;
using System.Threading;
using MouseBattery;

// One-shot discovery for the Bolt-paired M750L: keep trying for 45 s while the
// user keeps the mouse awake, then dump identity, name and every plausible
// battery feature's index, caps and live report in one capture.
internal static class M750Probe {
    static int Main() {
        HidDevice bolt=null;
        foreach(var d in Native.Enumerate(null)) if(d.Pid==0xC548) bolt=d;
        if(bolt==null) { Console.WriteLine("NO_BOLT_DEVICE"); return 1; }
        Console.WriteLine("BOLT "+bolt);
        var deadline=DateTime.UtcNow.AddSeconds(45);
        while(DateTime.UtcNow<deadline) {
            foreach(byte addr in new byte[]{1,255}) {
                try {
                    using(var h=new HidConnection(bolt)) {
                        h.Trace=Console.WriteLine;
                        byte fw=h.Feature(addr,3);
                        if(fw==0) { Console.WriteLine("addr={0}: no identity feature",addr); continue; }
                        byte[] identity=h.Query(addr,fw,0);
                        Console.WriteLine("addr={0} IDENTITY={1}",addr,BitConverter.ToString(identity));
                        byte nf=h.Feature(addr,5);
                        if(nf!=0) {
                            int len=h.Query(addr,nf,0)[0];
                            var sb=new StringBuilder();
                            for(int o=0;o<len;o+=16){var b=h.Query(addr,nf,0x10,(byte)o); sb.Append(Encoding.UTF8.GetString(b,0,Math.Min(b.Length,len-o)));}
                            Console.WriteLine("NAME="+sb);
                        }
                        foreach(ushort fid in new ushort[]{0x1000,0x1004,0x1001}) {
                            byte bi=h.Feature(addr,fid);
                            Console.WriteLine("addr={0} feature 0x{1:X4} idx={2}",addr,fid,bi);
                            if(bi==0) continue;
                            try {
                                Console.WriteLine("CAPS="+BitConverter.ToString(h.Query(addr,bi,0)));
                                Console.WriteLine("REPORT="+BitConverter.ToString(h.Query(addr,bi,0x10)));
                            } catch(Exception e) { Console.WriteLine("query err: "+e.Message); }
                        }
                        Console.WriteLine("M750_CAPTURED");
                        return 0;
                    }
                } catch(TimeoutException e) { Console.WriteLine("asleep: "+e.Message); }
                catch(Exception e) { Console.WriteLine("err: "+e.Message); }
            }
            Thread.Sleep(2000);
        }
        Console.WriteLine("NEVER_AWAKE_45S");
        return 1;
    }
}
