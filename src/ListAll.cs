using System;
using System.Text;
using System.Threading;
using MouseBattery;

// Reconnaissance dump of every Logitech HID collection (no usage/size filter)
// plus a HID++ handshake attempt on the BLE M750 (PID B02C) vendor interface.
internal static class ListAll {
    static int Main() {
        var deadline=DateTime.UtcNow.AddSeconds(120);
        bool dumped=false;
        while(DateTime.UtcNow<deadline) {
            var all=Native.Enumerate(null,true);
            if(!dumped) {
                foreach(var d in all) {
                    if(d.Pid==0xC232) continue; // G HUB virtual device noise
                    Console.WriteLine("PID={0:X4} usage={1:X4}:{2:X4} in={3} out={4}",d.Pid,d.UsagePage,d.Usage,d.InputLength,d.OutputLength);
                }
                dumped=true;
            }
            foreach(var d in all) {
                if(d.Pid!=0xB02C) continue;
                Console.WriteLine("B02C seen: usage={0:X4}:{1:X4} in={2} out={3}",d.UsagePage,d.Usage,d.InputLength,d.OutputLength);
                if(d.UsagePage==0x0001 || d.UsagePage==0x000C) continue; // mouse/consumer collections carry no HID++
                foreach(byte addr in new byte[]{255,1}) {
                    try {
                        using(var h=new HidConnection(d)) {
                            h.Trace=Console.WriteLine;
                            byte fw=h.Feature(addr,3);
                            Console.WriteLine("PID=B02C usage={0:X4}:{1:X4} addr={2} identityIdx={3} in={4} out={5}",d.UsagePage,d.Usage,addr,fw,d.InputLength,d.OutputLength);
                            if(fw==0) continue;
                            byte[] identity=h.Query(addr,fw,0);
                            Console.WriteLine("IDENTITY="+BitConverter.ToString(identity));
                            byte b1=h.Feature(addr,0x1000);
                            Console.WriteLine("f1000 idx="+b1);
                            byte b4=h.Feature(addr,0x1004);
                            Console.WriteLine("f1004 idx="+b4);
                            if(b1!=0) { try { Console.WriteLine("f1000 CAPS="+BitConverter.ToString(h.Query(addr,b1,0))); Console.WriteLine("f1000 REPORT="+BitConverter.ToString(h.Query(addr,b1,0x10))); } catch(Exception e) { Console.WriteLine("f1000 err: "+e.Message); } }
                            if(b4!=0) { try { Console.WriteLine("f1004 CAPS="+BitConverter.ToString(h.Query(addr,b4,0))); Console.WriteLine("f1004 REPORT="+BitConverter.ToString(h.Query(addr,b4,0x10))); } catch(Exception e) { Console.WriteLine("f1004 err: "+e.Message); } }
                            Console.WriteLine("B02C_CAPTURED");
                            return 0;
                        }
                    } catch(Exception e) { Console.WriteLine("addr {0} err: {1}",addr,e.Message); }
                }
            }
            Thread.Sleep(2000);
        }
        Console.WriteLine("NO_B02C_HANDSHAKE_90S");
        return 1;
    }
}
