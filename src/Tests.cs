using System;
using System.Drawing;
using System.IO;
using MouseBattery;
internal static class Tests {
    static int count;
    static void Check(bool valid,string name) { if(!valid) throw new Exception("FAIL: "+name); count++; Console.WriteLine("PASS: "+name); }
    static int Main(string[] args) {
        var wired=BatteryReading.Parse(new byte[]{0x5F,8,1,1},"PRO X 2 DEX","02-02-5C-94","有线");
        var wireless=BatteryReading.Parse(new byte[]{0x5F,8,0,0},"PRO X 2 DEX","02-02-5C-94","2.4G");
        Check(wired.Percent==95 && wired.ChargeText=="正在充电","captured wired report: 95%, charging");
        Check(wireless.Percent==95 && wireless.ChargeText=="未充电","captured wireless report: 95%, discharging");
        foreach(var bad in new[]{new byte[0],new byte[]{95,8,1},new byte[]{101,8,0,0},new byte[]{255,8,0,0}}) {
            bool rejected=false; try { BatteryReading.Parse(bad,"","",""); } catch(IOException) { rejected=true; }
            Check(rejected,"reject malformed/out-of-range external battery payload "+BitConverter.ToString(bad));
        }
        Check(BatteryReading.Parse(new byte[]{0,1,0,0},"","","").Percent==0,"zero is a valid percentage, not missing");
        Check(BatteryReading.Parse(new byte[]{100,8,3,1},"","","").ChargeText=="已充满","full charge status");
        Check(BatteryReading.Parse(new byte[]{80,4,99,0},"","","").ChargeText.Contains("未知"),"unknown charging status stays explicitly unknown");
        var state=new DisplayState();
        Check(!state.Visible(0),"cold start with no device stays hidden");
        state.Apply(wired,1000);
        Check(state.Visible(1000) && state.Tooltip(1000).Contains("有线"),"valid device makes icon visible");
        state.Apply(null,6000);
        Check(state.Visible(15999) && state.Tooltip(6000).Contains("旧读数"),"retry keeps visibly marked old data within 15 seconds");
        state.Apply(null,11000);
        Check(!state.Visible(16000),"repeated failure must not reset the 15-second expiry");
        state.Apply(wireless,17000);
        Check(state.Visible(17000) && !state.Tooltip(17000).Contains("旧读数") && state.Tooltip(17000).Contains("2.4G"),"reconnect restores icon and current transport");
        Check(!state.Visible(32000),"blocked polling cannot display old data indefinitely");
        Check(TrayIcon.Foreground(21,false)==Color.White && TrayIcon.Foreground(20,false)!=Color.White,"20 percent warning boundary");
        Check(TrayIcon.Foreground(10,false)!=TrayIcon.Foreground(11,false),"10 percent critical boundary");
        Check(TrayIcon.Foreground(-1,false)==TrayIcon.Foreground(95,false),"missing reading keeps the normal color, not a threshold");
        using(var missing=TrayIcon.Create(-1,false,24)) Check(missing.Width==24 && missing.Height==24,"missing-reading placeholder icon renders");
        Check(TrayIcon.Render(-1,true,16)!=null,"missing-reading placeholder bitmap renders");
        using(var sheet=new Bitmap(700,180)) using(var g=Graphics.FromImage(sheet)) {
            g.Clear(Color.FromArgb(42,42,42)); g.FillRectangle(Brushes.White,0,90,700,90);
            int[] levels={-1,0,9,10,11,20,21,95,100};
            for(int row=0;row<2;row++) for(int i=0;i<levels.Length;i++) {
                using(var b=TrayIcon.Render(levels[i],row==1,16)) g.DrawImageUnscaled(b,16+i*72,10+row*90);
                using(var b=TrayIcon.Render(levels[i],row==1,24)) g.DrawImageUnscaled(b,12+i*72,40+row*90);
                using(var icon=TrayIcon.Create(levels[i],row==1,24)) Check(icon.Width==24,"native icon created "+levels[i]+" light="+(row==1));
            }
            sheet.Save(args[0],System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("PASS "+count+" checks"); return 0;
    }
}
