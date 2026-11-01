namespace MouseBattery {
// Catppuccin theme tokens - single source of truth for all layers
// Mocha (dark) as default, Latte (light) for light mode
internal static class Ui {
    // Base colors
    internal static readonly System.Drawing.Color BaseLight = System.Drawing.Color.FromArgb(239, 241, 245);   // #EFF1F5
    internal static readonly System.Drawing.Color BaseDark = System.Drawing.Color.FromArgb(30, 30, 46);       // #1E1E2E
    internal static readonly System.Drawing.Color SurfaceLight = System.Drawing.Color.White;                  // #FFFFFF
    internal static readonly System.Drawing.Color SurfaceDark = System.Drawing.Color.FromArgb(49, 50, 68);    // #313244
    internal static readonly System.Drawing.Color BorderLight = System.Drawing.Color.FromArgb(204, 208, 218);  // #CCD0DA
    internal static readonly System.Drawing.Color BorderDark = System.Drawing.Color.FromArgb(69, 71, 90);      // #45475A
    internal static readonly System.Drawing.Color TextLight = System.Drawing.Color.FromArgb(76, 79, 105);      // #4C4F69
    internal static readonly System.Drawing.Color TextDark = System.Drawing.Color.FromArgb(205, 214, 244);     // #CDD6F4
    internal static readonly System.Drawing.Color SubtextLight = System.Drawing.Color.FromArgb(108, 111, 133); // #6C6F85
    internal static readonly System.Drawing.Color SubtextDark = System.Drawing.Color.FromArgb(166, 173, 200);  // #A6ADC8
    internal static readonly System.Drawing.Color AccentLight = System.Drawing.Color.FromArgb(30, 102, 245);   // #1E66F5
    internal static readonly System.Drawing.Color AccentDark = System.Drawing.Color.FromArgb(137, 180, 250);    // #89B4FA
    internal static readonly System.Drawing.Color YellowLight = System.Drawing.Color.FromArgb(223, 142, 29);    // #DF8E1D
    internal static readonly System.Drawing.Color YellowDark = System.Drawing.Color.FromArgb(249, 226, 175);    // #F9E2AF
    internal static readonly System.Drawing.Color RedLight = System.Drawing.Color.FromArgb(210, 15, 57);       // #D20F39
    internal static readonly System.Drawing.Color RedDark = System.Drawing.Color.FromArgb(243, 139, 168);       // #F38BA8
    internal static readonly System.Drawing.Color MauveLight = System.Drawing.Color.FromArgb(136, 57, 239);     // #8839EF (PRO X product line)
    internal static readonly System.Drawing.Color MauveDark = System.Drawing.Color.FromArgb(203, 166, 247);     // #CBA6F7
    internal static readonly System.Drawing.Color SapphireLight = System.Drawing.Color.FromArgb(32, 159, 181);  // #209FB5 (M750L product line)
    internal static readonly System.Drawing.Color SapphireDark = System.Drawing.Color.FromArgb(116, 199, 236);  // #74C7EC

    // Geometry tokens
    internal const float CornerCard = 10f;      // Card corner radius
    internal const float CornerPill = 9f;        // Pill corner radius
    internal const float CornerButton = 8f;      // Button corner radius
    internal const float PaddingCard = 16f;      // Card padding
    internal const float HeightTrack = 4f;       // Track bar height
    internal const float SpacingBase = 4f;       // Base spacing unit

    // Typography tokens
    internal const float SizeCaption = 10.5f;    // Caption text
    internal const float SizeBody = 11f;         // Body text
    internal const float SizeHeading = 12f;      // Heading text
    internal const float SizeDisplay = 15f;      // Display title (pill at rest)
    internal const float SizeNumeral = 30f;      // Numeral display (tooltip)

    // Helper to get color based on theme
    internal static System.Drawing.Color GetColor(bool lightMode, System.Drawing.Color light, System.Drawing.Color dark) {
        return lightMode ? light : dark;
    }

    // Theme-dependent tokens - helper functions
    internal static System.Drawing.Color GetSurfaceColor(bool lightMode) {
        return lightMode ? SurfaceLight : SurfaceDark;
    }

    internal static System.Drawing.Color GetBorderColor(bool lightMode) {
        return lightMode ? BorderLight : BorderDark;
    }
}
}