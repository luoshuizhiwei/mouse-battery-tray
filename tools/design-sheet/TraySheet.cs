using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using MouseBattery;

// R sheet: renders the production TrayIcon.Render into a 1:1 contact sheet.
// Compiled only by build-tray.ps1; never part of the shipped exe.
class TraySheet {
    static readonly int[] Levels = { 100, 85, 21, 20, 11, 10, 0, -1 };
    static readonly int[] Sizes = { 16, 24, 32 };

    static int Main(string[] args) {
        string outDir = args.Length > 0 ? args[0] : @"E:\MouseBattery\design-sheet\tray";
        Directory.CreateDirectory(outDir);
        int cell = 96, pad = 14, labelH = 18, headerH = 46;
        int width = pad * 2 + Levels.Length * cell;
        int height = headerH + Sizes.Length * (cell + labelH) + pad;

        foreach (bool light in new bool[] { true, false }) {
            var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(sheet)) {
                g.Clear(light ? Color.FromArgb(239, 241, 245) : Color.FromArgb(30, 30, 46));
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var title = new Font("Segoe UI", 10f, FontStyle.Bold))
                using (var titleBrush = new SolidBrush(light ? Color.FromArgb(76, 79, 105) : Color.FromArgb(205, 214, 244))) {
                    g.DrawString("R sheet - TrayIcon.Render (production, 1:1 no scaling) | " + (light ? "Latte light" : "Mocha dark") + " | rows 16/24/32px", title, titleBrush, pad, 12);
                }
                for (int r = 0; r < Sizes.Length; r++) {
                    for (int c = 0; c < Levels.Length; c++) {
                        int size = Sizes[r], level = Levels[c];
                        int x = pad + c * cell, y = headerH + r * (cell + labelH);
                        using (var bmp = TrayIcon.Render(level, light, size)) {
                            g.DrawImage(bmp, x + (cell - size) / 2, y + (cell - size) / 2, size, size);
                            bmp.Save(Path.Combine(outDir, "tray-" + (light ? "light" : "dark") + "-" + size + "-" + level + ".png"), ImageFormat.Png);
                        }
                        using (var font = new Font("Consolas", 8f))
                        using (var brush = new SolidBrush(light ? Color.FromArgb(108, 111, 133) : Color.FromArgb(166, 173, 200))) {
                            g.DrawString(size + "px  " + (level < 0 ? "--" : level.ToString()), font, brush, x + 4, y + cell + 2);
                        }
                    }
                }
            }
            string path = Path.Combine(outDir, "sheet-R-" + (light ? "light" : "dark") + ".png");
            sheet.Save(path, ImageFormat.Png);
            sheet.Dispose();
            Console.WriteLine(path);
        }
        return 0;
    }
}
