using System;
using System.Drawing;
using System.IO;
using MouseBattery;
internal static class Tests {
    static int count;
    static void Check(bool valid,string name) { if(!valid) throw new Exception("FAIL: "+name); count++; Console.WriteLine("PASS: "+name); }
    static int Main(string[] args) {
        var wired=BatteryReading.Parse(new byte[]{0x5F,8,1,1},"PRO X 2 DEX","unit-prox","有线");
        wired.Path="\\\\?\\hid#vid_046d&pid_c54d&mi_02#test"; wired.Gaming=true;
        var wireless=BatteryReading.Parse(new byte[]{0x5F,8,0,0},"PRO X 2 DEX","unit-prox","2.4G");
        wireless.Path=wired.Path; wireless.Gaming=true;
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
        state.Apply(new[]{wired},1000);
        Check(state.Visible(1000) && state.Tooltip(1000).Contains("PRO X 2 DEX") && state.Tooltip(1000).EndsWith("电竞"),"valid device shows its name and personality tag");
        state.MarkFailed(new[]{wired.Path},6000);
        Check(state.Visible(15999) && state.DisplayedFailed(15999),"failed attempts keep the readout marked within 15 seconds");
        Check(!state.Visible(16000),"repeated failure must not reset the 15-second expiry");
        state.Apply(new[]{wireless},17000);
        Check(state.Visible(17000) && !state.DisplayedFailed(17000) && state.Displayed(17000).Connection=="2.4G","reconnect restores icon and current transport");
        Check(!state.Visible(32000),"blocked polling cannot display old data indefinitely");
        var m750=BatteryReading.Parse(new byte[]{85,8,0,0},"Signature Plus M750 L","unit-m750","蓝牙"); m750.Gaming=false;
        m750.Path="\\\\?\\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02046d_pid&b02c_rev&0014";
        var proX=BatteryReading.Parse(new byte[]{95,8,0,0},"PRO X 2 DEX","unit-prox","2.4G"); proX.Gaming=true;
        proX.Path=wired.Path;
        state.Apply(new[]{proX,m750},40000);
        Check(state.Tooltip(40000).Contains("PRO X 2 DEX") && state.Tooltip(40000).Contains("M750"),"hover lists every known mouse");
        Check(state.Tooltip(40000).Contains("PRO X 2 DEX") && state.Tooltip(40000).Contains("Signature Plus M750 L") && state.Tooltip(40000).Contains("95% · 2.4G") && state.Displayed(40000).Percent==85 && state.Tooltip(40000).EndsWith("办公"),"tooltip lists both mice and tags the displayed personality");
        state.SetActive("unit-m750");
        Check(state.Displayed(40000).UnitId=="unit-m750","raw-input active device wins the pill");
        state.Apply(new[]{m750},52000);
        Check(state.Displayed(52000).UnitId=="unit-m750","active device keeps priority while its reading is fresh");
        state.Apply(new[]{proX},58000);
        state.SetActive("unit-prox");
        Check(state.Displayed(58000).UnitId=="unit-prox","switching mice moves the pill");
        Check(state.Tooltip(58000).Contains("\nSignature Plus M750 L · 85% · 蓝牙"),"idle companion stays listed while its reading is recent");
        state.SetActive("unit-m750");
        Check(state.Displayed(70000).UnitId=="unit-prox","stale active falls back to the freshest device");
        Check(!state.Visible(86000),"when every device goes quiet the readout hides");
        Check(state.KeyForRawPath(@"\\?\hid#vid_046d&pid_c54d&mi_00#8&77f4c7&0&0000")=="unit-prox","raw input maps the receiver by pid token");
        Check(state.KeyForRawPath(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02046d_pid&b02c_rev&0014_8&befd420")=="unit-m750","raw input maps the BLE gatt form");
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
