#include <windows.h>
#include <cstdint>
#include <cstdarg>
#include <cstdio>
#include <string>
#include <mutex>
#include <tlhelp32.h>

namespace {
constexpr wchar_t HealthName[]=L"Local\\MouseBatteryTaskbar.NativeHealth.v3";
struct Health {
    uint32_t magic; volatile LONG sequence; uint32_t state,owner;
    uint64_t tick;
    wchar_t init[256],layout[1024];
};
static_assert(sizeof(Health)==2584);
HMODULE g_instance=nullptr;
HANDLE g_healthMap=nullptr;
Health* g_health=nullptr;
std::mutex g_lifecycle;
HANDLE g_ownerProcess=nullptr;
bool g_started=false;
void NativeLog(wchar_t const* format,...) {
    wchar_t message[1024]; va_list args; va_start(args,format);
    _vsnwprintf_s(message,1024,_TRUNCATE,format,args); va_end(args);
    OutputDebugStringW(message);
}
bool OpenHealth() {
    if(g_health) return true;
    g_healthMap=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,4096,HealthName);
    if(!g_healthMap) return false;
    g_health=(Health*)MapViewOfFile(g_healthMap,FILE_MAP_ALL_ACCESS,0,0,4096);
    if(!g_health) {CloseHandle(g_healthMap);g_healthMap=nullptr;return false;}
    return true;
}
void CloseHealth() {
    if(g_health) {UnmapViewOfFile(g_health);g_health=nullptr;}
    if(g_healthMap) {CloseHandle(g_healthMap);g_healthMap=nullptr;}
}
void SetHealth(wchar_t const* key,wchar_t const* value) {
    if(!OpenHealth()) {NativeLog(L"Native health mapping failed: %lu",GetLastError());return;}
    InterlockedIncrement(&g_health->sequence); MemoryBarrier();
    g_health->magic=0x3348424D; g_health->tick=GetTickCount64();
    if(wcscmp(key,L"InitState")==0) {
        wcsncpy_s(g_health->init,value,_TRUNCATE);
        g_health->state=wcscmp(value,L"initialized")==0 ? 2 : wcscmp(value,L"stopped")==0 ? 3 : wcscmp(value,L"starting")==0 ? 1 : 4;
    } else wcsncpy_s(g_health->layout,value,_TRUNCATE);
    MemoryBarrier(); InterlockedIncrement(&g_health->sequence);
}
}

#include "TaskbarView.h"

namespace {
// Exact local taskbar.dll profiles (SSOT). The EXE no longer verifies the
// SHA-256 itself: NativeProbe exposes the supported set and the current
// fingerprint to managed code. Profile rows keyed by PE TimeDateStamp.
//  - 1989904251: 26100.9711 (pre-KB5127936)
//  - 1635664427: 26100.9818 (post-KB5127936/KB5132402, 2026-11-04)
// RVAs located via MS public PDB (taskbar.pdb 8645A6F8C08FC8A18DACD4B77CF1E2EE1):
//   FrameHeight = TaskbarHost::FrameHeight @ 0xB10A4 (element offset 0x10)
//   Decref      = std::_Ref_count_base::_Decref    @ 0x290B0
//   GetHost     = TaskListWnd::GetHost             @ 0xF9C40
//   siteVtable  = TaskListWnd site vftable         @ 0x1CFA30 (thunk imm 0x40/0x50/0x50)
bool InitializeProfile() {
    auto base=(BYTE*)LoadLibraryExW(L"taskbar.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!base) {SetHealth(L"InitState",L"taskbar.dll unavailable");return false;}
    auto dos=(IMAGE_DOS_HEADER*)base;
    auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    bool match=nt->FileHeader.TimeDateStamp==1635664427 && nt->OptionalHeader.SizeOfImage==3252224 && nt->OptionalHeader.CheckSum==3342558;
    if(!match) {FreeLibrary((HMODULE)base);SetHealth(L"InitState",L"unsupported taskbar.dll profile");return false;}
    g_siteVtable=base+1899056;
    g_getHost=(GetHostFn)(base+1023040);
    g_frameHeight=(FrameHeightFn)(base+725156);
    g_decref=(DecrefFn)(base+168112);
    auto code=(BYTE const*)g_frameHeight;
    if(code[0]!=0x48 || code[1]!=0x83 || code[2]!=0xEC || code[4]!=0x48 || code[5]!=0x83 || code[6]!=0xC1 || code[7]>0x7F) {
        FreeLibrary((HMODULE)base);SetHealth(L"InitState",L"unsupported FrameHeight prologue");return false;
    }
    g_elementOffset=code[7];
    // taskbar.dll is already held by Explorer. Balance our lookup reference.
    FreeLibrary((HMODULE)base);
    return true;
}

DWORD WINAPI NativeWorker(void* parameter) {
    HMODULE self=(HMODULE)parameter;
    HANDLE waits[]={g_stop,g_ownerProcess};
    while(WaitForMultipleObjects(2,waits,FALSE,500)==WAIT_TIMEOUT) {
        Work work; work.snapshot=Read();
        if(!Dispatch(work)) work.health=L"taskbar UI dispatch failed";
        SetHealth(L"LastLayout",work.health.c_str());
    }
    Work cleanup; cleanup.remove=true;
    if(!Dispatch(cleanup) && g_view) {
        // The old taskbar tree is already gone; do not release XAML on a worker.
        NativeLog(L"Taskbar unavailable while stopping the native view");g_view.release();
    }
    {
        std::lock_guard lock(g_lifecycle);
        if(g_data) {UnmapViewOfFile(g_data);g_data=nullptr;}
        if(g_mapping) {CloseHandle(g_mapping);g_mapping=nullptr;}
        CloseHandle(g_stop);g_stop=nullptr;
        CloseHandle(g_ownerProcess);g_ownerProcess=nullptr;
        SetHealth(L"LastLayout",L"hidden");SetHealth(L"InitState",L"stopped");
        CloseHealth();g_started=false;
    }
    FreeLibraryAndExitThread(self,0);
}

void StartInsideExplorer(DWORD owner) {
    std::lock_guard lock(g_lifecycle);
    if(g_started) return;
    if(!OpenHealth()) return;
    g_health->owner=owner;SetHealth(L"InitState",L"starting");
    if(!InitializeProfile()) return;
    g_ownerProcess=OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,owner);
    if(!g_ownerProcess) {SetHealth(L"InitState",L"cannot open owner process");return;}
    wchar_t path[32768];DWORD length=ARRAYSIZE(path);
    if(!QueryFullProcessImageNameW(g_ownerProcess,0,path,&length)) {
        CloseHandle(g_ownerProcess);g_ownerProcess=nullptr;SetHealth(L"InitState",L"cannot resolve application path");return;
    }
    g_reader.assign(path,length);
    g_message=RegisterWindowMessageW(L"MouseBatteryTaskbar.UI.v3");
    std::wstring stopName=L"Local\\MouseBatteryTaskbar.Stop.v3."+std::to_wstring(owner);
    g_stop=CreateEventW(nullptr,TRUE,FALSE,stopName.c_str());
    if(!g_stop) {CloseHandle(g_ownerProcess);g_ownerProcess=nullptr;SetHealth(L"InitState",L"cannot create stop event");return;}
    HMODULE self;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,(PCWSTR)&NativeWorker,&self)) {
        CloseHandle(g_stop);g_stop=nullptr;CloseHandle(g_ownerProcess);g_ownerProcess=nullptr;SetHealth(L"InitState",L"cannot retain native component");return;
    }
    g_started=true;
    SetHealth(L"InitState",L"initialized");
    HANDLE thread=CreateThread(nullptr,0,NativeWorker,self,0,nullptr);
    if(!thread) {
        g_started=false;CloseHandle(g_stop);g_stop=nullptr;CloseHandle(g_ownerProcess);g_ownerProcess=nullptr;
        SetHealth(L"InitState",L"cannot create view worker");FreeLibrary(self);return;
    }
    CloseHandle(thread);
}



HMODULE RemoteModule(DWORD pid,std::wstring const& value,bool fullPath) {
    winrt::handle snapshot{CreateToolhelp32Snapshot(TH32CS_SNAPMODULE|TH32CS_SNAPMODULE32,pid)};
    if(snapshot.get()==INVALID_HANDLE_VALUE) return nullptr;
    MODULEENTRY32W module{sizeof(module)};
    if(!Module32FirstW(snapshot.get(),&module)) return nullptr;
    do {if(_wcsicmp(fullPath ? module.szExePath : module.szModule,value.c_str())==0) return module.hModule;}
    while(Module32NextW(snapshot.get(),&module));
    return nullptr;
}
}
// Single source of truth for taskbar.dll compatibility. Managed code asks
// this instead of keeping its own hash table; fills fingerprint (hex, no
// dashes) for the tray-fallback message when unsupported.
extern "C" __declspec(dllexport) DWORD WINAPI NativeProbeTaskbar(wchar_t* fingerprintBuffer,DWORD bufferChars) {
    BYTE probeBase=0;
    auto base=(BYTE*)LoadLibraryExW(L"taskbar.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!base) return ERROR_NOT_SUPPORTED;
    auto dos=(IMAGE_DOS_HEADER*)base;
    auto nt=(IMAGE_NT_HEADERS64*)(base+dos->e_lfanew);
    DWORD stamp=nt->FileHeader.TimeDateStamp;
    DWORD image=nt->OptionalHeader.SizeOfImage;
    DWORD check=nt->OptionalHeader.CheckSum;
    FreeLibrary((HMODULE)base);
    bool match=(stamp==1635664427 && image==3252224 && check==3342558);
    if(match && !InitializeProfile()) match=false; // RVAs/prologue must also agree
    if(match) return ERROR_SUCCESS;
    if(fingerprintBuffer && bufferChars>=8) {
        // return the PE timestamp-derived short fingerprint (stable per build)
        _snwprintf_s(fingerprintBuffer,bufferChars,_TRUNCATE,L"%08X-%08X-%08X",stamp,image,check);
    }
    return ERROR_NOT_SUPPORTED;
}
extern "C" __declspec(dllexport) DWORD WINAPI NativeStart(void* parameter) {
    DWORD owner=(DWORD)(uintptr_t)parameter;
    HMODULE self;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,(PCWSTR)&NativeStart,&self)) return GetLastError();
    DWORD result=ERROR_SUCCESS;
    auto begin=GetTickCount64();
    for(;;) {
        bool stopped,same;
        {std::lock_guard lock(g_lifecycle);stopped=!g_started;same=g_started && g_health && g_health->owner==owner;if(!stopped && !same && g_stop) SetEvent(g_stop);}
        if(stopped || same) break;
        if(GetTickCount64()-begin>6000) {result=ERROR_TIMEOUT;break;}
        Sleep(10);
    }
    if(result==ERROR_SUCCESS) {
        StartInsideExplorer(owner);
        if(!g_started) result=ERROR_DLL_INIT_FAILED;
    }
    // Balance LoadLibrary's reference. The worker owns a separate reference,
    // and this entry thread retains its own until FreeLibraryAndExitThread.
    FreeLibrary(g_instance);
    FreeLibraryAndExitThread(self,result);
}
// Attach diagnostics: one line per step with Win32 error, written next to the
// EXE (NativeAttach runs in the EXE process, not in Explorer). The tray
// fallback masks the real failure point, so this file is the only truth.
static void AttachDiag(const char* step,DWORD error,const wchar_t* detail=nullptr) {
    wchar_t exe[MAX_PATH];GetModuleFileNameW(nullptr,exe,MAX_PATH);
    std::wstring path(exe);
    size_t cut=path.find_last_of(L'\\');
    std::wstring dir=cut==std::wstring::npos?L".":path.substr(0,cut);
    std::wstring file=dir+L"\\data\\attach-diag.txt";
    CreateDirectoryW((dir+L"\\data").c_str(),nullptr);
    HANDLE f=CreateFileW(file.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(f==INVALID_HANDLE_VALUE) return;
    LARGE_INTEGER zero{};SetFilePointer(f,0,nullptr,FILE_END);
    char line[512];
    int n=_snprintf_s(line,sizeof(line),_TRUNCATE,"[%llu] %s err=%lu",(unsigned long long)GetTickCount64(),step,error);
    if(detail) {int m=_snprintf_s(line+n,sizeof(line)-n,_TRUNCATE," | %ls",detail);if(m>0)n+=m;}
    line[n<0?0:n]=0;
    static const char CRLF[2]={'\r','\n'};
    DWORD wrote=0;WriteFile(f,line,(DWORD)strlen(line),&wrote,nullptr);
    WriteFile(f,CRLF,2,&wrote,nullptr);
    CloseHandle(f);
}
extern "C" __declspec(dllexport) DWORD WINAPI NativeAttach(DWORD owner) {
    AttachDiag("step:enter",0);
    HWND taskbar=FindWindowW(L"Shell_TrayWnd",nullptr);DWORD pid=0;
    if(!taskbar) {AttachDiag("step:find-taskbar",ERROR_NOT_FOUND);return ERROR_NOT_FOUND;}GetWindowThreadProcessId(taskbar,&pid);
    AttachDiag("step:taskbar-found",0);
    wchar_t path[32768];if(!GetModuleFileNameW(g_instance,path,ARRAYSIZE(path))) {DWORD e=GetLastError();AttachDiag("step:get-self-path",e);return e;}
    AttachDiag("step:self-path",0,path);
    winrt::handle process{OpenProcess(PROCESS_CREATE_THREAD|PROCESS_QUERY_INFORMATION|PROCESS_VM_OPERATION|PROCESS_VM_READ|PROCESS_VM_WRITE,FALSE,pid)};
    if(!process) {DWORD e=GetLastError();AttachDiag("step:open-explorer",e);return e;}
    AttachDiag("step:open-explorer",0);
    HMODULE kernel=GetModuleHandleW(L"kernel32.dll"), loaderOwner=nullptr;
    auto loader=GetProcAddress(kernel,"LoadLibraryW");
    if(!loader || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(PCWSTR)loader,&loaderOwner)) return GetLastError();
    wchar_t ownerPath[MAX_PATH];if(!GetModuleFileNameW(loaderOwner,ownerPath,ARRAYSIZE(ownerPath))) return GetLastError();
    std::wstring ownerName(ownerPath);ownerName=ownerName.substr(ownerName.find_last_of(L'\\')+1);
    HMODULE remoteLoaderOwner=RemoteModule(pid,ownerName,false);
    if(!remoteLoaderOwner) {AttachDiag("step:find-kernel32-in-explorer",ERROR_MOD_NOT_FOUND);return ERROR_MOD_NOT_FOUND;}
    AttachDiag("step:find-kernel32-in-explorer",0);
    auto remoteLoader=(LPTHREAD_START_ROUTINE)((BYTE*)remoteLoaderOwner+((BYTE*)loader-(BYTE*)loaderOwner));
    SIZE_T bytes=(wcslen(path)+1)*sizeof(wchar_t);
    void* argument=VirtualAllocEx(process.get(),nullptr,bytes,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
    if(!argument) {DWORD e=GetLastError();AttachDiag("step:virtualalloc",e);return e;}
    AttachDiag("step:virtualalloc",0);
    SIZE_T written=0;
    if(!WriteProcessMemory(process.get(),argument,path,bytes,&written) || written!=bytes) {
        DWORD e=GetLastError();AttachDiag("step:write-memory",e);VirtualFreeEx(process.get(),argument,0,MEM_RELEASE);return e ? e : ERROR_WRITE_FAULT;
    }
    AttachDiag("step:write-memory",0);
    winrt::handle loadThread{CreateRemoteThread(process.get(),nullptr,0,remoteLoader,argument,0,nullptr)};
    if(!loadThread) {DWORD e=GetLastError();AttachDiag("step:create-remote-thread",e);VirtualFreeEx(process.get(),argument,0,MEM_RELEASE);return e;}
    AttachDiag("step:create-remote-thread",0);
    DWORD wait=WaitForSingleObject(loadThread.get(),10000);
    if(wait!=WAIT_OBJECT_0) {
        // Never free a path buffer that a pending loader may still be using.
        AttachDiag("step:loader-wait",wait==WAIT_TIMEOUT?ERROR_TIMEOUT:GetLastError());
        SetHealth(L"LastLayout",L"native loader pending; argument buffer retained");
        return wait==WAIT_TIMEOUT ? ERROR_TIMEOUT : GetLastError();
    }
    VirtualFreeEx(process.get(),argument,0,MEM_RELEASE);
    DWORD loaded=0;if(!GetExitCodeThread(loadThread.get(),&loaded)) {DWORD e=GetLastError();AttachDiag("step:loader-exit-code",e);return e;}
    AttachDiag("step:loader-exit-code",0,loaded?L"HMODULE!=0":L"HMODULE==0 (LoadLibraryW failed inside Explorer)");
    if(!loaded) {SetHealth(L"LastLayout",(L"LoadLibraryW returned zero; path="+std::wstring(path)+L" loader="+ownerName).c_str());return ERROR_DLL_INIT_FAILED;}
    HMODULE remote=RemoteModule(pid,path,true);
    if(!remote) {AttachDiag("step:confirm-module-in-explorer",ERROR_MOD_NOT_FOUND,path);return ERROR_MOD_NOT_FOUND;}
    AttachDiag("step:confirm-module-in-explorer",0);
    auto localStart=GetProcAddress(g_instance,"NativeStart");
    if(!localStart) return GetLastError();
    auto remoteStart=(LPTHREAD_START_ROUTINE)((BYTE*)remote+((BYTE*)localStart-(BYTE*)g_instance));
    winrt::handle startThread{CreateRemoteThread(process.get(),nullptr,0,remoteStart,(void*)(uintptr_t)owner,0,nullptr)};
    if(!startThread) {DWORD e=GetLastError();AttachDiag("step:start-thread",e);return e;}
    wait=WaitForSingleObject(startThread.get(),10000);
    if(wait!=WAIT_OBJECT_0) {DWORD e=wait==WAIT_TIMEOUT?ERROR_TIMEOUT:GetLastError();AttachDiag("step:start-wait",e);return e;}
    DWORD result=0;if(!GetExitCodeThread(startThread.get(),&result)) {DWORD e=GetLastError();AttachDiag("step:start-exit-code",e);return e;}
    AttachDiag("step:native-start-exit",result);
    return result;
}
extern "C" __declspec(dllexport) DWORD WINAPI NativeDetach() {
    auto name=L"Local\\MouseBatteryTaskbar.Stop.v3."+std::to_wstring(GetCurrentProcessId());
    HANDLE stop=OpenEventW(EVENT_MODIFY_STATE,FALSE,name.c_str());
    if(!stop) {CloseHealth();return ERROR_SUCCESS;}
    if(!OpenHealth()) {DWORD error=GetLastError();CloseHandle(stop);return error;}
    SetEvent(stop);CloseHandle(stop);
    auto start=GetTickCount64();
    while(g_health && g_health->state==2 && GetTickCount64()-start<6000) Sleep(10);
    DWORD result=g_health && g_health->state==2 ? ERROR_TIMEOUT : ERROR_SUCCESS;
    CloseHealth();return result;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,void*) {
    if(reason==DLL_PROCESS_ATTACH) g_instance=instance;
    return TRUE;
}
