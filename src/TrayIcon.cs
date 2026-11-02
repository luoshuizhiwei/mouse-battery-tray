using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MouseBattery {
internal static class TrayIcon {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    internal static Color Foreground(int percent,bool light) {
        if(percent<0) return MouseBattery.Ui.GetColor(light, Color.FromArgb(25,25,25), Color.White);
        if(percent<=10) return MouseBattery.Ui.CritInk(light);
        if(percent<=20) return MouseBattery.Ui.WarnInk(light);
        return MouseBattery.Ui.GetColor(light, Color.FromArgb(25,25,25), Color.White);
    }
    static System.Drawing.Drawing2D.GraphicsPath ShellPath(float x,float y,float w,float h,float radius) {
        var path=new System.Drawing.Drawing2D.GraphicsPath();
        float d=radius*2;
        path.AddArc(x,y,d,d,180,90);
        path.AddArc(x+w-d,y,d,d,270,90);
        path.AddArc(x+w-d,y+h-d,d,d,0,90);
        path.AddArc(x,y+h-d,d,d,90,90);
        path.CloseFigure();
        return path;
    }
    // Battery-shell tray mark: outlined body with terminal nub and the numeral
    // inside, threshold color applied to the whole instrument.
    internal static Bitmap Render(int percent,bool light,int size) {
        var bitmap=new Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap)) {
            g.Clear(Color.Transparent); g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var ink=new SolidBrush(Foreground(percent,light)))
            using(var pen=new Pen(ink,size*0.10f)) {
                float bodyH=size*0.62f,bodyW=size*0.74f;
                float bx=1f,by=(size-bodyH)/2f;
                using(var path=ShellPath(bx,by,bodyW,bodyH,bodyH*0.30f)) g.DrawPath(pen,path);
                float nubW=size*0.07f,nubH=bodyH*0.36f;
                g.FillRectangle(ink,bx+bodyW+pen.Width*0.6f,size/2f-nubH/2f,nubW,nubH);
                string text=percent<0 ? "--" : percent.ToString();
                using(var font=new Font("Segoe UI",size*((percent==100||percent<0) ? 0.32f : 0.42f),FontStyle.Bold,GraphicsUnit.Pixel))
                using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()) {
                    format.Alignment=StringAlignment.Center; format.LineAlignment=StringAlignment.Center;
                    format.FormatFlags=StringFormatFlags.NoWrap;
                    g.DrawString(text,font,ink,new RectangleF(bx-1,by-1,bodyW+2,bodyH+2),format);
                }
            }
        }
        return bitmap;
    }
    internal static Icon Create(int percent,bool light,int size) {
        using(var bitmap=Render(percent,light,size)) {
            IntPtr h=bitmap.GetHicon();
            try { using(var icon=Icon.FromHandle(h)) return (Icon)icon.Clone(); }
            finally { DestroyIcon(h); }
        }
    }
}
}
