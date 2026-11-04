using Microsoft.Win32;

namespace MouseBattery {
// User's chosen display surface. Tray is a first-class mode, Taskbar is an
// enhancement available only on a supported taskbar.dll build.
internal enum DisplayMode { Taskbar=0, Tray=1 }

internal static class Mode {
    internal const string SettingsKey=@"Software\MouseBatteryTray";
    internal const string ValueName="DisplayMode";

    // Pure parse: anything unexpected (missing / wrong type / out of range)
    // falls back to the default. Kept separate from registry I/O for unit tests.
    internal static DisplayMode Parse(object value) {
        if(value is int) {
            int n=(int)value;
            if(n==0) return DisplayMode.Taskbar;
            if(n==1) return DisplayMode.Tray;
        }
        return DisplayMode.Taskbar;
    }

    internal static DisplayMode Load() {
        using(var key=Registry.CurrentUser.OpenSubKey(SettingsKey)) {
            return key==null ? DisplayMode.Taskbar : Parse(key.GetValue(ValueName));
        }
    }

    internal static void Save(DisplayMode mode) {
        using(var key=Registry.CurrentUser.CreateSubKey(SettingsKey)) {
            key.SetValue(ValueName,(int)mode,RegistryValueKind.DWord);
        }
    }

    // Separate counter so "promote once" never depends on a system-owned key
    // whose value Windows may pre-create as 0.
    internal static bool PromotionDone() {
        using(var key=Registry.CurrentUser.OpenSubKey(SettingsKey)) {
            return key!=null && key.GetValue("TrayPromotedOnce") is int && (int)key.GetValue("TrayPromotedOnce")!=0;
        }
    }
    internal static void MarkPromotionDone() {
        using(var key=Registry.CurrentUser.CreateSubKey(SettingsKey)) {
            key.SetValue("TrayPromotedOnce",1,RegistryValueKind.DWord);
        }
    }
}
}
