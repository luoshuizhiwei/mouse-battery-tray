using System;
using System.Text;
using MouseBattery;
internal class Probe {
    static int Main() {
        bool found=false;
        foreach(var d in Native.Enumerate(null)) {
            Console.WriteLine(d);
            foreach(byte addr in new byte[]{255,1}) {
                try {
                    using(var h=new HidConnection(d)) {
                        h.Trace=Console.WriteLine;
                        Console.WriteLine("Address="+addr);
                        byte name=h.Feature(addr,5);
                        if(name!=0) {
                            int len=h.Query(addr,name,0)[0]; var text=new StringBuilder();
                            for(int offset=0;offset<len;offset+=16) { var b=h.Query(addr,name,0x10,(byte)offset); text.Append(Encoding.UTF8.GetString(b,0,Math.Min(b.Length,len-offset))); }
                            Console.WriteLine("NAME="+text);
                        }
                        byte fw=h.Feature(addr,3);
                        if(fw!=0) Console.WriteLine("IDENTITY="+BitConverter.ToString(h.Query(addr,fw,0)));
                        byte battery=h.Feature(addr,0x1004);
                        Console.WriteLine("BATTERY_FEATURE="+battery);
                        if(battery!=0) { Console.WriteLine("CAPS="+BitConverter.ToString(h.Query(addr,battery,0))); Console.WriteLine("BATTERY="+BitConverter.ToString(h.Query(addr,battery,0x10))); found=true; }
                    }
                } catch(TimeoutException e) { Console.WriteLine("TimeoutException: "+e.Message); }
                catch(System.IO.IOException e) { Console.WriteLine("IOException: "+e.Message); }
                catch(System.ComponentModel.Win32Exception e) { Console.WriteLine("Win32Exception: "+e.Message); }
            }
        }
        return found?0:1;
    }
}
