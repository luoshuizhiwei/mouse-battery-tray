// Native XAML view, adapted from the v2.1 display module.
// No third-party runtime host or engine API. See THIRD-PARTY-NOTICES.md.
void WorkerDiag(wchar_t const*); // defined in NativeHost.cpp; dispatch-chain diagnostics
#include <windows.h>
static bool ReadableAddr(const void* p,SIZE_T n) {
    MEMORY_BASIC_INFORMATION mbi{};
    if(!VirtualQuery(p,&mbi,sizeof(mbi))) return false;
    if(mbi.State!=MEM_COMMIT) return false;
    DWORD protect=mbi.Protect&0xFF;
    if(protect==PAGE_NOACCESS || (mbi.Protect&PAGE_GUARD)) return false;
    return (uintptr_t)p+n <= (uintptr_t)mbi.BaseAddress+mbi.RegionSize;
}
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
#include <winrt/Windows.UI.Xaml.Media.Animation.h>
#include <gdiplus.h>
#include <windowsx.h>
#include <chrono>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Xaml.Interop.h>
#include <winrt/Windows.UI.Xaml.Markup.h>
#include <winrt/Windows.UI.Input.h>
#include <winrt/Windows.System.h>
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
using namespace Windows::UI::Xaml::Media::Animation;

namespace {
// Owned layered menu; defined after Insert, declared here because Remove hides it.
void OwnedMenuHide();
void ShowOwnedMenu(bool light,bool startup,int anchorX,int anchorY,int dpi);
struct Snapshot {
    uint32_t magic, sequence;
    uint64_t tick;
    int32_t state, percent, charging, transport, pid, position, startup, reserved;
    wchar_t tooltip[216];                 // 48..479 (was 232; tail now carries probe fields)
    int32_t themeOverride;                // 480: 0 host theme, 1 force Light, 2 force Dark
    int32_t lastActiveTick;               // 484: reserved for the freshness badge (0 = unknown)
    int32_t lastSuccessTickAny;           // 488: reserved (0 = unknown)
    int32_t pad[5];                       // 492..511, keeps sizeof at 512
};
static_assert(sizeof(Snapshot)==512);
HANDLE g_stop=nullptr;
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
    Grid statusLine{nullptr};
    TextBlock statusRailText{nullptr}; // right-aligned transport text sharing ④'s rail
    Grid tooltipTrack{nullptr}; Border trackBackground{nullptr}, tooltipFill{nullptr}, tooltipHairline{nullptr};
    Windows::UI::Xaml::Shapes::Rectangle tickHigh{nullptr}, tickCritical{nullptr};
    Windows::UI::Xaml::Shapes::Ellipse nameHalo{nullptr}, nameDot{nullptr};
    FontIcon statusGlyph{nullptr};
    Border glowRing{nullptr}; Storyboard glowStory{nullptr}; bool glowOn=false;
    ColumnDefinition column{nullptr},implicitColumn{nullptr};
    Canvas overlay{nullptr}; // position 3: absolute-positioning parent that adds no layout slot
    double leftPad=-1.0; // position 3: DIP from the taskbar's left edge, cached so the
                         // 500ms dispatch only touches XAML when the readout really moves
    event_token menuOpenToken{};
    bool startupState=true; // snapshot's startup flag, drives the menu label
    event_token hoverEnter{},hoverExit{};
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
// Pre-composed blend: `top` at `alpha` over `bottom` (Ui.Overlay equivalent).
inline Color MixColor(Color bottom,Color top,int alpha) {
    double a=alpha/255.0;
    return Color{255,(uint8_t)(top.R*a+bottom.R*(1-a)),(uint8_t)(top.G*a+bottom.G*(1-a)),(uint8_t)(top.B*a+bottom.B*(1-a))};
}
// L1: on a real light taskbar the Latte warning glyphs measured 1.65:1/2.84:1,
// so only the *ink* mixes toward Text; glass/fill keep the pure token hue.
inline Color CatWarnInk(bool light) { return light ? MixColor(CatYellow(true),CatText(true),170) : CatYellow(false); }
inline Color CatCritInk(bool light) { return light ? MixColor(CatRed(true),CatText(true),48) : CatRed(false); }

std::unique_ptr<View> g_view;

HWND TaskbarWindow() {
    HWND window=FindWindowW(L"Shell_TrayWnd",nullptr); DWORD pid=0;
    if(window) GetWindowThreadProcessId(window,&pid);
    return pid==GetCurrentProcessId() ? window : nullptr;
}

// DIP between the right edge of the leftmost external taskbar window (on this
// machine: TrafficMonitor's embedded panel) and our readout. 8 was too tight -
// the bare 15 DIP numeral read as one more field of that panel.
const double kExternalGap=14.0;
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
    return context.end>taskbarRect.left ? (context.end-taskbarRect.left)*96.0/GetDpiForWindow(taskbar) : 0.0;
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
    if(!bandWindow) {WorkerDiag(L"tb-root-no-band");return nullptr;}
    auto band=(void**)GetWindowLongPtrW(bandWindow,0);
    if(!band) {WorkerDiag(L"tb-root-no-bandptr");return nullptr;}
    void** site=nullptr; int hitIndex=-1;
    for(int i=0;i<=20;i++) if(band[i]==g_siteVtable) {site=band+i;hitIndex=i;break;}
    if(!site) {WorkerDiag(L"tb-root-no-site");return nullptr;}
    // 9974/10011.16384: GetTaskbarHost's virtual helper returns stale offsets
    // (out[0]=TaskbarHost+0x278 read back as the scalar 3). The probe verified
    // the real chain by shape: site+0x1E8 = the TaskbarHost object;
    // TaskbarHost+0x278 = the XamlHost container; container+0x10 = the XamlRoot
    // element whose vtable lives in Windows.UI.Xaml.dll. Read that chain
    // directly; if the offsets ever drift again, re-locate the container by
    // scanning TaskbarHost for the same shape instead of trusting one number.
    void* taskbarHost=*(void**)((BYTE*)site+0x1E8);
    if(!taskbarHost || !ReadableAddr(taskbarHost,8)) {WorkerDiag(L"tb-root-no-th");return nullptr;}
    auto containerLooksValid=[&](void* c)->bool {
        if(!c || !ReadableAddr(c,0x18)) return false;
        void* u=*(void**)((BYTE*)c+g_elementOffset);
        if(!u || !ReadableAddr(u,8)) return false;
        uintptr_t vt=*(uintptr_t*)u;
        if(vt<0x10000 || vt>0x7FFFFFFEFFFF) return false;
        HMODULE mod=nullptr;
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(PCWSTR)vt,&mod)) return false;
        wchar_t path[260];
        if(!GetModuleFileNameW(mod,path,260)) return false;
        wchar_t* slash=wcsrchr(path,L'\\');
        return _wcsicmp(slash?slash+1:path,L"Windows.UI.Xaml.dll")==0;
    };
    void* container=*(void**)((BYTE*)taskbarHost+0x278);
    if(!containerLooksValid(container)) {
        container=nullptr;
        for(int off=0x200;off<0x380;off+=8) {
            if(!ReadableAddr((BYTE*)taskbarHost+off,8)) continue;
            void* p=*(void**)((BYTE*)taskbarHost+off);
            if(containerLooksValid(p)) {container=p;break;}
        }
        if(!container) {WorkerDiag(L"tb-root-no-container");return nullptr;}
    }
    auto unknown=*(IUnknown**)((BYTE*)container+g_elementOffset);
    FrameworkElement element{nullptr};
    if(unknown) unknown->QueryInterface(guid_of<FrameworkElement>(),put_abi(element));
    return element ? element.XamlRoot() : nullptr;
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
    if(v.glowStory) v.glowStory.Stop();
    OwnedMenuHide();
    if(v.menuOpenToken) v.host.RightTapped(v.menuOpenToken);
    v.pill.PointerEntered(v.hoverEnter); v.pill.PointerExited(v.hoverExit);
    v.host.ContextFlyout(nullptr);
    uint32_t index=0;
    if(v.overlay) {
        if(v.parent.Children().IndexOf(v.overlay,index)) v.parent.Children().RemoveAt(index);
        v.overlay=nullptr;
    }
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


// ---------------- owned right-click menu (GDI+, layered window) --------------
// The stock MenuFlyout cannot be reshaped (any presenter style setter stopped it
// from opening at all) and a XAML Flyout above the 48 DIP taskbar XamlRoot never
// receives pointer input. A plain layered popup in this process owns drawing,
// hit-testing and closing, so the card is entirely ours.
struct OwnedMenu {
    HWND hwnd=nullptr; HANDLE ready=nullptr; DWORD threadId=0;
    bool light=true, startup=true, visible=false;
    int hover=-1, anchorX=0, anchorY=0, dpi=96;
};
OwnedMenu g_menu;
std::mutex g_menuLock;

struct MenuMetrics { int cardW,cardH,rowH,pad,gap,shadow; };
MenuMetrics Metrics() {
    double s=g_menu.dpi/96.0;
    MenuMetrics m{};
    m.shadow=0; m.pad=(int)(4*s); m.gap=(int)(4*s); m.rowH=(int)(32*s);
    m.cardW=(int)(144*s); m.cardH=m.pad*2+m.rowH*3+m.gap;
    return m;
}
int MenuRowTop(MenuMetrics const& m,int row) { return m.pad+row*m.rowH+(row==2?m.gap:0); }
int HitRow(int x,int y) {
    MenuMetrics m=Metrics();
    int cx=x-m.shadow, cy=y-m.shadow;
    if(cx<m.pad || cx>m.cardW-m.pad) return -1;
    for(int i=0;i<3;i++) { int top=MenuRowTop(m,i); if(cy>=top && cy<top+m.rowH) return i; }
    return -1;
}
wchar_t const* RowGlyph(int row) { return row==0 ? L"\uE713" : row==1 ? L"\uE72C" : L"\uE7E8"; }
wchar_t const* RowText(int row,bool startup) { return row==0 ? L"设置" : row==1 ? (startup ? L"关闭开机启动" : L"开启开机启动") : L"退出工具"; }

void OwnedMenuHide() {
    if(g_menu.hwnd && g_menu.visible) {
        g_menu.visible=false; g_menu.hover=-1;
        KillTimer(g_menu.hwnd,1);
        ShowWindow(g_menu.hwnd,SW_HIDE);
        WorkerDiag(L"menu-owned-hidden");
    }
}
void OwnedMenuAction(int row) {
    Launch(row==0 ? L"--manage" : row==1 ? L"--toggle-startup" : L"--exit");
}
void RoundPath(Gdiplus::GraphicsPath& p,float x,float y,float w,float h,float r) {
    float d=r*2;
    p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90);
    p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90);
    p.CloseFigure();
}
void OwnedMenuPaint(HDC hdc) {
    if(!hdc) return;
    MenuMetrics m=Metrics();
    double s=g_menu.dpi/96.0;
    bool gLight=g_menu.light;
    Gdiplus::Color card = gLight ? Gdiplus::Color(255,255,255,255) : Gdiplus::Color(255,49,50,68);
    Gdiplus::Color edge = gLight ? Gdiplus::Color(115,204,208,218) : Gdiplus::Color(115,69,71,90);
    Gdiplus::Color ink  = gLight ? Gdiplus::Color(238,76,79,105) : Gdiplus::Color(238,205,214,244);
    Gdiplus::Color sub  = gLight ? Gdiplus::Color(225,108,111,133) : Gdiplus::Color(225,166,173,200);
    Gdiplus::Color hot  = gLight ? Gdiplus::Color(18,76,79,105) : Gdiplus::Color(22,205,214,244);
    Gdiplus::Graphics g(hdc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    g.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAliasGridFit);
    Gdiplus::GraphicsPath cardPath;
    RoundPath(cardPath,0.0f,0.0f,(float)m.cardW,(float)m.cardH,(float)(12*s));
    Gdiplus::SolidBrush cardBrush(card); g.FillPath(&cardBrush,&cardPath);
    Gdiplus::Pen edgePen(edge,1.0f); g.DrawPath(&edgePen,&cardPath);
    Gdiplus::FontFamily uiFam(L"Microsoft YaHei UI");
    Gdiplus::FontFamily iconFam(L"Segoe MDL2 Assets");
    Gdiplus::Font uiFont(&uiFam,12.0f*(float)s,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
    Gdiplus::Font iconFont(&iconFam,16.0f*(float)s,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
    Gdiplus::StringFormat left; left.SetAlignment(Gdiplus::StringAlignmentNear);
    left.SetLineAlignment(Gdiplus::StringAlignmentCenter); left.SetFormatFlags(Gdiplus::StringFormatFlagsNoWrap);
    for(int i=0;i<3;i++) {
        int top=MenuRowTop(m,i);
        if(g_menu.hover==i) {
            Gdiplus::GraphicsPath rp;
            RoundPath(rp,(float)m.pad,(float)top,(float)(m.cardW-m.pad*2),(float)m.rowH,(float)(6*s));
            Gdiplus::SolidBrush hb(hot); g.FillPath(&hb,&rp);
        }
        Gdiplus::SolidBrush iconBrush(g_menu.hover==i ? ink : sub);
        g.DrawString(RowGlyph(i),-1,&iconFont,Gdiplus::PointF((float)(m.pad+(int)(10*s)),(float)(top+m.rowH/2)),&left,&iconBrush);
        Gdiplus::SolidBrush textBrush(ink);
        g.DrawString(RowText(i,g_menu.startup),-1,&uiFont,Gdiplus::PointF((float)(m.pad+(int)(36*s)),(float)(top+m.rowH/2)),&left,&textBrush);
    }
    g.Flush();
}
LRESULT CALLBACK OwnedMenuProc(HWND h,UINT msg,WPARAM wp,LPARAM lp) {
    switch(msg) {
    case WM_APP+1: {
        g_menu.visible=true; g_menu.hover=-1;
        MenuMetrics m=Metrics();
        double ms=g_menu.dpi/96.0;
        int x=g_menu.anchorX-m.cardW/2, y=g_menu.anchorY-m.cardH;
        // Size and round the window while it is still hidden, then show: showing
        // first put an unclipped square frame on screen for one frame, which read
        // as the card "flashing" as it opened.
        SetWindowPos(h,HWND_TOPMOST,x,y,m.cardW,m.cardH,SWP_NOACTIVATE); // NOZORDER here would drop the TOPMOST request
        HRGN rgn=CreateRoundRectRgn(0,0,m.cardW+1,m.cardH+1,(int)(24*ms),(int)(24*ms));
        SetWindowRgn(h,rgn,TRUE);
        ShowWindow(h,SW_SHOWNOACTIVATE);
        InvalidateRect(h,nullptr,TRUE);
        // No SetCapture: a no-activate window cannot hold the pointer, and the
        // click that dismissed it would also reach whatever is underneath.
        SetTimer(h,1,80,nullptr);
        WorkerDiag(L"menu-owned-shown");
        return 0; }
    case WM_TIMER: {
        // Light dismiss: a click anywhere outside the card closes it.
        POINT p{}; GetCursorPos(&p);
        RECT r{}; GetWindowRect(h,&r);
        if(!PtInRect(&r,p) && (GetAsyncKeyState(VK_LBUTTON)&0x8000)) OwnedMenuHide();
        else if(!PtInRect(&r,p) && (GetAsyncKeyState(VK_RBUTTON)&0x8000)) OwnedMenuHide();
        return 0;
    }
    case WM_ERASEBKGND: return 1; // the card paints every pixel; erasing white first flickered
    case WM_PAINT: {
        PAINTSTRUCT ps; HDC dc=BeginPaint(h,&ps);
        RECT rc{}; GetClientRect(h,&rc);
        HDC mem=CreateCompatibleDC(dc);
        HBITMAP bmp=CreateCompatibleBitmap(dc,rc.right,rc.bottom);
        HGDIOBJ old=SelectObject(mem,bmp);
        OwnedMenuPaint(mem);
        BitBlt(dc,0,0,rc.right,rc.bottom,mem,0,0,SRCCOPY);
        SelectObject(mem,old); DeleteObject(bmp); DeleteDC(mem);
        EndPaint(h,&ps); return 0; }
    case WM_MOUSEMOVE: {
        int row=HitRow(GET_X_LPARAM(lp),GET_Y_LPARAM(lp));
        if(row!=g_menu.hover) { g_menu.hover=row; InvalidateRect(h,nullptr,TRUE); }
        return 0;
    }
    case WM_LBUTTONUP: {
        int row=HitRow(GET_X_LPARAM(lp),GET_Y_LPARAM(lp));
        OwnedMenuHide();
        if(row>=0) OwnedMenuAction(row);
        return 0;
    }
    case WM_CAPTURECHANGED: if(g_menu.visible) OwnedMenuHide(); return 0;
    case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
    case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(h,msg,wp,lp);
}
DWORD WINAPI OwnedMenuThread(void*) {
    ULONG_PTR token=0; Gdiplus::GdiplusStartupInput input;
    Gdiplus::GdiplusStartup(&token,&input,nullptr);
    WNDCLASSEXW wc{}; wc.cbSize=sizeof(wc); wc.lpfnWndProc=OwnedMenuProc; wc.hInstance=g_instance;
    wc.hCursor=LoadCursorW(nullptr,IDC_ARROW); wc.lpszClassName=L"MouseBatteryOwnedMenu";
    RegisterClassExW(&wc);
    g_menu.hwnd=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST|WS_EX_NOACTIVATE,
        L"MouseBatteryOwnedMenu",L"",WS_POPUP,0,0,10,10,nullptr,nullptr,g_instance,nullptr);
    if(!g_menu.hwnd) { WorkerDiag(L"menu-owned-window-failed"); if(g_menu.ready) SetEvent(g_menu.ready); return 1; }
    WorkerDiag(L"menu-owned-window-ready");
    if(g_menu.ready) SetEvent(g_menu.ready);
    MSG msg;
    while(GetMessageW(&msg,nullptr,0,0)>0) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    Gdiplus::GdiplusShutdown(token);
    return 0;
}
void ShowOwnedMenu(bool light,bool startup,int anchorX,int anchorY,int dpi) {
    {
        std::lock_guard lock(g_menuLock);
        g_menu.light=light; g_menu.startup=startup;
        g_menu.anchorX=anchorX; g_menu.anchorY=anchorY; g_menu.dpi=dpi; g_menu.hover=-1;
    }
    if(!g_menu.threadId) {
        g_menu.ready=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        HANDLE thread=CreateThread(nullptr,0,OwnedMenuThread,nullptr,0,&g_menu.threadId);
        if(thread) CloseHandle(thread);
        if(g_menu.ready) WaitForSingleObject(g_menu.ready,3000);
    }
    if(g_menu.hwnd) PostMessageW(g_menu.hwnd,WM_APP+1,0,0);
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
    if(position==3) {v->host.Width(56);v->host.HorizontalAlignment(HorizontalAlignment::Left);}
    v->host.Background(SolidColorBrush(Colors::Transparent()));
    v->pill=Border(); v->pill.Width(52); v->pill.Height(28); v->pill.CornerRadius({10,10,10,10});
    v->pill.Padding({4,0,4,0}); v->pill.BorderThickness({1,1,1,1});
    v->pill.HorizontalAlignment(HorizontalAlignment::Center); v->pill.VerticalAlignment(VerticalAlignment::Center);
    StackPanel number; number.Orientation(Orientation::Horizontal); number.HorizontalAlignment(HorizontalAlignment::Center); number.VerticalAlignment(VerticalAlignment::Center);
    // At rest the readout is bare text (Text face, quiet weight, no
    // background) but one step larger than the clock for legibility; hovering
    // wakes the glass pill and the data weight.
    v->text=TextBlock(); v->text.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->text.FontSize(15.0); v->text.FontWeight(Windows::UI::Text::FontWeights::Normal()); v->text.VerticalAlignment(VerticalAlignment::Center);
    v->suffix=TextBlock(); v->suffix.Text(L"%"); v->suffix.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->suffix.FontSize(11.5); v->suffix.Margin({2,2,0,0}); v->suffix.VerticalAlignment(VerticalAlignment::Center); // opacity is theme-dependent, set in Update (§4.1)
    number.Children().Append(v->text); number.Children().Append(v->suffix); v->pill.Child(number);
    // Charge pulse (design doc 4.4): a ring on the pill's own 52x28 outline that
    // only exists while charging, breathing 0.30 <-> 0.95 opacity on a 2.4 s
    // cycle. The numeral keeps its geometry and the ring ignores the pointer, so
    // hover glass and the tooltip still own the pill.
    v->glowRing=Border();
    v->glowRing.Width(52); v->glowRing.Height(28);
    v->glowRing.CornerRadius({10,10,10,10});
    v->glowRing.BorderThickness({1,1,1,1});
    v->glowRing.Background(nullptr);
    v->glowRing.Opacity(0.0);
    v->glowRing.IsHitTestVisible(false);
    v->glowRing.Visibility(Visibility::Collapsed);
    {
        DoubleAnimation fade;
        fade.From(0.30); fade.To(0.95);
        Duration cycle; cycle.TimeSpan=Windows::Foundation::TimeSpan{std::chrono::milliseconds(1200)}; cycle.Type=DurationType::TimeSpan;
        fade.Duration(cycle);
        fade.AutoReverse(true);
        RepeatBehavior forever; forever.Type=RepeatBehaviorType::Forever;
        fade.RepeatBehavior(forever);
        Storyboard::SetTarget(fade,v->glowRing);
        Storyboard::SetTargetProperty(fade,L"Opacity");
        v->glowStory=Storyboard();
        v->glowStory.Children().Append(fade);
    }
    Grid pillStack; pillStack.Width(52); pillStack.Height(28);
    pillStack.HorizontalAlignment(HorizontalAlignment::Center); pillStack.VerticalAlignment(VerticalAlignment::Center);
    pillStack.Children().Append(v->glowRing); pillStack.Children().Append(v->pill);
    v->host.Child(pillStack);
    StackPanel card; card.Width(240); card.Padding({20,20,20,20});
    // ① Identity band (§2.2): 16 DIP halo + 8 DIP core at the content origin,
    // device name 6 DIP to the right of the core. The halo overhangs 4 DIP
    // into the card padding, so the dot column is 14 wide with a -4 margin.
    v->tooltipName=TextBlock(); v->tooltipName.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipName.FontSize(13.0); v->tooltipName.FontWeight(Windows::UI::Text::FontWeights::Medium()); v->tooltipName.VerticalAlignment(VerticalAlignment::Center); v->tooltipName.TextTrimming(TextTrimming::CharacterEllipsis);
    Grid nameRow; nameRow.Margin({0,0,0,8});
    ColumnDefinition dotColumn; dotColumn.Width({14,GridUnitType::Pixel});
    ColumnDefinition nameColumn; nameColumn.Width({1,GridUnitType::Star});
    nameRow.ColumnDefinitions().Append(dotColumn); nameRow.ColumnDefinitions().Append(nameColumn);
    Grid dotHost; dotHost.Width(16); dotHost.Height(16); dotHost.Margin({-4,0,0,0}); dotHost.VerticalAlignment(VerticalAlignment::Center);
    v->nameHalo=Windows::UI::Xaml::Shapes::Ellipse(); v->nameHalo.Width(16); v->nameHalo.Height(16);
    v->nameDot=Windows::UI::Xaml::Shapes::Ellipse(); v->nameDot.Width(8); v->nameDot.Height(8);
    dotHost.Children().Append(v->nameHalo); dotHost.Children().Append(v->nameDot);
    Grid::SetColumn(dotHost,0); Grid::SetColumn(v->tooltipName,1);
    nameRow.Children().Append(dotHost); nameRow.Children().Append(v->tooltipName);
    v->tooltipValue=TextBlock(); v->tooltipValue.FontFamily(FontFamily(L"Segoe UI Variable Display")); v->tooltipValue.FontSize(40.0); v->tooltipValue.FontWeight(Windows::UI::Text::FontWeights::SemiBold()); v->tooltipValue.VerticalAlignment(VerticalAlignment::Center);
    // Editorial percent: the numeral stays threshold-colored, the unit recedes.
    StackPanel valueRow; valueRow.Orientation(Orientation::Horizontal);
    v->valueUnit=TextBlock(); v->valueUnit.Text(L"%"); v->valueUnit.FontFamily(FontFamily(L"Segoe UI Variable Display")); v->valueUnit.FontSize(15.0); v->valueUnit.Margin({4,0,0,6}); v->valueUnit.VerticalAlignment(VerticalAlignment::Bottom); // baseline-hug the numeral; ink α205 set in Update
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
    v->tooltipConnection=TextBlock(); v->tooltipConnection.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipConnection.FontSize(11.0); v->tooltipConnection.VerticalAlignment(VerticalAlignment::Center); // ink α190 set in Update
    Grid statusLine; // ④ state left / transport right, one rail (§2.2)
    ColumnDefinition stateColumn; stateColumn.Width({1,GridUnitType::Auto});
    ColumnDefinition railColumn; railColumn.Width({1,GridUnitType::Star});
    statusLine.ColumnDefinitions().Append(stateColumn); statusLine.ColumnDefinitions().Append(railColumn);
    Grid::SetColumn(statusRow,0); Grid::SetColumn(v->tooltipConnection,1);
    statusLine.Children().Append(statusRow);
    // The transport text MUST be in the visual tree: Update() writes to it every
    // dispatch, and mutating an unparented WinRT object long-term corrupts the
    // heap (the overnight 0xC0000374 pattern). The star column pins it right —
    // a Stretch spacer inside a horizontal StackPanel never expanded (dead rail).
    statusLine.Children().Append(v->tooltipConnection);
    v->statusLine=statusLine; v->statusRailText=v->tooltipConnection;
    // Signature scale: in-slot segment marks at the REAL 20%/10% thresholds.
    // Neutral takes the card material so the track reads as cut; crossing
    // lights Yellow/Red. Proud ticks read as scratches (user redline 11-16).
    v->tooltipTrack=Grid(); v->tooltipTrack.Height(14); v->tooltipTrack.Margin({0,12,0,10});
    v->trackBackground=Border(); v->trackBackground.Height(6); v->trackBackground.CornerRadius({3,3,3,3}); v->trackBackground.VerticalAlignment(VerticalAlignment::Center);
    v->tooltipFill=Border(); v->tooltipFill.Height(6); v->tooltipFill.HorizontalAlignment(HorizontalAlignment::Left); v->tooltipFill.CornerRadius({3,3,3,3}); v->tooltipFill.VerticalAlignment(VerticalAlignment::Center);
    // Margins are grid-relative: the content column already starts at the card
    // padding, so 20% = 40-1 and 10% = 20-1 (the doc's 59.25/39.25 are
    // card-relative and would double-count the 20 DIP padding).
    v->tickHigh=Windows::UI::Xaml::Shapes::Rectangle(); v->tickHigh.Width(2); v->tickHigh.Height(6); v->tickHigh.VerticalAlignment(VerticalAlignment::Center); v->tickHigh.HorizontalAlignment(HorizontalAlignment::Left); v->tickHigh.Margin({39,0,0,0});
    v->tickCritical=Windows::UI::Xaml::Shapes::Rectangle(); v->tickCritical.Width(2); v->tickCritical.Height(6); v->tickCritical.VerticalAlignment(VerticalAlignment::Center); v->tickCritical.HorizontalAlignment(HorizontalAlignment::Left); v->tickCritical.Margin({19,0,0,0});
    v->tooltipTrack.Children().Append(v->trackBackground); v->tooltipTrack.Children().Append(v->tooltipFill);
    v->tooltipTrack.Children().Append(v->tickHigh); v->tooltipTrack.Children().Append(v->tickCritical);
    // One hairline between readout and state (§2.2): one cut is a partition.
    v->tooltipHairline=Border(); v->tooltipHairline.Height(1); v->tooltipHairline.Margin({0,0,0,10});
    // Companion mouse summary fed by tooltip line 1; hidden for single-mouse setups.
    v->tooltipOther=TextBlock(); v->tooltipOther.FontFamily(FontFamily(L"Segoe UI Variable Text")); v->tooltipOther.FontSize(11.5); v->tooltipOther.Margin({0,8,0,0});
    // The companion line is longer than the card is wide ("… · Bolt 接收器");
    // without a width cap and wrapping it ran past the card edge and got cut
    // mid-word. Wrap inside the 200 DIP content column instead.
    v->tooltipOther.TextWrapping(TextWrapping::Wrap); v->tooltipOther.MaxWidth(200); v->tooltipOther.Visibility(Visibility::Collapsed); // ink α225 set in Update
    card.Children().Append(nameRow); card.Children().Append(valueRow); card.Children().Append(v->tooltipTrack); card.Children().Append(v->tooltipHairline); card.Children().Append(statusLine); card.Children().Append(v->tooltipOther);
    v->tooltip=ToolTip(); v->tooltip.Padding({0,0,0,0}); v->tooltip.CornerRadius({12,12,12,12}); v->tooltip.Content(card); ToolTipService::SetToolTip(v->pill,v->tooltip); // pill only: hovering the empty slot corners must not open the card
    // Right-click opens the owned layered menu (MenuWindow block above): the
    // stock MenuFlyout cannot be reshaped and a XAML Flyout above the taskbar
    // never receives pointer input, so this surface is drawn and hit-tested here.
    v->menuOpenToken=v->host.RightTapped([](Windows::Foundation::IInspectable const&,Windows::UI::Xaml::Input::RightTappedRoutedEventArgs const& e){
        if(!g_view) return;
        auto& vv=*g_view;
        HWND taskbar=TaskbarWindow();
        if(!taskbar) return;
        RECT tbr{}; GetWindowRect(taskbar,&tbr);
        int dpi=GetDpiForWindow(taskbar);
        if(dpi<=0) dpi=96;
        double scale=dpi/96.0;
        auto content=vv.host.XamlRoot().Content();
        auto point=vv.host.TransformToVisual(content).TransformPoint({0,0});
        int anchorX=(int)std::lround(tbr.left+(point.X+vv.host.ActualWidth()/2.0)*scale);
        int anchorY=(int)std::lround(tbr.top+point.Y*scale);
        bool light=vv.host.ActualTheme()==ElementTheme::Light;
        { wchar_t b[256]; _snwprintf_s(b,256,_TRUNCATE,L"menu-anchor dpi=%d scale=%.3f ax=%d ay=%d tbr=%d,%d pt=%.1f,%.1f",dpi,scale,anchorX,anchorY,(int)tbr.left,(int)tbr.top,point.X,point.Y); WorkerDiag(b); }
        ShowOwnedMenu(light,vv.startupState,anchorX,anchorY,dpi);
        e.Handled(true);
    });
    v->pill.ContextFlyout(nullptr);
    // Hover reveals the glass pill; left click stays inert on purpose so a
    // stray click on the readout can never trigger anything.
    v->hoverEnter=v->pill.PointerEntered([](auto const&,auto const&){ if(g_view){ g_view->hover=true; PaintPill(*g_view); } });
    v->hoverExit=v->pill.PointerExited([](auto const&,auto const&){ if(g_view){ g_view->hover=false; PaintPill(*g_view); } });
    if(grid && position==3) {
        // RootGrid owns the taskbar background and label columns. Inserting a
        // spacer column there pushed the background layer aside and left a black
        // hole exactly as wide as the spacer (anything transparent on top, e.g.
        // TrafficMonitor's panel, went black with it). A Canvas child adds no
        // layout slot at all, and unlike a column-spanning Grid child it keeps the
        // host's own 56x48 layout box - ToolTipService needs that box to anchor
        // the hover card above the pill.
        v->overlay=Canvas();
        v->overlay.HorizontalAlignment(HorizontalAlignment::Stretch);
        v->overlay.VerticalAlignment(VerticalAlignment::Stretch);
        Grid::SetColumn(v->overlay,0);
        Grid::SetColumnSpan(v->overlay,(int)std::max(1u,grid.ColumnDefinitions().Size()));
        Grid::SetRowSpan(v->overlay,(int)std::max(1u,grid.RowDefinitions().Size()));
        v->leftPad=ExternalLeftWidth()+kExternalGap;
        v->host.Width(56); v->host.Height(48);
        Canvas::SetLeft(v->host,v->leftPad);
        Canvas::SetTop(v->host,0.0);
        v->overlay.Children().Append(v->host);
        grid.Children().Append(v->overlay);
    } else if(grid) {
        // An empty ColumnDefinitions collection means one implicit star column.
        // Materialize it before inserting our Auto column; otherwise native
        // children shifted to column 1 would be clamped back into our column 0.
        if(grid.ColumnDefinitions().Size()==0) {
            v->implicitColumn=ColumnDefinition(); v->implicitColumn.Width({1,GridUnitType::Star});
            grid.ColumnDefinitions().Append(v->implicitColumn);
        }
        v->column=ColumnDefinition(); v->column.Width({1,GridUnitType::Auto});
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
    if(work.remove || work.snapshot.state==0) {
        wchar_t b[128];
        _snwprintf_s(b,128,_TRUNCATE,L"hidden st=%d pct=%d age=%llu",work.snapshot.state,work.snapshot.percent,(unsigned long long)(GetTickCount64()-work.snapshot.tick));
        work.health=b;
        Remove();return;
    }
    auto& s=work.snapshot;
    auto root=TaskbarRoot(TaskbarWindow());
    WorkerDiag(root?L"root-ok":L"root-null");
    if(!root) {NativeLog(L"Taskbar XamlRoot not ready");work.health=L"XamlRoot unavailable";return;}
    if(g_view && (g_view->position!=s.position || g_view->host.XamlRoot()!=root)) Remove();
    if(!g_view) {
        WorkerDiag(L"insert-before");
        bool inserted=Insert(root.Content().as<FrameworkElement>(),s.position);
        WorkerDiag(inserted?L"insert-ok":L"insert-fail");
        if(!inserted) {work.health=L"placement unavailable";return;}
    }
    auto& v=*g_view;
    if(s.position==3) {
        double leftPad=ExternalLeftWidth()+kExternalGap;
        if(leftPad<0) leftPad=0;
        // TrafficMonitor resizes with its content; only touch XAML when the gap moved.
        if(v.leftPad<0 || leftPad>v.leftPad+0.5 || leftPad<v.leftPad-0.5) {
            v.leftPad=leftPad; Canvas::SetLeft(v.host,leftPad);
        }
        if(v.overlay) {
            double barHeight=v.overlay.ActualHeight();
            if(barHeight>0 && v.host.Height()!=barHeight) v.host.Height(barHeight);
        }
    }
    v.text.Text(std::to_wstring(s.percent));
    bool actualLight=v.host.ActualTheme()==ElementTheme::Light;
    // Probe-only override; production always publishes 0 so the host theme wins.
    bool light=s.themeOverride==1 ? true : s.themeOverride==2 ? false : actualLight;
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
    Color color=warnRed ? CatCritInk(light) : warnYellow ? CatWarnInk(light) : CatText(light);
    Color fillTop=WithAlphaA(pillColor,topA);
    Color fillBottom=WithAlphaA(pillColor,botA);
    Color stroke=WithAlphaA(pillColor,strA);
    uint64_t sig=Signature(s,light);
    bool restyle=!v.styled||sig!=v.styleSig;
    if(restyle) { // N3: nothing changed => touch no ToolTip property, keep it open
    v.text.Foreground(SolidColorBrush(color)); v.suffix.Foreground(SolidColorBrush(color));
    v.suffix.Opacity(light?0.80:0.72); // §4.1: rest has no glass, opacity carries the unit's recession
    // Layered glass pill: dense at the top edge, fading toward the bottom.
    v.baseTop=fillTop; v.baseBottom=fillBottom; v.baseStroke=stroke;
    PaintPill(v);
    std::wstring detail(s.tooltip);
    size_t nl=detail.find(L'\n');
    v.tooltipName.Text(nl==std::wstring::npos ? detail : detail.substr(0,nl));
    if(nl!=std::wstring::npos && nl+1<detail.size()) {
        v.tooltipOther.Visibility(Visibility::Visible);
        {   // The card column is 200 DIP wide; "Bolt 接收器" overflowed it and left
            // a lone 器 on the second row. Shorten the transport name for the card
            // only - the tooltip keeps the full wording.
            std::wstring other=detail.substr(nl+1);
            std::wstring const longName=L"Bolt 接收器";
            size_t at=other.find(longName);
            if(at!=std::wstring::npos) other.replace(at,longName.size(),L"Bolt");
            v.tooltipOther.Text(other);
        }
    } else v.tooltipOther.Visibility(Visibility::Collapsed);

    v.tooltipValue.Text(std::to_wstring(s.percent)); // numeral only: the % lives in valueUnit (F1)
    // §2.2: the CARD numeral carries identity when healthy (the pill numeral
    // stays Text at rest — §4.1 clock isomorphism, probe-asserted). Stale
    // readings decay the whole numeral to α190 (§3.6).
    Color valueColor=warnRed ? CatCritInk(light) : warnYellow ? CatWarnInk(light) : identity;
    if(s.state==2) valueColor=WithAlphaA(valueColor,190);
    v.tooltipValue.Foreground(SolidColorBrush(valueColor));
    Color muted=CatSubtext(light);
    v.nameHalo.Fill(SolidColorBrush(WithAlphaA(identity,36)));
    v.nameDot.Fill(SolidColorBrush(identity));
    v.tooltipHairline.Background(SolidColorBrush(WithAlphaA(CatBorder(light),115)));
    v.statusRailText.TextAlignment(TextAlignment::Right);
    // Threshold ticks: real 20%/10% positions; neutral = card material (light
    // flat Surface, dark Base sits on the Surface→Scrim gradient at the track
    // row) so the mark reads as a cut, crossed = full Yellow/Red (§2.2).
    Color neutralTick=light ? CatSurface(light) : CatBase(light);
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
    if(s.state==2) { v.tooltipStatus.Foreground(SolidColorBrush(CatWarnInk(light))); }
    else if(!gaming || s.charging==0 || s.charging==3) {
        v.tooltipStatus.Foreground(SolidColorBrush(WithAlphaA(CatText(light),s.charging==3?235:220)));
        if(s.charging==3 && gaming) { /* full: no bolt */ }
        else if(!gaming) { v.tooltipStatus.Foreground(SolidColorBrush(WithAlphaA(CatSubtext(light),220))); }
    } else if(s.charging==4) {
        charge=L"充电异常"; v.tooltipStatus.Foreground(SolidColorBrush(CatCritInk(light)));
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
    v.tooltipOther.Foreground(SolidColorBrush(staleRow ? WithAlphaA(CatWarnInk(light),210) : WithAlphaA(muted,225)));
    v.trackBackground.Background(SolidColorBrush(CatBorder(light)));
    v.tooltipFill.Background(SolidColorBrush(s.percent<=20 ? pillColor : identity));
    v.tooltipFill.Width(200.0*s.percent/100.0);
    Automation::AutomationProperties::SetName(v.host,hstring(s.tooltip));
    v.startupState=s.startup;
    // Charge state: 1..3 charging, 4 fault, 0 unknown (design doc 4.4).
    bool chargingNow=s.charging>=1 && s.charging<=3;
    v.glowRing.BorderBrush(SolidColorBrush(WithAlphaA(identity,240)));
    if(chargingNow) {
        if(!v.glowOn) { v.glowOn=true; v.glowRing.Visibility(Visibility::Visible); v.glowStory.Begin(); }
    } else {
        if(v.glowOn) { v.glowOn=false; v.glowStory.Stop(); v.glowRing.Visibility(Visibility::Collapsed); }
    }
    v.host.UpdateLayout();
    v.styled=true; v.styleSig=sig;
    } // restyle
    auto point=v.host.TransformToVisual(root.Content().as<UIElement>()).TransformPoint({0,0});
    work.health=L"visible position="+std::to_wstring(s.position)+L" percent="+std::to_wstring(s.percent)+L" x="+std::to_wstring(point.X)+L" y="+std::to_wstring(point.Y)+L" width="+std::to_wstring(v.host.ActualWidth())+L" height="+std::to_wstring(v.host.ActualHeight())+L" parent="+std::wstring(get_class_name(v.parent));
    work.health+=L" style=catppuccin pillWidth="+std::to_wstring(v.pill.ActualWidth())+L" pillHeight="+std::to_wstring(v.pill.ActualHeight())+L" numberWidth="+std::to_wstring(v.text.ActualWidth())+L" suffixWidth="+std::to_wstring(v.suffix.ActualWidth());
    work.health+=L" state="+std::to_wstring(s.state)+L" textRGB="+std::to_wstring(color.R)+L","+std::to_wstring(color.G)+L","+std::to_wstring(color.B);
    auto solidRGB=[](Windows::UI::Xaml::Shapes::Rectangle const& r){ auto b=r.Fill().as<SolidColorBrush>(); return std::to_wstring(b.Color().R)+L","+std::to_wstring(b.Color().G)+L","+std::to_wstring(b.Color().B); };
    work.health+=L" tickHigh="+solidRGB(v.tickHigh)+L" tickCritical="+solidRGB(v.tickCritical)+L" other="+(v.tooltipOther.Visibility()==Visibility::Visible?L"1":L"0")+L" kind="+(gaming?L"gaming":L"office");
    // §4.3 diagnostics: glass top stop and unit opacity, for probe assertions.
    wchar_t unitBuf[16]; _snwprintf_s(unitBuf,16,_TRUNCATE,L"%.2f",light?0.80:0.72);
    work.health+=L" pillTopARGB="+std::to_wstring(v.baseTop.A)+L","+std::to_wstring(v.baseTop.R)+L","+std::to_wstring(v.baseTop.G)+L","+std::to_wstring(v.baseTop.B)+L" unitOpacity="+unitBuf;
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
    static std::atomic<bool> failureLogged{false}; // log only the first failure; steady failure must not flood the file
    // Do NOT cache the mapping: after an app restart or Windows update the
    // publisher may recreate the named object, and a cached view would keep
    // reading a stale (hours-old) snapshot forever. Open/mapping/close each
    // dispatch - cheap (500ms cadence) and always bound to the live object.
    HANDLE map=OpenFileMappingW(FILE_MAP_READ,FALSE,L"Local\\MouseBatteryTaskbar.v3");
    if(!map) {if(!failureLogged.exchange(true)) WorkerDiag(L"read-map-open-failed");return result;}
    auto data=(Snapshot const*)MapViewOfFile(map,FILE_MAP_READ,0,0,sizeof(Snapshot));
    if(!data) {CloseHandle(map);if(!failureLogged.exchange(true)) WorkerDiag(L"read-view-map-failed");return result;}
    uint32_t before=((Snapshot volatile const*)data)->sequence;
    if(before&1) {UnmapViewOfFile(data);CloseHandle(map);if(!failureLogged.exchange(true)) WorkerDiag(L"read-seq-odd");return result;}
    MemoryBarrier(); std::memcpy(&result,data,sizeof(result)); MemoryBarrier();
    uint32_t after=((Snapshot volatile const*)data)->sequence;
    UnmapViewOfFile(data);CloseHandle(map);
    if(before!=after || result.magic!=0x3254424D || result.percent<0 || result.percent>100 || result.state<0 || result.state>2 || result.position<0 || result.position>3 || GetTickCount64()-result.tick>=15000) {
        result.state=0;
        static std::atomic<long long> lastDump{0}; // dump what the worker actually sees, throttled
        auto now=GetTickCount64();
        if(now-lastDump.exchange((long long)now)>20000) {
            wchar_t b[192];
            _snwprintf_s(b,192,_TRUNCATE,L"read-check-fail ba=%u/%u mg=%u st=%d pct=%d tick=%llu",(unsigned)before,(unsigned)after,(unsigned)result.magic,(int)result.state,(int)result.percent,(unsigned long long)result.tick);
            WorkerDiag(b);
        }
    }
    result.tooltip[215]=0;
    if(result.themeOverride<0 || result.themeOverride>2) result.themeOverride=0;
    return result;
}
}
