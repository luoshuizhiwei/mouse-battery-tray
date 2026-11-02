param([string]$Sheet = 'E:\MouseBattery\design-sheet\settings\sheet-S-dark.png')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$src = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Pix {
  static byte[] Grab(Bitmap bmp, Rectangle rect, out int stride) {
    var d = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try {
      var buf = new byte[d.Stride * rect.Height];
      Marshal.Copy(d.Scan0, buf, 0, buf.Length);
      stride = d.Stride;
      return buf;
    } finally { bmp.UnlockBits(d); }
  }
  public static long DiffRegions(string path, Rectangle a, Rectangle b, int tol) {
    using (var bmp = new Bitmap(path)) {
      int sa, sb;
      var pa = Grab(bmp, a, out sa);
      var pb = Grab(bmp, b, out sb);
      long n = 0;
      for (int j = 0; j < a.Height; j++) for (int i = 0; i < a.Width; i++) {
        int ka = j * sa + i * 4, kb = j * sb + i * 4;
        int d = Math.Max(Math.Abs(pa[ka] - pb[kb]), Math.Max(Math.Abs(pa[ka + 1] - pb[kb + 1]), Math.Abs(pa[ka + 2] - pb[kb + 2])));
        if (d > tol) n++;
      }
      return n;
    }
  }
  public static long[] Find(string path, int x, int y, int w, int h, int r, int g, int b, int tol) {
    using (var bmp = new Bitmap(path)) {
      var rect = new Rectangle(x, y, w, h);
      int stride;
      var buf = Grab(bmp, rect, out stride);
      long n = 0; int left = -1, right = -1, top = -1, bot = -1;
      for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) {
        int k = j * stride + i * 4;
        if (Math.Abs(buf[k] - b) <= tol && Math.Abs(buf[k + 1] - g) <= tol && Math.Abs(buf[k + 2] - r) <= tol) {
          n++;
          if (left < 0 || i < left) left = i;
          if (i > right) right = i;
          if (top < 0) top = j;
          bot = j;
        }
      }
      return new long[] { n, left, right, top, bot };
    }
  }
}
'@
Add-Type -TypeDefinition $src -ReferencedAssemblies @([System.Drawing.Bitmap].Assembly.Location)

$bmp = New-Object System.Drawing.Bitmap($Sheet)
Write-Output ("sheet=" + $Sheet + " " + $bmp.Width + "x" + $bmp.Height)
$bmp.Dispose()

$accent = [Pix]::Find($Sheet, 16, 112, 232, 26, 137, 180, 250, 8)
Write-Output ("toggle-on accent pixels=" + $accent[0] + " bbox=" + $accent[1] + ".." + $accent[2] + " x " + $accent[3] + ".." + $accent[4] + " => track " + ($accent[2] - $accent[1] + 1) + "x" + ($accent[4] - $accent[3] + 1) + " DIP (design 40x20 / legacy 36x18)")

$off = New-Object System.Drawing.Rectangle(16, 78, 232, 26)
$disabled = New-Object System.Drawing.Rectangle(16, 146, 232, 26)
$focus = New-Object System.Drawing.Rectangle(16, 214, 232, 26)
Write-Output ("disabled-off vs off differing pixels=" + [Pix]::DiffRegions($Sheet, $off, $disabled, 4) + " (0 = disabled has no visual state)")
Write-Output ("focus-off vs off differing pixels=" + [Pix]::DiffRegions($Sheet, $off, $focus, 4) + " (0 = focus ring did not render)")

$p0 = New-Object System.Drawing.Rectangle(16, 314, 480, 68)
$h1 = New-Object System.Drawing.Rectangle(16, 626, 480, 68)
Write-Output ("picker hover vs selected-only differing pixels=" + [Pix]::DiffRegions($Sheet, $p0, $h1, 4) + " (>0 = hover state visible)")

$mist = [Pix]::Find($Sheet, 16, 314, 480, 68, 59, 66, 86, 10)
$sapp = [Pix]::Find($Sheet, 16, 314, 480, 68, 116, 199, 236, 10)
Write-Output ("picker mist-blue composite pixels=" + $mist[0] + " bbox=" + $mist[1] + ".." + $mist[2] + " x " + $mist[3] + ".." + $mist[4])
Write-Output ("picker sapphire token pixels=" + $sapp[0] + " (identity mirror should own the pill)")
