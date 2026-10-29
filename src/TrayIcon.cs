using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MouseBattery {
internal static class TrayIcon {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    internal static Color Foreground(int percent,bool light) {
        if(percent<0) return light ? Color.FromArgb(25,25,25) : Color.White;
        if(percent<=10) return light ? Color.FromArgb(195,28,40) : Color.FromArgb(255,106,106);
        if(percent<=20) return light ? Color.FromArgb(150,104,0) : Color.FromArgb(255,210,70);
        return light ? Color.FromArgb(25,25,25) : Color.White;
    }
    internal static Bitmap Render(int percent,bool light,int size) {
        var bitmap=new Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap)) {
            g.Clear(Color.Transparent); g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            string text=percent<0 ? "--" : percent.ToString();
            using(var font=new Font("Segoe UI",size*((percent==100||percent<0) ? 0.64f : 0.82f),FontStyle.Bold,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(Foreground(percent,light)))
            using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()) {
                format.Alignment=StringAlignment.Center; format.LineAlignment=StringAlignment.Center;
                format.FormatFlags=StringFormatFlags.NoWrap;
                g.DrawString(text,font,brush,new RectangleF(-1,-1,size+2,size+2),format);
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
