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
};
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

// Pill coloring lives here so a pointer hover swaps the readout between its
// two states immediately: at rest it is bare text like the native clock, and
// only hovering wakes the glass pill — without waiting for the next 500 ms
// data dispatch. Weight stays constant: SemiBold glyphs are wider, and the
// centered readout would visibly jump on hover.
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
    StackPanel card; card.Width(224); card.Padding({14,12,14,12});
    v->tooltipName=TextBlock(); v->tooltipName.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipName.FontSize(12.0); v->tooltipName.FontWeight(Windows::UI::Text::FontWeights::Medium()); v->tooltipName.Opacity(0.85); v->tooltipName.Margin({0,0,0,6});
    v->tooltipValue=TextBlock(); v->tooltipValue.FontFamily(FontFamily(L"Segoe UI Variable Display")); v->tooltipValue.FontSize(30.0); v->tooltipValue.FontWeight(Windows::UI::Text::FontWeights::SemiBold()); v->tooltipValue.VerticalAlignment(VerticalAlignment::Center);
    // Editorial percent: the numeral stays threshold-colored, the unit recedes.
    StackPanel valueRow; valueRow.Orientation(Orientation::Horizontal);
    v->valueUnit=TextBlock(); v->valueUnit.Text(L"%"); v->valueUnit.FontFamily(FontFamily(L"Segoe UI Variable Display")); v->valueUnit.FontSize(13.0); v->valueUnit.Opacity(0.70); v->valueUnit.Margin({3,7,0,0}); v->valueUnit.VerticalAlignment(VerticalAlignment::Center);
    valueRow.Children().Append(v->tooltipValue); valueRow.Children().Append(v->valueUnit);
    v->tooltipStatus=TextBlock(); v->tooltipStatus.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipStatus.FontSize(11.0); v->tooltipStatus.FontWeight(Windows::UI::Text::FontWeights::Normal());
    v->tooltipStatus.Margin({0,4,0,0});
    // Charging is the one state that earns a symbol; verified E945 bolt.
    v->statusGlyph=FontIcon(); v->statusGlyph.Glyph(L"\uE945"); v->statusGlyph.FontFamily(FontFamily(L"Segoe MDL2 Assets")); v->statusGlyph.FontSize(12.0); v->statusGlyph.Margin({0,3,5,0}); v->statusGlyph.Visibility(Visibility::Collapsed);
    StackPanel statusRow; statusRow.Orientation(Orientation::Horizontal);
    statusRow.Children().Append(v->statusGlyph); statusRow.Children().Append(v->tooltipStatus);
    // Signature scale: threshold ticks at the 20% and 10% marks, drawn over
    // both the track and the fill so the readout carries its own warning scale.
    v->tooltipTrack=Grid(); v->tooltipTrack.Height(4); v->tooltipTrack.CornerRadius({2,2,2,2}); v->tooltipTrack.Margin({0,12,0,10});
    v->trackBackground=Border(); v->trackBackground.CornerRadius({2,2,2,2});
    v->tooltipFill=Border(); v->tooltipFill.HorizontalAlignment(HorizontalAlignment::Left); v->tooltipFill.CornerRadius({2,2,2,2});
    v->tickHigh=Windows::UI::Xaml::Shapes::Rectangle(); v->tickHigh.Width(1); v->tickHigh.HorizontalAlignment(HorizontalAlignment::Left); v->tickHigh.Margin({156.8,1,0,1});
    v->tickCritical=Windows::UI::Xaml::Shapes::Rectangle(); v->tickCritical.Width(1); v->tickCritical.HorizontalAlignment(HorizontalAlignment::Left); v->tickCritical.Margin({176.4,1,0,1});
    v->tooltipTrack.Children().Append(v->trackBackground); v->tooltipTrack.Children().Append(v->tooltipFill);
    v->tooltipTrack.Children().Append(v->tickHigh); v->tooltipTrack.Children().Append(v->tickCritical);
    v->tooltipConnection=TextBlock(); v->tooltipConnection.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipConnection.FontSize(11.0); v->tooltipConnection.Opacity(0.70);
    // Companion mouse summary fed by tooltip line 1; hidden for single-mouse setups.
    v->tooltipOther=TextBlock(); v->tooltipOther.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipOther.FontSize(11.0); v->tooltipOther.Opacity(0.70); v->tooltipOther.Margin({0,4,0,0}); v->tooltipOther.Visibility(Visibility::Collapsed);
    card.Children().Append(v->tooltipName); card.Children().Append(valueRow); card.Children().Append(statusRow); card.Children().Append(v->tooltipTrack); card.Children().Append(v->tooltipConnection); card.Children().Append(v->tooltipOther);
    v->tooltip=ToolTip(); v->tooltip.Padding({0,0,0,0}); v->tooltip.CornerRadius({8,8,8,8}); v->tooltip.Content(card); ToolTipService::SetToolTip(v->host,v->tooltip);
    v->menu=MenuFlyout();
    auto iconFor=[](wchar_t const* glyph){ FontIcon i; i.FontFamily(FontFamily(L"Segoe MDL2 Assets")); i.Glyph(glyph); return i; };
    v->settings=MenuFlyoutItem(); v->settings.Text(L"设置"); v->settings.Icon(iconFor(L"\uE713"));
    v->toggle=MenuFlyoutItem(); v->toggle.Text(L"切换开机启动"); v->toggle.Icon(iconFor(L"\uE72C"));
    v->exit=MenuFlyoutItem(); v->exit.Text(L"退出工具"); v->exit.Icon(iconFor(L"\uE7E8"));
    v->settingsToken=v->settings.Click([](auto const&,auto const&){Launch(L"--manage");});
    v->toggleToken=v->toggle.Click([](auto const&,auto const&){Launch(L"--toggle-startup");});
    v->exitToken=v->exit.Click([](auto const&,auto const&){Launch(L"--exit");});
    v->menu.Items().Append(v->settings); v->menu.Items().Append(v->toggle); v->menu.Items().Append(MenuFlyoutSeparator()); v->menu.Items().Append(v->exit);
    v->host.ContextFlyout(v->menu);
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
    Color color=light ? Color{255,35,50,68} : Color{255,237,243,250};
    Color fillTop=light ? Color{46,66,107,148} : Color{50,159,189,222};
    Color fillBottom=light ? Color{14,66,107,148} : Color{16,159,189,222};
    Color stroke=light ? Color{45,66,107,148} : Color{55,159,189,222};
    if(s.percent<=20) {
        color=light ? Color{255,145,96,18} : Color{255,235,193,111};
        fillTop=light ? Color{30,230,140,0} : Color{34,248,198,70};
        fillBottom=light ? Color{8,230,140,0} : Color{10,248,198,70};
        stroke=light ? Color{60,200,120,0} : Color{65,248,198,70};
    }
    if(s.percent<=10) {
        color=light ? Color{255,178,58,67} : Color{255,247,150,155};
        fillTop=light ? Color{32,196,43,28} : Color{38,255,125,125};
        fillBottom=light ? Color{10,196,43,28} : Color{12,255,125,125};
        stroke=light ? Color{60,196,43,28} : Color{65,255,125,125};
    }
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
    v.tooltipValue.Text(std::to_wstring(s.percent)+L"%");
    v.tooltipValue.Foreground(SolidColorBrush(color));
    std::wstring charge;
    switch(s.charging) {case 0:charge=L"未充电";break;case 1:charge=L"正在充电";break;case 2:charge=L"慢速充电";break;case 3:charge=L"已充满";break;case 4:charge=L"充电异常";break;default:charge=L"未知充电状态 ("+std::to_wstring(s.charging)+L")";}
    v.tooltipStatus.Text(s.state==2 ? L"正在重连 · 上次读数" : charge);
    v.tooltipConnection.Text(s.state==2 ? L"等待设备恢复连接" : s.transport==1 ? L"已连接 · USB 有线" : L"已连接 · 2.4G 无线");
    Color muted=light ? Color{255,113,128,148} : Color{255,159,177,198};
    Color accent=light ? Color{255,66,107,148} : Color{255,159,189,222};
    // Threshold ticks carry meaning: amber once the fill crosses 20%, red at
    // 10%; neutral while the reading is healthy.
    Color neutralTick=light ? Color{110,113,128,148} : Color{110,169,189,222};
    Color highTick=s.percent<=20 ? (light ? Color{200,145,96,18} : Color{200,235,193,111}) : neutralTick;
    Color critTick=s.percent<=10 ? (light ? Color{200,178,58,67} : Color{200,247,150,155}) : neutralTick;
    v.tickHigh.Fill(SolidColorBrush(highTick)); v.tickCritical.Fill(SolidColorBrush(critTick));
    bool charging=s.charging==1||s.charging==2;
    v.statusGlyph.Visibility(charging ? Visibility::Visible : Visibility::Collapsed);
    if(charging) v.statusGlyph.Foreground(SolidColorBrush(accent));
    v.tooltip.RequestedTheme(light ? ElementTheme::Light : ElementTheme::Dark);
    v.tooltip.Background(SolidColorBrush(light ? Color{255,250,252,255} : Color{255,34,44,57}));
    v.tooltip.BorderBrush(SolidColorBrush(light ? Color{255,220,230,242} : Color{255,49,64,82}));
    v.tooltipName.Foreground(SolidColorBrush(muted));
    v.valueUnit.Foreground(SolidColorBrush(muted));
    v.tooltipStatus.Foreground(SolidColorBrush(s.state==2 ? (light ? Color{255,145,96,18} : Color{255,235,193,111}) : muted));
    v.tooltipConnection.Foreground(SolidColorBrush(muted));
    v.tooltipOther.Foreground(SolidColorBrush(muted));
    v.trackBackground.Background(SolidColorBrush(light ? Color{255,229,237,246} : Color{255,49,64,82}));
    v.tooltipFill.Background(SolidColorBrush(s.percent<=20 ? color : accent));
    v.tooltipFill.Width(196.0*s.percent/100.0);
    Automation::AutomationProperties::SetName(v.host,hstring(s.tooltip));
    v.toggle.Text(s.startup ? L"关闭开机启动" : L"开启开机启动");
    v.host.UpdateLayout();
    auto point=v.host.TransformToVisual(root.Content().as<UIElement>()).TransformPoint({0,0});
    work.health=L"visible position="+std::to_wstring(s.position)+L" percent="+std::to_wstring(s.percent)+L" x="+std::to_wstring(point.X)+L" y="+std::to_wstring(point.Y)+L" width="+std::to_wstring(v.host.ActualWidth())+L" height="+std::to_wstring(v.host.ActualHeight())+L" parent="+std::wstring(get_class_name(v.parent));
    work.health+=L" style=mist-blue pillWidth="+std::to_wstring(v.pill.ActualWidth())+L" pillHeight="+std::to_wstring(v.pill.ActualHeight())+L" numberWidth="+std::to_wstring(v.text.ActualWidth())+L" suffixWidth="+std::to_wstring(v.suffix.ActualWidth());
    work.health+=L" state="+std::to_wstring(s.state)+L" textRGB="+std::to_wstring(color.R)+L","+std::to_wstring(color.G)+L","+std::to_wstring(color.B);
    auto solidRGB=[](Windows::UI::Xaml::Shapes::Rectangle const& r){ auto b=r.Fill().as<SolidColorBrush>(); return std::to_wstring(b.Color().R)+L","+std::to_wstring(b.Color().G)+L","+std::to_wstring(b.Color().B); };
    work.health+=L" tickHigh="+solidRGB(v.tickHigh)+L" tickCritical="+solidRGB(v.tickCritical)+L" other="+(v.tooltipOther.Visibility()==Visibility::Visible?L"1":L"0");
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
