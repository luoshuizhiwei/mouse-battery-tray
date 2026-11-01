using System;
using MouseBattery;

// Sweep a Bolt receiver's paired-device indices: Bolt hosts up to 3 devices,
// each on its own HID++ device index. M750 on channel 1 may not sit at index 1.
internal static class BoltSweep {
    static int Main() {
        foreach(var d in Native.Enumerate(null)) {
            if(d.Pid!=0xC548) continue;
            Console.WriteLine("C548 path="+d.Path);
            for(byte addr=1;addr<=6;addr++) {
                try {
                    using(var h=new HidConnection(d)) {
                        h.Trace=null;
                        byte fw=h.Feature(addr,3);
                        if(fw==0) { Console.WriteLine("addr {0}: identity idx=0 (unpaired)",addr); continue; }
                        byte[] identity=h.Query(addr,fw,0);
                        byte nf=h.Feature(addr,5);
                        string name="";
                        if(nf!=0) {
                            int len=h.Query(addr,nf,0)[0];
                            var bytes=new System.Collections.Generic.List<byte>();
                            while(bytes.Count<len) { var p=h.Query(addr,nf,0x10,(byte)bytes.Count); int n=Math.Min(p.Length,len-bytes.Count); for(int i=0;i<n;i++) bytes.Add(p[i]); }
                            name=System.Text.Encoding.UTF8.GetString(bytes.ToArray());
                        }
                        byte b4=h.Feature(addr,0x1004);
                        Console.WriteLine("addr {0}: name=\"{1}\" identity={2} f1004 idx={3}",addr,name,BitConverter.ToString(identity,7,4),b4);
                        if(b4!=0) Console.WriteLine("  REPORT="+BitConverter.ToString(h.Query(addr,b4,0x10)));
                    }
                } catch(Exception e) { Console.WriteLine("addr {0}: {1}",addr,e.Message); }
            }
            return 0;
        }
        Console.WriteLine("NO_C548");
        return 1;
    }
}
