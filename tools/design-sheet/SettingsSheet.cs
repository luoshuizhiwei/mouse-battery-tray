using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using MouseBattery;

// S sheet: renders the PRODUCTION WinForms controls (no replica painters) at
// their DIP geometry, in both themes, into one PNG per theme.
// 96-DPI logical raster by design: geometry and state are judged here, glyph
// hinting must be judged from the 150% PrintWindow shot instead.
// Compiled only by build-settings-sheet.ps1.
class SettingsSheet {
    static readonly string ToggleText = "\u767b\u5f55 Windows \u540e\u81ea\u52a8\u542f\u52a8";
    static bool light;

    [STAThread]
    static int Main(string[] args) {
        string outDir = args.Length > 0 ? args[0] : @"E:\MouseBattery\design-sheet\settings";
        Directory.CreateDirectory(outDir);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        foreach (bool theme in new bool[] { true, false }) {
            light = theme;
            var form = new Form {
                Text = "S sheet", FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0),
                ClientSize = new Size(1080, 1000), BackColor = Ui.Base(light), ForeColor = Ui.Text(light)
            };
            Build(form);
            form.Show();
            form.Refresh();
            var bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            string path = Path.Combine(outDir, "sheet-S-" + (light ? "light" : "dark") + ".png");
            bmp.Save(path, ImageFormat.Png);
            bmp.Dispose();
            form.Close();
            form.Dispose();
            Console.WriteLine(path);
        }
        return 0;
    }

    static Label Caption(Form form, string text, int x, int y, bool strong) {
        var label = new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = strong ? Ui.Text(light) : Ui.Subtext(light) };
        label.Font = new Font("Segoe UI", strong ? 10f : 8.5f, strong ? FontStyle.Bold : FontStyle.Regular);
        form.Controls.Add(label);
        return label;
    }

    static void SetPrivate(object target, string field, object value) {
        var info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        if (info != null) info.SetValue(target, value);
    }

    static ToggleSwitch MakeToggle(int x, int y, bool on, bool enabled, bool focus) {
        var toggle = new ToggleSwitch(light);
        toggle.Text = ToggleText;
        toggle.Location = new Point(x, y);
        toggle.Checked = on;
        toggle.Enabled = enabled;
        if (focus) toggle.Focus();
        return toggle;
    }

    static PositionPicker MakePicker(int x, int y, int position, int hover, bool focus) {
        var picker = new PositionPicker { Location = new Point(x, y), Size = new Size(480, 68) };
        picker.SetTheme(light);
        picker.Gaming = true;
        picker.Percent = 85;
        picker.Position = position;
        SetPrivate(picker, "hover", hover);
        picker.Invalidate();
        if (focus) picker.Focus();
        return picker;
    }

    static void Build(Form form) {
        Caption(form, "S sheet | production controls | " + (light ? "Latte light" : "Mocha dark") + " | 96-DPI logical raster (geometry/state only)", 16, 12, true);

        int y = 52;
        Caption(form, "ToggleSwitch (production: 232x26; track 36x18) - rest off / rest on / disabled / focus", 16, y, true);
        y += 26;
        form.Controls.Add(MakeToggle(16, y, false, true, false)); Caption(form, "off", 260, y + 5, false);
        form.Controls.Add(MakeToggle(16, y + 34, true, true, false)); Caption(form, "on", 260, y + 39, false);
        form.Controls.Add(MakeToggle(16, y + 68, false, false, false)); Caption(form, "disabled off", 260, y + 73, false);
        form.Controls.Add(MakeToggle(16, y + 102, true, false, false)); Caption(form, "disabled on", 260, y + 107, false);
        form.Controls.Add(MakeToggle(16, y + 136, false, true, true)); Caption(form, "focus (dotted ControlPaint rect: legacy, F9)", 260, y + 141, false);

        y += 210;
        Caption(form, "PositionPicker (production 480x68) - selected 0..3, then hover on unselected slots", 16, y, true);
        y += 26;
        form.Controls.Add(MakePicker(16, y, 0, -1, false)); Caption(form, "selected=0", 500, y + 24, false);
        form.Controls.Add(MakePicker(16, y + 78, 1, -1, false)); Caption(form, "selected=1", 500, y + 102, false);
        form.Controls.Add(MakePicker(16, y + 156, 2, -1, false)); Caption(form, "selected=2", 500, y + 180, false);
        form.Controls.Add(MakePicker(16, y + 234, 3, -1, false)); Caption(form, "selected=3", 500, y + 258, false);
        form.Controls.Add(MakePicker(16, y + 312, 0, 1, false)); Caption(form, "selected=0 hover=1 (hover fill visible)", 500, y + 336, false);

        y += 404;
        Caption(form, "RoundedPanel - Raised (Surface/Border) vs Sunken (Scrim/Border a150)", 16, y, true);
        y += 26;
        var raised = new RoundedPanel(Ui.Surface(light), Ui.Border(light)) { Location = new Point(16, y), Size = new Size(240, 64) };
        var sunken = new RoundedPanel(Ui.Scrim(light), Ui.WithAlpha(Ui.Border(light), 150)) { Location = new Point(272, y), Size = new Size(240, 64) };
        form.Controls.Add(raised); form.Controls.Add(sunken);
        Caption(form, "raised", 500, y + 22, false);
        Caption(form, "sunken", 500, y + 42, false);

        y += 88;
        Caption(form, "Buttons (production properties, rest only: hover/pressed need a live pointer)", 16, y, true);
        y += 26;
        var primary = new Button { Text = "\u5b8c\u6210", Location = new Point(16, y), Size = new Size(96, 34), FlatStyle = FlatStyle.Flat, BackColor = Ui.Accent(light), ForeColor = Ui.OnAccent(light) };
        primary.FlatAppearance.BorderSize = 0;
        var secondary = new Button { Text = "\u9000\u51fa\u5de5\u5177", Location = new Point(124, y), Size = new Size(96, 34), FlatStyle = FlatStyle.Flat, BackColor = Ui.Base(light), ForeColor = Ui.Subtext(light) };
        secondary.FlatAppearance.BorderSize = 0;
        form.Controls.Add(primary); form.Controls.Add(secondary);
        Caption(form, "primary rest / secondary rest", 240, y + 9, false);

        y += 60;
        Caption(form, "Degraded banner (production composition for the same theme)", 16, y, true);
        y += 26;
        var bannerBg = Ui.Overlay(Ui.Base(light), 255, Ui.WithAlpha(Ui.Yellow(light), light ? 22 : 26));
        var banner = new RoundedPanel(bannerBg, Ui.WithAlpha(Ui.Yellow(light), 105)) { Location = new Point(16, y), Size = new Size(520, 58) };
        var rail = new Panel { Location = new Point(0, 0), Size = new Size(3, 58), BackColor = Ui.WithAlpha(Ui.Yellow(light), 210) };
        var bannerText = new Label {
            Text = "\u4efb\u52a1\u680f\u7248\u672c\u672a\u9002\u914d\uff0c\u5f53\u524d\u4ee5\u6258\u76d8\u56fe\u6807\u663e\u793a\u7535\u91cf\uff08\u6307\u7eb9 CA1E335A\u20260741\uff09\uff0c\u9002\u914d\u540e\u81ea\u52a8\u6062\u590d",
            Location = new Point(20, 6), Size = new Size(470, 46),
            Font = new Font("Microsoft YaHei UI", Ui.PtFromDip(Ui.SizeMeta)),
            ForeColor = light ? Ui.YellowLight : Ui.WithAlpha(Ui.YellowDark, 245)
        };
        banner.Controls.Add(rail); banner.Controls.Add(bannerText);
        form.Controls.Add(banner);
    }
}
