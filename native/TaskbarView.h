// Native XAML view, adapted from the v2.1 display module.
// No third-party runtime host or engine API. See THIRD-PARTY-NOTICES.md.
#include <windows.h>
#undef GetCurrentTime
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Input.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <atomic>
#include <memory>
#include <string>
#include <cstring>

using namespace winrt;
using namespace Windows::UI;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Media;

namespace {
struct Snapshot {
    uint32_t magic, sequence;
    uint64_t tick;
    int32_t state, percent, charging, transport, pid, position, startup, reserved;
    wchar_t tooltip[232];
};
static_assert(sizeof(Snapshot)==512);
HANDLE g_stop=nullptr, g_mapping=nullptr;
const Snapshot* g_data=nullptr;
std::wstring g_reader;
std::atomic<UINT> g_message{0};
using GetHostFn=void*(WINAPI*)(void*,void*);
using FrameHeightFn=int(WINAPI*)(void*);
using DecrefFn=void(WINAPI*)(void*);
GetHostFn g_getHost=nullptr;
FrameHeightFn g_frameHeight=nullptr;
DecrefFn g_decref=nullptr;
void* g_siteVtable=nullptr;
size_t g_elementOffset=0;

struct View {
    Panel parent{nullptr}; Border host{nullptr}; TextBlock text{nullptr}; ToolTip tooltip{nullptr};
    Border pill{nullptr}; TextBlock suffix{nullptr},tooltipName{nullptr},tooltipValue{nullptr},tooltipStatus{nullptr},tooltipConnection{nullptr};
    TextBlock valueUnit{nullptr},tooltipOther{nullptr};
    Border statusWell{nullptr};
    StackPanel statusLine{nullptr};
    TextBlock statusRailText{nullptr}; // right-aligned transport text sharing ④'s rail
    Grid tooltipTrack{nullptr}; Border trackBackground{nullptr}, tooltipFill{nullptr};
    Windows::UI::Xaml::Shapes::Rectangle tickHigh{nullptr}, tickCritical{nullptr};
    FontIcon statusGlyph{nullptr};
    ColumnDefinition column{nullptr},implicitColumn{nullptr};
    MenuFlyout menu{nullptr}; MenuFlyoutItem settings{nullptr}, toggle{nullptr}, exit{nullptr};
    event_token settingsToken{},toggleToken{},exitToken{},hoverEnter{},hoverExit{};
    // Pill color state kept so a pointer hover can lift the alphas without a
    // data dispatch; hover is the only affordance the tiny pill can carry.
    Color baseTop{},baseBottom{},baseStroke{};
    bool hover=false;
    int position=-1;
    // N3: Update() is dispatched every 500ms. Re-assigning a fresh
    // SolidColorBrush to an open ToolTip dismisses it mid-show, so the style
    // block only runs when an input actually changed.
    uint64_t styleSig=0;
    bool styled=false;
};

// Catppuccin theme tokens - matches Ui.cs static class
inline Color CatBase(bool light) { return light ? Color{255,239,241,245} : Color{255,30,30,46}; }       // #EFF1F5/#1E1E2E
inline Color CatSurface(bool light) { return light ? Color{255,255,255,255} : Color{255,49,50,68}; }                  // #FFFFFF/#313244
inline Color CatBorder(bool light) { return light ? Color{255,204,208,218} : Color{255,69,71,90}; }    // #CCD0DA/#45475A
inline Color CatText(bool light) { return light ? Color{255,76,79,105} : Color{255,205,214,244}; }     // #4C4F69/#CDD6F4
inline Color CatSubtext(bool light) { return light ? Color{255,108,111,133} : Color{255,166,173,200}; }// #6C6F85/#A6ADC8
inline Color CatAccent(bool light) { return light ? Color{255,30,102,245} : Color{255,137,180,250}; }   // #1E66F5/#89B4FA
inline Color CatYellow(bool light) { return light ? Color{255,223,142,29} : Color{255,249,226,175}; }   // #DF8E1D/#F9E2AF
inline Color CatRed(bool light) { return light ? Color{255,210,15,57} : Color{255,243,139,168}; }       // #D20F39/#F38BA8
inline Color CatMauve(bool light) { return light ? Color{255,136,57,239} : Color{255,203,166,247}; }    // #8839EF/#CBA6F7 PRO X
inline Color CatSapphire(bool light) { return light ? Color{255,32,159,181} : Color{255,116,199,236}; }// #209FB5/#74C7EC M750L
// Derived roles (design doc §6.1) - alpha overlays of the bases above.
inline Color CatIdentity(bool gaming,bool light) { return gaming ? CatMauve(light) : CatSapphire(light); }
inline Color CatScrim(bool light) { return light ? Color{255,246,247,249} : Color{255,48,49,66}; }      // #F6F7F9 / #303142 (pre-composed)
inline Color CatOnAccent(bool light) { return light ? CatSurface(light) : CatBase(light); }             // text on accent fills
inline Color WithAlphaA(Color c,int a) { Color r=c; r.A=(uint8_t)(a>255?255:a); return r; }

std::unique_ptr<View> g_view;

HWND TaskbarWindow() {
    HWND window=FindWindowW(L"Shell_TrayWnd",nullptr); DWORD pid=0;
    if(window) GetWindowThreadProcessId(window,&pid);
    return pid==GetCurrentProcessId() ? window : nullptr;
}

double ExternalLeftWidth() {
    HWND taskbar=TaskbarWindow(); RECT taskbarRect{}; GetWindowRect(taskbar,&taskbarRect);
    struct Context { HWND taskbar; RECT rectangle; LONG end; } context{taskbar,taskbarRect,taskbarRect.left};
    EnumChildWindows(taskbar,[](HWND child,LPARAM parameter)->BOOL {
        auto& c=*(Context*)parameter; DWORD pid=0; GetWindowThreadProcessId(child,&pid);
        // SetParent does not change WS_POPUP; TrafficMonitor's embedded dialog
        // is enumerated beneath the taskbar but GetParent reports its owner.
        if(pid==GetCurrentProcessId() || !IsWindowVisible(child)) return TRUE;
        RECT rectangle{}; GetWindowRect(child,&rectangle);
        if(rectangle.left<=c.rectangle.left+32 && rectangle.top<c.rectangle.bottom && rectangle.bottom>c.rectangle.top) c.end=std::max(c.end,rectangle.right);
        return TRUE;
    },(LPARAM)&context);
    return context.end>taskbarRect.left ? (context.end-taskbarRect.left)*96.0/GetDpiForWindow(taskbar)+8.0 : 0.0;
}

FrameworkElement Find(FrameworkElement const& root,wchar_t const* name) {
    if(!root) return nullptr;
    if(root.Name()==name) return root;
    int count=VisualTreeHelper::GetChildrenCount(root);
    for(int i=0;i<count;i++) {
        auto child=VisualTreeHelper::GetChild(root,i).try_as<FrameworkElement>();
        if(auto result=Find(child,name)) return result;
    }
    return nullptr;
}

XamlRoot TaskbarRoot(HWND window) {
    HWND bandWindow=(HWND)GetPropW(window,L"TaskbandHWND");
    if(!bandWindow) return nullptr;
    auto band=(void**)GetWindowLongPtrW(bandWindow,0);
    if(!band) return nullptr;
    void** site=nullptr;
    for(int i=0;i<=20;i++) if(band[i]==g_siteVtable) {site=band+i;break;}
    if(!site) return nullptr;
    void* host[2]{}; g_getHost(site,host);
    if(!host[0] || !host[1]) {if(host[1])g_decref(host[1]);return nullptr;}
    auto unknown=*(IUnknown**)((BYTE*)host[0]+g_elementOffset);
    FrameworkElement element{nullptr};
    if(unknown) unknown->QueryInterface(guid_of<FrameworkElement>(),put_abi(element));
    g_decref(host[1]);
    return element ? element.XamlRoot() : nullptr;
}

TextBlock StackKindText() {
    TextBlock t; t.FontFamily(FontFamily(L"Segoe UI Variable Text")); t.FontSize(10.5); t.FontWeight(Windows::UI::Text::FontWeights::Medium()); t.Opacity(0.85);
    return t;
}
// Pill coloring lives here so a pointer hover swaps the readout between its
void PaintPill(View& v) {
    if(!v.pill) return;
    if(!v.hover) {
        v.pill.Background(SolidColorBrush(Colors::Transparent()));
        v.pill.BorderBrush(SolidColorBrush(Colors::Transparent()));
        return;
    }
    LinearGradientBrush brush;
    brush.StartPoint({0,0}); brush.EndPoint({0,1});
    GradientStop s1; s1.Color(v.baseTop); s1.Offset(0.0);
    GradientStop s2; s2.Color(v.baseBottom); s2.Offset(1.0);
    brush.GradientStops().Append(s1); brush.GradientStops().Append(s2);
    v.pill.Background(brush); v.pill.BorderBrush(SolidColorBrush(v.baseStroke));
}

void Launch(wchar_t const* argument) {
    std::wstring command=L"\""+g_reader+L"\" "+argument;
    STARTUPINFOW start{sizeof(start)}; start.dwFlags=STARTF_USESHOWWINDOW; start.wShowWindow=SW_HIDE;
    PROCESS_INFORMATION process{};
    if(!CreateProcessW(g_reader.c_str(),command.data(),nullptr,nullptr,FALSE,0,nullptr,nullptr,&start,&process)) {
        NativeLog(L"Reader launch failed: %lu",GetLastError()); return;
    }
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
}

void Remove() {
    if(!g_view) return;
    auto& v=*g_view;
    v.menu.Hide();
    v.settings.Click(v.settingsToken); v.toggle.Click(v.toggleToken); v.exit.Click(v.exitToken);
    v.pill.PointerEntered(v.hoverEnter); v.pill.PointerExited(v.hoverExit);
    v.host.ContextFlyout(nullptr);
    uint32_t index=0;
    if(v.parent.Children().IndexOf(v.host,index)) v.parent.Children().RemoveAt(index);
    if(v.column) {
        auto grid=v.parent.as<Grid>();
        uint32_t slot=0;
        if(grid.ColumnDefinitions().IndexOf(v.column,slot)) {
            grid.ColumnDefinitions().RemoveAt(slot);
            for(auto const& item:grid.Children()) if(auto child=item.try_as<FrameworkElement>()) {
                int col=Grid::GetColumn(child), span=Grid::GetColumnSpan(child);
                if(col>(int)slot) Grid::SetColumn(child,col-1);
                else if(col<(int)slot && col+span>(int)slot) Grid::SetColumnSpan(child,span-1);
            }
        }
        // Restore the original implicit single-column Grid when we created it.
        if(v.implicitColumn && grid.ColumnDefinitions().Size()==1 && grid.ColumnDefinitions().GetAt(0)==v.implicitColumn) grid.ColumnDefinitions().RemoveAt(0);
    }
    NativeLog(L"Removed taskbar text and released its layout slot");
    g_view.reset();
}

bool Insert(FrameworkElement const& root,int position) {
    auto parent=Find(root,position==3 ? L"RootGrid" : L"SystemTrayFrameGrid").try_as<Panel>();
    if(!parent) {NativeLog(L"Taskbar placement parent unavailable");return false;}
    auto grid=parent.try_as<Grid>(); auto stack=parent.try_as<StackPanel>();
    if(!grid && (!stack || stack.Orientation()!=Orientation::Horizontal)) {
        NativeLog(L"Unsupported placement panel: %s",get_class_name(parent).c_str()); return false;
    }
    uint32_t slot=0;
    if(position==1 || position==2) {
        auto reference=Find(parent,position==1 ? L"NotificationCenterButton" : L"ShowDesktopStack");
        if(!reference) {NativeLog(L"Requested clock anchor unavailable");return false;}
        if(grid) slot=Grid::GetColumn(reference);
        else if(!parent.Children().IndexOf(reference,slot)) {NativeLog(L"Clock anchor is not a direct child");return false;}
    }
    if(grid && slot>grid.ColumnDefinitions().Size()) {NativeLog(L"Invalid taskbar column");return false;}

    auto v=std::make_unique<View>(); v->parent=parent; v->position=position;
    v->host=Border(); v->host.Name(L"MouseBatteryTaskbarText");
    Automation::AutomationProperties::SetAutomationId(v->host,L"MouseBatteryTaskbarText");
    v->host.MinWidth(56); v->host.VerticalAlignment(VerticalAlignment::Stretch);
    if(position==3) {v->host.Width(56);v->host.HorizontalAlignment(HorizontalAlignment::Right);}
    v->host.Background(SolidColorBrush(Colors::Transparent()));
    v->pill=Border(); v->pill.Width(52); v->pill.Height(28); v->pill.CornerRadius({9,9,9,9});
    v->pill.Padding({4,0,4,0}); v->pill.BorderThickness({1,1,1,1});
    v->pill.HorizontalAlignment(HorizontalAlignment::Center); v->pill.VerticalAlignment(VerticalAlignment::Center);
    StackPanel number; number.Orientation(Orientation::Horizontal); number.HorizontalAlignment(HorizontalAlignment::Center); number.VerticalAlignment(VerticalAlignment::Center);
    // At rest the readout is bare text (Text face, quiet weight, no
    // background) but one step larger than the clock for legibility; hovering
    // wakes the glass pill and the data weight.
    v->text=TextBlock(); v->text.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->text.FontSize(15.0); v->text.FontWeight(Windows::UI::Text::FontWeights::Normal()); v->text.VerticalAlignment(VerticalAlignment::Center);
    v->suffix=TextBlock(); v->suffix.Text(L"%"); v->suffix.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->suffix.FontSize(12.0); v->suffix.Opacity(0.80); v->suffix.Margin({1,2,0,0}); v->suffix.VerticalAlignment(VerticalAlignment::Center);
    number.Children().Append(v->text); number.Children().Append(v->suffix); v->pill.Child(number); v->host.Child(v->pill);
    StackPanel card; card.Width(240); card.Padding({20,20,20,20});
    v->tooltipName=TextBlock(); v->tooltipName.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipName.FontSize(12.0); v->tooltipName.FontWeight(Windows::UI::Text::FontWeights::Medium()); v->tooltipName.Opacity(0.85); v->tooltipName.Margin({0,0,0,6});
    v->tooltipValue=TextBlock(); v->tooltipValue.FontFamily(FontFamily(L"Segoe UI Variable Display")); v->tooltipValue.FontSize(40.0); v->tooltipValue.FontWeight(Windows::UI::Text::FontWeights::SemiBold()); v->tooltipValue.VerticalAlignment(VerticalAlignment::Center);
    // Editorial percent: the numeral stays threshold-colored, the unit recedes.
    StackPanel valueRow; valueRow.Orientation(Orientation::Horizontal);
    v->valueUnit=TextBlock(); v->valueUnit.Text(L"%"); v->valueUnit.FontFamily(FontFamily(L"Segoe UI Variable Display")); v->valueUnit.FontSize(15.0); v->valueUnit.Opacity(0.80); v->valueUnit.Margin({4,0,0,6}); v->valueUnit.VerticalAlignment(VerticalAlignment::Bottom); // baseline-hug the numeral
    valueRow.Children().Append(v->tooltipValue); valueRow.Children().Append(v->valueUnit);
    v->tooltipStatus=TextBlock(); v->tooltipStatus.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipStatus.FontSize(12.0); v->tooltipStatus.FontWeight(Windows::UI::Text::FontWeights::Normal()); v->tooltipStatus.VerticalAlignment(VerticalAlignment::Center);
    // Charging is the one state that earns a symbol; verified E945 bolt. The
    // 18x18 identity-tinted well keeps the glyph centered (kills the old
    // baseline mismatch) and doubles as the state's only "component".
    v->statusWell=Border(); v->statusWell.Width(18); v->statusWell.Height(18); v->statusWell.CornerRadius({6,6,6,6}); v->statusWell.VerticalAlignment(VerticalAlignment::Center); v->statusWell.Margin({0,0,6,0}); v->statusWell.Visibility(Visibility::Collapsed);
    v->statusGlyph=FontIcon(); v->statusGlyph.Glyph(L"\uE945"); v->statusGlyph.FontFamily(FontFamily(L"Segoe MDL2 Assets")); v->statusGlyph.FontSize(12.0);
    v->statusWell.Child(v->statusGlyph);
    StackPanel statusRow; statusRow.Orientation(Orientation::Horizontal);
    statusRow.Children().Append(v->statusWell); statusRow.Children().Append(v->tooltipStatus);
    v->tooltipConnection=TextBlock(); v->tooltipConnection.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipConnection.FontSize(11.0); v->tooltipConnection.Opacity(0.75); v->tooltipConnection.VerticalAlignment(VerticalAlignment::Center);
    StackPanel statusLine; statusLine.Orientation(Orientation::Horizontal); // ④ state left / transport right, one rail
    statusLine.Children().Append(statusRow);
    v->statusLine=statusLine; v->statusRailText=v->tooltipConnection;
    // Signature scale: ticks at the REAL 20%/10% thresholds, taller than the
    // slot so lighting up reads as a gauge notch (F2); container must be 14 high.
    v->tooltipTrack=Grid(); v->tooltipTrack.Height(14); v->tooltipTrack.Margin({0,10,0,10});
    v->trackBackground=Border(); v->trackBackground.Height(6); v->trackBackground.CornerRadius({3,3,3,3}); v->trackBackground.VerticalAlignment(VerticalAlignment::Center);
    v->tooltipFill=Border(); v->tooltipFill.Height(6); v->tooltipFill.HorizontalAlignment(HorizontalAlignment::Left); v->tooltipFill.CornerRadius({3,3,3,3}); v->tooltipFill.VerticalAlignment(VerticalAlignment::Center);
    v->tickHigh=Windows::UI::Xaml::Shapes::Rectangle(); v->tickHigh.Width(1.5); v->tickHigh.Height(12); v->tickHigh.VerticalAlignment(VerticalAlignment::Center); v->tickHigh.HorizontalAlignment(HorizontalAlignment::Left); v->tickHigh.Margin({57.75,0,0,0});
    v->tickCritical=Windows::UI::Xaml::Shapes::Rectangle(); v->tickCritical.Width(1.5); v->tickCritical.Height(12); v->tickCritical.VerticalAlignment(VerticalAlignment::Center); v->tickCritical.HorizontalAlignment(HorizontalAlignment::Left); v->tickCritical.Margin({37.75,0,0,0});
    v->tooltipTrack.Children().Append(v->trackBackground); v->tooltipTrack.Children().Append(v->tooltipFill);
    v->tooltipTrack.Children().Append(v->tickHigh); v->tooltipTrack.Children().Append(v->tickCritical);
    // Companion mouse summary fed by tooltip line 1; hidden for single-mouse setups.
    v->tooltipOther=TextBlock(); v->tooltipOther.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipOther.FontSize(11.5); v->tooltipOther.Opacity(0.88); v->tooltipOther.Margin({0,8,0,0}); v->tooltipOther.Visibility(Visibility::Collapsed);
    card.Children().Append(v->tooltipName); card.Children().Append(valueRow); card.Children().Append(v->tooltipTrack); card.Children().Append(statusLine); card.Children().Append(v->tooltipOther);
    v->tooltip=ToolTip(); v->tooltip.Padding({0,0,0,0}); v->tooltip.CornerRadius({12,12,12,12}); v->tooltip.Content(card); ToolTipService::SetToolTip(v->pill,v->tooltip); // pill only: hovering the empty slot corners must not open the card
    v->menu=MenuFlyout();
    auto iconFor=[](wchar_t const* glyph){ FontIcon i; i.FontFamily(FontFamily(L"Segoe MDL2 Assets")); i.Glyph(glyph); return i; };
    v->settings=MenuFlyoutItem(); v->settings.Text(L"设置"); v->settings.Icon(iconFor(L"\uE713"));
    v->toggle=MenuFlyoutItem(); v->toggle.Text(L"切换开机启动"); v->toggle.Icon(iconFor(L"\uE72C"));
    v->exit=MenuFlyoutItem(); v->exit.Text(L"退出工具"); v->exit.Icon(iconFor(L"\uE7E8"));
    v->settingsToken=v->settings.Click([](auto const&,auto const&){Launch(L"--manage");});
    v->toggleToken=v->toggle.Click([](auto const&,auto const&){Launch(L"--toggle-startup");});
    v->exitToken=v->exit.Click([](auto const&,auto const&){Launch(L"--exit");});
    v->menu.Items().Append(v->settings); v->menu.Items().Append(v->toggle); v->menu.Items().Append(MenuFlyoutSeparator()); v->menu.Items().Append(v->exit);
    v->pill.ContextFlyout(v->menu); // right-click menu also scoped to the visible pill
    // Hover reveals the glass pill; left click stays inert on purpose so a
    // stray click on the readout can never trigger anything.
    v->hoverEnter=v->pill.PointerEntered([](auto const&,auto const&){ if(g_view){ g_view->hover=true; PaintPill(*g_view); } });
    v->hoverExit=v->pill.PointerExited([](auto const&,auto const&){ if(g_view){ g_view->hover=false; PaintPill(*g_view); } });
    if(grid) {
        // An empty ColumnDefinitions collection means one implicit star column.
        // Materialize it before inserting our Auto column; otherwise native
        // children shifted to column 1 would be clamped back into our column 0.
        if(grid.ColumnDefinitions().Size()==0) {
            v->implicitColumn=ColumnDefinition(); v->implicitColumn.Width({1,GridUnitType::Star});
            grid.ColumnDefinitions().Append(v->implicitColumn);
        }
        v->column=ColumnDefinition(); v->column.Width({1,GridUnitType::Auto});
        if(position==3) v->column.Width({ExternalLeftWidth()+56,GridUnitType::Pixel});
        grid.ColumnDefinitions().InsertAt(slot,v->column);
        for(auto const& item:grid.Children()) if(auto child=item.try_as<FrameworkElement>()) {
            int col=Grid::GetColumn(child),span=Grid::GetColumnSpan(child);
            if(col>=(int)slot) Grid::SetColumn(child,col+1);
            else if(col+span>(int)slot) Grid::SetColumnSpan(child,span+1);
        }
        Grid::SetColumn(v->host,slot);
        Grid::SetRowSpan(v->host,(int)std::max(1u,grid.RowDefinitions().Size()));
        grid.Children().Append(v->host);
    } else parent.Children().InsertAt(slot,v->host);
    NativeLog(L"Inserted native text: position=%d panel=%s slot=%u",position,get_class_name(parent).c_str(),slot);
    g_view=std::move(v); return true;
}

// N3: hash of every input the style block depends on. An unchanged hash means
// the 500ms dispatch has nothing to repaint, so an open ToolTip stays open.
uint64_t Signature(Snapshot const& s,bool light) {
    uint64_t h=1469598103934665603ULL;
    for(wchar_t const* p=s.tooltip;*p;++p) { h^=(uint64_t)(uint16_t)*p; h*=1099511628211ULL; }
    uint64_t v=(uint64_t)(light?1:0)|((uint64_t)(s.reserved?1:0)<<1)|((uint64_t)(s.state&3)<<2)
        |((uint64_t)(s.percent&127)<<4)|((uint64_t)(s.charging&7)<<11)|((uint64_t)(s.transport&7)<<14)
        |((uint64_t)(s.startup?1:0)<<17)|((uint64_t)(s.position&3)<<18);
    return v^(h<<20);
}

struct Work { Snapshot snapshot{}; bool remove=false, ran=false; std::wstring health; };
void Update(Work& work) {
    if(work.remove || work.snapshot.state==0) {Remove();work.health=L"hidden";return;}
    auto& s=work.snapshot;
    auto root=TaskbarRoot(TaskbarWindow());
    if(!root) {NativeLog(L"Taskbar XamlRoot not ready");work.health=L"XamlRoot unavailable";return;}
    if(g_view && (g_view->position!=s.position || g_view->host.XamlRoot()!=root)) Remove();
    if(!g_view && !Insert(root.Content().as<FrameworkElement>(),s.position)) {work.health=L"placement unavailable";return;}
    auto& v=*g_view;
    if(s.position==3) v.column.Width({ExternalLeftWidth()+56,GridUnitType::Pixel});
    v.text.Text(std::to_wstring(s.percent));
    bool light=v.host.ActualTheme()==ElementTheme::Light;
    bool gaming=s.reserved!=0; // personality rides the reserved field (ABI-compatible)
    Color identity=CatIdentity(gaming,light);
    // Design doc §4.2: hover glass in identity color, threshold overrides.
    // Dark: gaming top α88/bottom α26/stroke α140; office α66/20/90.
    // Light: gaming α60/18/120; office α46/14/80. Threshold multiplies α×0.88.
    double mul=1.0; // threshold decay
    bool warnYellow=s.percent>10&&s.percent<=20, warnRed=s.percent<=10;
    if(warnYellow||warnRed) mul=0.88;
    if(s.state==2) mul*=0.72; // stale reading decays everything
    Color pillColor=warnRed ? CatRed(light) : warnYellow ? CatYellow(light) : identity;
    int topA=light ? (int)((gaming?60:46)*mul) : (int)((gaming?88:66)*mul);
    int botA=light ? (int)((gaming?18:14)*mul) : (int)((gaming?26:20)*mul);
    int strA=light ? (int)((gaming?120:80)*mul) : (int)((gaming?140:90)*mul);
    Color color=warnRed ? CatRed(light) : warnYellow ? CatYellow(light) : CatText(light);
    Color fillTop=WithAlphaA(pillColor,topA);
    Color fillBottom=WithAlphaA(pillColor,botA);
    Color stroke=WithAlphaA(pillColor,strA);
    uint64_t sig=Signature(s,light);
    bool restyle=!v.styled||sig!=v.styleSig;
    if(restyle) { // N3: nothing changed => touch no ToolTip property, keep it open
    v.text.Foreground(SolidColorBrush(color)); v.suffix.Foreground(SolidColorBrush(color));
    // Layered glass pill: dense at the top edge, fading toward the bottom.
    v.baseTop=fillTop; v.baseBottom=fillBottom; v.baseStroke=stroke;
    PaintPill(v);
    std::wstring detail(s.tooltip);
    size_t nl=detail.find(L'\n');
    v.tooltipName.Text(nl==std::wstring::npos ? detail : detail.substr(0,nl));
    if(nl!=std::wstring::npos && nl+1<detail.size()) {
        v.tooltipOther.Visibility(Visibility::Visible);
        v.tooltipOther.Text(detail.substr(nl+1));
    } else v.tooltipOther.Visibility(Visibility::Collapsed);

    v.tooltipValue.Text(std::to_wstring(s.percent)); // numeral only: the % lives in valueUnit (F1)
    v.tooltipValue.Foreground(SolidColorBrush(color));
    Color muted=CatSubtext(light);
    Color accent=CatAccent(light);
    v.statusRailText.TextAlignment(TextAlignment::Right);
    // Threshold ticks: real 20%/10% positions; neutral = Subtext α110, crossed
    // = full Yellow/Red (design doc §2.2).
    Color neutralTick=WithAlphaA(muted,110);
    // N2: each notch is judged independently: the 20% mark stays lit at any
    // reading at or below 20 (including <=10), the 10% mark only at or below 10.
    Color highTick=s.percent<=20 ? CatYellow(light) : neutralTick;
    // N2: the 10% mark never inherits the 20% amber; it is neutral above 10%.
    Color critTick=warnRed ? CatRed(light) : neutralTick;
    v.tickHigh.Fill(SolidColorBrush(highTick)); v.tickCritical.Fill(SolidColorBrush(critTick));
    // State matrix (design doc §2.3).
    std::wstring charge;
    if(s.state==2) charge=L"正在重连 · 上次读数";
    else if(!gaming) charge=L"AA 电池供电";
    else switch(s.charging) {case 0:charge=L"未充电";break;case 1:charge=L"正在充电";break;case 2:charge=L"慢速充电";break;case 3:charge=L"已充满";break;case 4:charge=L"充电异常";break;default:charge=L"未知充电状态 ("+std::to_wstring(s.charging)+L")";}
    bool showWell=false;
    Color wellFill=identity, wellStroke=identity, boltColor=identity;
    if(s.state==2) { v.tooltipStatus.Foreground(SolidColorBrush(CatYellow(light))); }
    else if(!gaming || s.charging==0 || s.charging==3) {
        v.tooltipStatus.Foreground(SolidColorBrush(WithAlphaA(CatText(light),s.charging==3?235:220)));
        if(s.charging==3 && gaming) { /* full: no bolt */ }
        else if(!gaming) { v.tooltipStatus.Foreground(SolidColorBrush(WithAlphaA(CatSubtext(light),220))); }
    } else if(s.charging==4) {
        charge=L"充电异常"; v.tooltipStatus.Foreground(SolidColorBrush(CatRed(light)));
        wellFill=WithAlphaA(CatRed(light),34); wellStroke=WithAlphaA(CatRed(light),110); boltColor=CatRed(light); showWell=true;
    } else { // charging 1/2
        v.tooltipStatus.Foreground(SolidColorBrush(WithAlphaA(CatText(light),235)));
        wellFill=WithAlphaA(identity,34); wellStroke=WithAlphaA(identity,90); boltColor=identity; showWell=true;
    }
    v.statusWell.Visibility(showWell ? Visibility::Visible : Visibility::Collapsed);
    v.statusWell.Background(SolidColorBrush(wellFill)); v.statusWell.BorderBrush(SolidColorBrush(wellStroke)); v.statusWell.BorderThickness({1,1,1,1});
    v.statusGlyph.Foreground(SolidColorBrush(boltColor));
    v.tooltipStatus.Text(charge);
    v.tooltipConnection.Text(s.state==2 ? L"等待设备恢复连接" : s.transport==1 ? L"USB 有线" : s.transport==3 ? L"蓝牙" : s.transport==0 ? L"" : L"2.4G 无线"); // F4 + missing transport hides
    v.tooltipConnection.Visibility(s.transport==0 ? Visibility::Collapsed : Visibility::Visible);
    v.tooltip.RequestedTheme(light ? ElementTheme::Light : ElementTheme::Dark);
    // Card materials (design doc §2.4): Edge-lit gaming / Frosted office /
    // Warning override. Dark gets a Surface→Scrim gradient; light stays flat.
    if(light) v.tooltip.Background(SolidColorBrush(CatSurface(light)));
    else {
        LinearGradientBrush cardBrush;
        cardBrush.StartPoint({0,0}); cardBrush.EndPoint({0,1});
        GradientStop cs; cs.Color(CatSurface(light)); cs.Offset(0.0);
        GradientStop ce; ce.Color(CatScrim(light)); ce.Offset(1.0);
        cardBrush.GradientStops().Append(cs); cardBrush.GradientStops().Append(ce);
        v.tooltip.Background(cardBrush);
    }
    // Card edge: identity (Mauve gaming full α255 dark / Sapphire office α90);
    // warning forces α200 Yellow/Red. Dark-only per design doc §2.4 light rule.
    int edgeA=gaming ? (light?210:255) : (light?0:90);
    if(warnYellow||warnRed) { edgeA=200; }
    if(light && !gaming) edgeA=0; // frosted office: no visible edge in light
    if(light && gaming) v.tooltip.BorderBrush(SolidColorBrush(WithAlphaA(CatMauve(true),edgeA)));
    else if(warnYellow) v.tooltip.BorderBrush(SolidColorBrush(WithAlphaA(CatYellow(light),edgeA)));
    else if(warnRed) v.tooltip.BorderBrush(SolidColorBrush(WithAlphaA(CatRed(light),edgeA)));
    else if(gaming) v.tooltip.BorderBrush(SolidColorBrush(WithAlphaA(CatMauve(light),edgeA)));
    else v.tooltip.BorderBrush(SolidColorBrush(WithAlphaA(CatSapphire(light),edgeA==0?0:90)));
    bool edgeVisible=gaming||warnYellow||warnRed||!light;
    v.tooltip.BorderThickness(edgeVisible ? Thickness(1) : Thickness(0));
    v.tooltipName.Foreground(SolidColorBrush(WithAlphaA(CatText(light),235)));
    v.valueUnit.Foreground(SolidColorBrush(WithAlphaA(muted,205)));
    v.tooltipConnection.Foreground(SolidColorBrush(WithAlphaA(muted,190)));
    v.tooltipOther.Foreground(SolidColorBrush(WithAlphaA(muted,225)));
    bool staleRow=detail.find(L"旧读数")!=std::wstring::npos;
    v.tooltipOther.Foreground(SolidColorBrush(staleRow ? WithAlphaA(CatYellow(light),210) : WithAlphaA(muted,225)));
    v.trackBackground.Background(SolidColorBrush(CatBorder(light)));
    v.tooltipFill.Background(SolidColorBrush(s.percent<=20 ? color : identity));
    v.tooltipFill.Width(200.0*s.percent/100.0);
    Automation::AutomationProperties::SetName(v.host,hstring(s.tooltip));
    v.toggle.Text(s.startup ? L"关闭开机启动" : L"开启开机启动");
    v.host.UpdateLayout();
    v.styled=true; v.styleSig=sig;
    } // restyle
    auto point=v.host.TransformToVisual(root.Content().as<UIElement>()).TransformPoint({0,0});
    work.health=L"visible position="+std::to_wstring(s.position)+L" percent="+std::to_wstring(s.percent)+L" x="+std::to_wstring(point.X)+L" y="+std::to_wstring(point.Y)+L" width="+std::to_wstring(v.host.ActualWidth())+L" height="+std::to_wstring(v.host.ActualHeight())+L" parent="+std::wstring(get_class_name(v.parent));
    work.health+=L" style=catppuccin pillWidth="+std::to_wstring(v.pill.ActualWidth())+L" pillHeight="+std::to_wstring(v.pill.ActualHeight())+L" numberWidth="+std::to_wstring(v.text.ActualWidth())+L" suffixWidth="+std::to_wstring(v.suffix.ActualWidth());
    work.health+=L" state="+std::to_wstring(s.state)+L" textRGB="+std::to_wstring(color.R)+L","+std::to_wstring(color.G)+L","+std::to_wstring(color.B);
    auto solidRGB=[](Windows::UI::Xaml::Shapes::Rectangle const& r){ auto b=r.Fill().as<SolidColorBrush>(); return std::to_wstring(b.Color().R)+L","+std::to_wstring(b.Color().G)+L","+std::to_wstring(b.Color().B); };
    work.health+=L" tickHigh="+solidRGB(v.tickHigh)+L" tickCritical="+solidRGB(v.tickCritical)+L" other="+(v.tooltipOther.Visibility()==Visibility::Visible?L"1":L"0")+L" kind="+(gaming?L"gaming":L"office");
}

LRESULT CALLBACK DispatchHook(int code,WPARAM wParam,LPARAM lParam) {
    if(code==HC_ACTION) {
        auto message=(CWPSTRUCT const*)lParam;
        if(message->message==g_message.load()) {
            auto work=(Work*)message->lParam;
            if(!work->ran) {
                work->ran=true;
                try {Update(*work);}
                catch(hresult_error const& e) {work->health=L"XAML error "+std::to_wstring((unsigned)e.code().value)+L": "+std::wstring(e.message());NativeLog(L"%s",work->health.c_str());}
            }
        }
    }
    return CallNextHookEx(nullptr,code,wParam,lParam);
}
bool Dispatch(Work& work) {
    HWND window=TaskbarWindow(); if(!window) return false;
    DWORD thread=GetWindowThreadProcessId(window,nullptr);
    HHOOK hook=SetWindowsHookExW(WH_CALLWNDPROC,DispatchHook,nullptr,thread);
    if(!hook) {NativeLog(L"UI dispatch hook failed: %lu",GetLastError());return false;}
    SendMessageW(window,g_message.load(),0,(LPARAM)&work);
    UnhookWindowsHookEx(hook); return work.ran;
}

Snapshot Read() {
    Snapshot result{};
    if(!g_data) {
        g_mapping=OpenFileMappingW(FILE_MAP_READ,FALSE,L"Local\\MouseBatteryTaskbar.v2");
        if(!g_mapping) return result;
        g_data=(Snapshot const*)MapViewOfFile(g_mapping,FILE_MAP_READ,0,0,sizeof(Snapshot));
        if(!g_data) {NativeLog(L"MapViewOfFile failed: %lu",GetLastError());CloseHandle(g_mapping);g_mapping=nullptr;return result;}
    }
    uint32_t before=((Snapshot volatile const*)g_data)->sequence;
    if(before&1) return result;
    MemoryBarrier(); std::memcpy(&result,g_data,sizeof(result)); MemoryBarrier();
    uint32_t after=((Snapshot volatile const*)g_data)->sequence;
    if(before!=after || result.magic!=0x3254424D || result.percent<0 || result.percent>100 || result.state<0 || result.state>2 || result.position<0 || result.position>3 || GetTickCount64()-result.tick>=15000) result.state=0;
    result.tooltip[231]=0;
    return result;
}
}
