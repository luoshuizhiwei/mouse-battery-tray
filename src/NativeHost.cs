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
    const string TaskbarSha256="CA1E335AC1FAE5FF57519035302A2F365C4620D3A230F31F1231AF7331760741";
    const string HealthMap=@"Local\MouseBatteryTaskbar.NativeHealth.v3";
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint AttachFunction(uint owner);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint DetachFunction();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,SetLastError=true)] static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string name,string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    readonly IntPtr library;
    readonly AttachFunction attach;
    readonly DetachFunction detach;
    readonly Stopwatch timer=Stopwatch.StartNew();
    MemoryMappedFile mapping;
    MemoryMappedViewAccessor view;
    uint explorerPid;
    long nextCheck;
    bool attached;
    internal string Status="等待任务栏";
    internal uint StopResult;

    internal NativeHost() {
        string taskbar=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskbar.dll");
        using(var sha=SHA256.Create()) using(var file=File.OpenRead(taskbar)) {
            string hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
            if(hash!=TaskbarSha256) throw new NotSupportedException("当前 Windows 任务栏版本尚未适配。程序未修改任务栏；请保留系统版本和此指纹以便适配："+hash);
        }
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
        library=LoadLibraryEx(path,IntPtr.Zero,0x100|0x800);
        if(library==IntPtr.Zero) throw new Win32Exception();
        IntPtr start=GetProcAddress(library,"NativeAttach"), stop=GetProcAddress(library,"NativeDetach");
        if(start==IntPtr.Zero || stop==IntPtr.Zero) {FreeLibrary(library);throw new EntryPointNotFoundException("Native component entry points unavailable");}
        attach=(AttachFunction)Marshal.GetDelegateForFunctionPointer(start,typeof(AttachFunction));
        detach=(DetachFunction)Marshal.GetDelegateForFunctionPointer(stop,typeof(DetachFunction));
    }

    private uint ReadHealth(out uint owner) {
        owner=0;
        if(view==null) {
            try {mapping=MemoryMappedFile.OpenExisting(HealthMap,MemoryMappedFileRights.Read);view=mapping.CreateViewAccessor(0,4096,MemoryMappedFileAccess.Read);}
            catch(FileNotFoundException) {Status="等待任务栏组件";return 0;}
        }
        int before=view.ReadInt32(4);
        if((before&1)!=0) return 0;
        Thread.MemoryBarrier();
        uint magic=view.ReadUInt32(0),state=view.ReadUInt32(8),pid=view.ReadUInt32(12);
        ulong tick=view.ReadUInt64(16);
        byte[] init=new byte[512],layout=new byte[2048];
        view.ReadArray(24,init,0,init.Length);view.ReadArray(536,layout,0,layout.Length);
        Thread.MemoryBarrier();
        if(before!=view.ReadInt32(4)) return 0;
        if(magic!=0x3348424D) {Status="等待任务栏组件初始化";return 0;}
        owner=pid;
        Status=Encoding.Unicode.GetString(init).Split('\0')[0]+" | "+Encoding.Unicode.GetString(layout).Split('\0')[0];
        if(state==2 && GetTickCount64()-tick>10000) Status="任务栏组件未响应 | "+Status;
        return state;
    }

    internal void Maintain() {
        if(timer.ElapsedMilliseconds<nextCheck) return;
        nextCheck=timer.ElapsedMilliseconds+2000;
        IntPtr taskbar=FindWindow("Shell_TrayWnd",null);
        if(taskbar==IntPtr.Zero) {Status="等待 Windows 任务栏恢复";return;}
        uint pid;GetWindowThreadProcessId(taskbar,out pid);
        uint owner;uint state=ReadHealth(out owner);
        if(!attached || explorerPid!=pid || state==3 || (owner!=0 && owner!=(uint)Process.GetCurrentProcess().Id)) {
            uint error=attach((uint)Process.GetCurrentProcess().Id);
            if(error!=0) {ReadHealth(out owner);throw new Win32Exception((int)error,"无法连接 Windows 任务栏："+Status);}
            attached=true;explorerPid=pid;
            state=ReadHealth(out owner);
        }
        if(state==4) throw new NotSupportedException("任务栏组件初始化失败："+Status);
    }

    public void Dispose() {
        StopResult=attached ? detach() : 0;
        try {
            string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","native-lifecycle.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            File.WriteAllText(log,DateTimeOffset.Now.ToString("o")+"\nDetachResult="+StopResult,Encoding.UTF8);
        }
        // The lifecycle record is auxiliary; a locked data directory must not break teardown.
        catch(IOException) { } catch(UnauthorizedAccessException) { }
        if(view!=null) view.Dispose();if(mapping!=null) mapping.Dispose();
        FreeLibrary(library);
    }
}
}
