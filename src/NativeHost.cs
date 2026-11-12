using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MouseBattery {
internal sealed class NativeHost : IDisposable {
    // SSOT: taskbar.dll compatibility lives entirely in the native component
    // (profile table in NativeHost.cpp). Managed code only asks the probe and
    // surfaces the fingerprint it returns. Never add hash constants here.
    const string HealthMap=@"Local\MouseBatteryTaskbar.NativeHealth.v4";
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint AttachFunction(uint owner);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint DetachFunction();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint ProbeFunction(IntPtr fingerprintBuffer,uint bufferChars);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,SetLastError=true)] static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string name,string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    readonly IntPtr library;
    string libraryPath; // kept for defensive re-loads before native calls
    readonly AttachFunction attach;
    readonly DetachFunction detach;
    readonly ProbeFunction probe;
    readonly Stopwatch timer=Stopwatch.StartNew();
    uint explorerPid;
    long nextCheck;
    bool attached;
    internal string Status="等待任务栏";
    internal uint StopResult;

    internal NativeHost() {
        byte[] bytes;
        using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MouseBattery.Native.dll"))
        using(var buffer=new MemoryStream()) {resource.CopyTo(buffer);bytes=buffer.ToArray();}
        string nativeHash;
        using(var sha=SHA256.Create()) nativeHash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
        string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"components",nativeHash);
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"MouseBattery.Native.dll");
        if(File.Exists(path)) {
            using(var sha=SHA256.Create()) using(var file=File.OpenRead(path)) {
                if(BitConverter.ToString(sha.ComputeHash(file)).Replace("-","")!=nativeHash) throw new IOException("应用内部组件校验失败："+path);
            }
        } else using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write)) file.Write(bytes,0,bytes.Length);
        libraryPath=path;
        library=LoadLibraryEx(path,IntPtr.Zero,0x100|0x800);
        if(library==IntPtr.Zero) throw new Win32Exception();
        IntPtr start=GetProcAddress(library,"NativeAttach"), stop=GetProcAddress(library,"NativeDetach"), probePtr=GetProcAddress(library,"NativeProbeTaskbar");
        if(start==IntPtr.Zero || stop==IntPtr.Zero || probePtr==IntPtr.Zero) {FreeLibrary(library);throw new EntryPointNotFoundException("Native component entry points unavailable");}
        attach=(AttachFunction)Marshal.GetDelegateForFunctionPointer(start,typeof(AttachFunction));
        detach=(DetachFunction)Marshal.GetDelegateForFunctionPointer(stop,typeof(DetachFunction));
        probe=(ProbeFunction)Marshal.GetDelegateForFunctionPointer(probePtr,typeof(ProbeFunction));
        // SSOT probe: the native component owns the profile table and answers
        // with ERROR_SUCCESS or ERROR_NOT_SUPPORTED plus a fingerprint string.
        var buf=Marshal.AllocCoTaskMem(128);
        try {
            uint r=probe(buf,128);
            if(r!=0) {
                string fp=Marshal.PtrToStringUni(buf);
                throw new NotSupportedException("当前 Windows 任务栏版本尚未适配。程序未修改任务栏；请保留系统版本和此指纹以便适配："+(string.IsNullOrEmpty(fp)?"未知":fp));
            }
        } finally { Marshal.FreeCoTaskMem(buf); }
    }

    // Reopen on EVERY check: a cached view can be bound to a stale named
    // object after Explorer/app restarts. Require the Explorer-side owner PID
    // and a fresh tick; otherwise treat the health as unknown and re-attach.
    private uint ReadHealth(out uint owner) {
        owner=0;
        try {
            using(var m=MemoryMappedFile.OpenExisting(HealthMap,MemoryMappedFileRights.Read))
            using(var acc=m.CreateViewAccessor(0,4096,MemoryMappedFileAccess.Read)) {
                int before=acc.ReadInt32(4);
                if((before&1)!=0) {Status="任务栏组件正在写入";return 0;}
                Thread.MemoryBarrier();
                uint magic=acc.ReadUInt32(0),state=acc.ReadUInt32(8),pid=acc.ReadUInt32(12);
                ulong tick=acc.ReadUInt64(16);
                byte[] init=new byte[512],layout=new byte[2048];
                acc.ReadArray(24,init,0,init.Length);acc.ReadArray(536,layout,0,layout.Length);
                Thread.MemoryBarrier();
                if(before!=acc.ReadInt32(4)) {Status="任务栏状态更新中";return 0;}
                if(magic!=0x3348424D) {Status="等待任务栏组件初始化";return 0;}
                if(GetTickCount64()-tick>10000) {Status="任务栏组件未响应";return 0;}
                owner=pid;
                Status=Encoding.Unicode.GetString(init).Split('\0')[0]+" | "+Encoding.Unicode.GetString(layout).Split('\0')[0];
                return state;
            }
        }
        catch(FileNotFoundException) {Status="等待任务栏组件";return 0;}
    }

    internal void Maintain() {
        if(timer.ElapsedMilliseconds<nextCheck) return;
        nextCheck=timer.ElapsedMilliseconds+2000;
        IntPtr taskbar=FindWindow("Shell_TrayWnd",null);
        if(taskbar==IntPtr.Zero) {Status="等待 Windows 任务栏恢复";return;}
        uint pid;GetWindowThreadProcessId(taskbar,out pid);
        uint owner;uint state=ReadHealth(out owner);
        if(!attached || explorerPid!=pid || state==3 || (owner!=0 && owner!=(uint)Process.GetCurrentProcess().Id)) {
            IntPtr pin=LoadLibraryEx(libraryPath,IntPtr.Zero,0x100|0x800); // +1 ref: guarantee mapped during the call
            uint error;
            try { error=attach((uint)Process.GetCurrentProcess().Id); }
            finally { if(pin!=IntPtr.Zero) FreeLibrary(pin); }
            if(error!=0) {ReadHealth(out owner);throw new Win32Exception((int)error,"无法连接 Windows 任务栏："+Status);}
            attached=true;explorerPid=pid;
            state=ReadHealth(out owner);
            // A just-started worker has not written its health yet (state==0).
            // That is transient, never fatal: the next Maintain cycle re-checks.
            if(state==0) {Status="等待任务栏组件初始化";return;}
        }
        if(state==4) throw new NotSupportedException("任务栏组件初始化失败："+Status);
    }

    public void Dispose() {
        if(attached) {
            IntPtr pin=LoadLibraryEx(libraryPath,IntPtr.Zero,0x100|0x800);
            try { StopResult=detach(); } finally { if(pin!=IntPtr.Zero) FreeLibrary(pin); }
        } else StopResult=0;
        try {
            string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","native-lifecycle.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            File.WriteAllText(log,DateTimeOffset.Now.ToString("o")+"\nDetachResult="+StopResult,Encoding.UTF8);
        }
        // The lifecycle record is auxiliary; a locked data directory must not break teardown.
        catch(IOException) { } catch(UnauthorizedAccessException) { }
        FreeLibrary(library);
    }
}
}
