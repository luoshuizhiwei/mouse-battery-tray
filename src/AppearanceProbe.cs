using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using MouseBattery;

// Explicitly simulated integration probe; compiled separately from the product.
class AppearanceProbe {
    static double Metric(string status,string name) {
        var match=Regex.Match(status,@"\b"+name+@"=([0-9.]+)");
        if(!match.Success) throw new Exception("Missing metric: "+name+" in "+status);
        return double.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
    }
    [STAThread] static int Main() {
        if(Process.GetProcessesByName("MouseBattery").Length!=0) throw new Exception("Stop the real reader before publishing simulated battery values");
        using(var publisher=new TaskbarPublisher()) using(var host=new NativeHost()) {
            int[] values={0,9,10,20,21,86,100,100};
            for(int i=0;i<values.Length;i++) {
                int value=values[i]; bool stale=i==values.Length-1;
                var state=new DisplayState();
                state.Apply(BatteryReading.Parse(new byte[]{(byte)value,4,0,0},"布局验证（模拟电量）","mock","2.4G"),0);
                if(stale) state.Apply(null,1);
                publisher.MarkSuccess();publisher.Publish(state,1,0,false);
                host.Maintain();Thread.Sleep(2200);host.Maintain();
                string status=host.Status;
                if(!Regex.IsMatch(status,@"\bpercent="+value+@"\b") || !status.Contains("visible position=0") || !status.Contains("state="+(stale ? "2" : "1"))) throw new Exception("Wrong rendered value/state: "+status);
                if(Metric(status,"width")!=56 || Metric(status,"height")!=48) throw new Exception("Outer placement changed: "+status);
                // Pill width less its 4 DIP side padding and 1 DIP side borders.
                double content=Metric(status,"numberWidth")+Metric(status,"suffixWidth")+1.5;
                if(content>42) throw new Exception("Percent text overflows: "+status);
                string rgb=value<=10 ? "178,58,67" : value<=20 ? "145,96,18" : "35,50,68";
                string dark=value<=10 ? "247,150,155" : value<=20 ? "235,193,111" : "237,243,250";
                if(!status.Contains("textRGB="+rgb) && !status.Contains("textRGB="+dark)) throw new Exception("Wrong threshold color: "+status);
                Console.WriteLine("PASS simulated "+value+"% stale="+stale+" contentWidth="+content.ToString("F3",CultureInfo.InvariantCulture)+" | "+status);
            }
        }
        Console.WriteLine("PASS 8 actual native rendering checks (all readings simulated)");
        return 0;
    }
}
