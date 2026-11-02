using System.Drawing;

namespace MouseBattery {
// Catppuccin design tokens - single source of truth for all managed layers.
// Hex bases come from the official palette; every derived value here is an
// alpha overlay of those bases (design doc §6.1). Native TaskbarView.h keeps
// same-valued Cat*() helpers - keep the two tables in sync.
internal static class Ui {
    // ── Base palette (never change these hexes) ──
    internal static readonly Color BaseLight = Color.FromArgb(239,241,245);    // #EFF1F5
    internal static readonly Color BaseDark = Color.FromArgb(30,30,46);       // #1E1E2E
    internal static readonly Color SurfaceLight = Color.White;                // #FFFFFF
    internal static readonly Color SurfaceDark = Color.FromArgb(49,50,68);    // #313244
    internal static readonly Color BorderLight = Color.FromArgb(204,208,218); // #CCD0DA
    internal static readonly Color BorderDark = Color.FromArgb(69,71,90);     // #45475A
    internal static readonly Color TextLight = Color.FromArgb(76,79,105);     // #4C4F69
    internal static readonly Color TextDark = Color.FromArgb(205,214,244);    // #CDD6F4
    internal static readonly Color SubtextLight = Color.FromArgb(108,111,133);// #6C6F85
    internal static readonly Color SubtextDark = Color.FromArgb(166,173,200); // #A6ADC8
    internal static readonly Color AccentLight = Color.FromArgb(30,102,245);  // #1E66F5
    internal static readonly Color AccentDark = Color.FromArgb(137,180,250);  // #89B4FA
    internal static readonly Color YellowLight = Color.FromArgb(223,142,29);  // #DF8E1D
    internal static readonly Color YellowDark = Color.FromArgb(249,226,175);  // #F9E2AF
    internal static readonly Color RedLight = Color.FromArgb(210,15,57);      // #D20F39
    internal static readonly Color RedDark = Color.FromArgb(243,139,168);     // #F38BA8
    internal static readonly Color MauveLight = Color.FromArgb(136,57,239);   // #8839EF PRO X
    internal static readonly Color MauveDark = Color.FromArgb(203,166,247);   // #CBA6F7
    internal static readonly Color SapphireLight = Color.FromArgb(32,159,181);// #209FB5 M750L
    internal static readonly Color SapphireDark = Color.FromArgb(116,199,236);// #74C7EC

    // ── Derived roles (alpha overlays of the bases; design doc §6.1) ──
    internal static Color Overlay(Color bottom,int alpha,Color top) {
        double a=top.A/255.0;
        int r=(int)(top.R*a+bottom.R*(1-a));
        int g=(int)(top.G*a+bottom.G*(1-a));
        int b=(int)(top.B*a+bottom.B*(1-a));
        return Color.FromArgb(255,r,g,b); // pre-composed solid for GDI+ parents
    }
    internal static Color WithAlpha(Color c,int alpha) { return Color.FromArgb(alpha,c.R,c.G,c.B); }

    internal static Color Base(bool light) { return light?BaseLight:BaseDark; }
    internal static Color Surface(bool light) { return light?SurfaceLight:SurfaceDark; }
    internal static Color Border(bool light) { return light?BorderLight:BorderDark; }
    internal static Color Text(bool light) { return light?TextLight:TextDark; }
    internal static Color Subtext(bool light) { return light?SubtextLight:SubtextDark; }
    internal static Color Accent(bool light) { return light?AccentLight:AccentDark; }
    internal static Color Yellow(bool light) { return light?YellowLight:YellowDark; }
    internal static Color Red(bool light) { return light?RedLight:RedDark; }
    internal static Color Mauve(bool light) { return light?MauveLight:MauveDark; }
    internal static Color Sapphire(bool light) { return light?SapphireLight:SapphireDark; }
    // identity = product-line color resolved by device kind
    internal static Color Identity(bool gaming,bool light) { return gaming?Mauve(light):Sapphire(light); }
    internal static Color OnAccent(bool light) { return light?SurfaceLight:BaseDark; }
    // Legacy shims kept while call sites migrate to the named role helpers.
    internal static Color GetColor(bool lightMode,Color light,Color dark) { return lightMode?light:dark; }
    internal static Color GetSurfaceColor(bool lightMode) { return Surface(lightMode); }
    internal static Color GetBorderColor(bool lightMode) { return Border(lightMode); }

    // Scrim: sunken surface. Dark = Base α18 over Surface (#303142); light = Border α22 over Surface.
    internal static Color Scrim(bool light) {
        return light ? Overlay(SurfaceLight,255,WithAlpha(BorderLight,22)) : Overlay(SurfaceDark,255,WithAlpha(BaseDark,18));
    }
    internal static Color AccentPlus(bool light) { return Overlay(Accent(light),255,WithAlpha(Text(light),34)); }
    internal static Color GhostHover(bool light) { return Overlay(Base(light),255,WithAlpha(Text(light),22)); }
    internal static Color GhostPressed(bool light) { return Overlay(Base(light),255,WithAlpha(Text(light),34)); }
    // Light-theme warning inks: Latte Yellow/Red glyphs measure 1.65:1 / 2.84:1
    // on a real light taskbar, so threshold *text* mixes toward Text; the glass
    // and the track keep the pure token hue.
    internal static Color WarnInk(bool light) { return light ? Overlay(YellowLight,255,WithAlpha(TextLight,170)) : YellowDark; }
    internal static Color CritInk(bool light) { return light ? Overlay(RedLight,255,WithAlpha(TextLight,48)) : RedDark; }

    // ── Geometry tokens (DIP; WinForms controls use 96-DPI units = DIP) ──
    internal const float CornerCard=12f;
    internal const float CornerPill=10f;
    internal const float CornerButton=10f;
    internal const float CornerIconWell=6f;
    internal const float StrokeFocus=1.5f;
    internal const float NotchWidth=1.5f;
    internal const float NotchHeight=12f;
    internal const float PaddingCard=20f;
    internal const int WidthCardNative=240;      // hover card
    internal const int WidthContentNative=200;   // hover card content column (1% = 2 DIP)
    internal const int WidthWindow=560;
    internal const int HeightWindowNormal=502;
    internal const int HeightWindowDegraded=426;
    internal const int WidthCardWindow=520;
    internal const int WidthCardContent=480;     // 1% = 4.8 DIP
    internal const int HeightTrack=6;
    internal const int HeightTrackBand=14;
    internal const float ScaleFactorNative=2.00f; // hover card fill DIP per percent

    // ── Type scale (DIP; WinForms pt = DIP×0.75) ──
    internal const float SizeNumeral=40f;        // big readout, both layers
    internal const float SizeUnit=15f;
    internal const float SizeDisplayTitle=20f;
    internal const float SizeHeading=13f;
    internal const float SizeBody=12f;
    internal const float SizeMeta=11f;
    internal const float SizeMicro=10.5f;
    internal const float SizeFaint=10f;
    internal const float SizePillNumber=15f;
    internal const float SizePillUnit=11.5f;
    internal static float PtFromDip(float dip) { return dip*0.75f; }
}
}
