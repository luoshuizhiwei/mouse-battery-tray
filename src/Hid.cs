using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MouseBattery {
internal sealed class HidDevice {
    public string Path, Product;
    public ushort Pid, UsagePage, Usage, InputLength, OutputLength;
    public override string ToString() { return string.Format("PID={0:X4} {1} usage={2:X4}:{3:X4} in={4} out={5}", Pid, Product, UsagePage, Usage, InputLength, OutputLength); }
}
internal static class Native {
    [StructLayout(LayoutKind.Sequential)] internal struct InterfaceData { public int Size; public Guid Guid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct Attributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] internal struct Caps {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError=true)] internal static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes a);
    [DllImport("hid.dll", CharSet=CharSet.Unicode)] internal static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder s, int n);
    [DllImport("hid.dll")] internal static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] internal static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr p, out Caps caps);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid g, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, uint size, out uint transferred, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, uint size, out uint transferred, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out uint transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initial, string name);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
    internal static List<HidDevice> Enumerate(List<string> rejected) {
        var result = new List<HidDevice>(); Guid guid; HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 18);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try {
            for (uint i=0;;i++) {
                var data = new InterfaceData { Size=Marshal.SizeOf(typeof(InterfaceData)) };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) {
                    if (Marshal.GetLastWin32Error()==259) break;
                    throw new Win32Exception();
                }
                uint size; SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal((int)size);
                try {
                    Marshal.WriteInt32(detail, IntPtr.Size==8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out size, IntPtr.Zero)) throw new Win32Exception();
                    string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                    if (path.IndexOf("vid_046d", StringComparison.OrdinalIgnoreCase)<0) continue;
                    // A busy or inaccessible device must not break the whole scan.
                    try {
                        using (var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                            if (h.IsInvalid) throw new Win32Exception();
                            var a=new Attributes { Size=Marshal.SizeOf(typeof(Attributes)) };
                            if (!HidD_GetAttributes(h,ref a)) throw new Win32Exception();
                            IntPtr p; if (!HidD_GetPreparsedData(h,out p)) throw new Win32Exception();
                            Caps c; try { if(HidP_GetCaps(p,out c)!=0x110000) throw new IOException("HidP_GetCaps failed"); } finally { HidD_FreePreparsedData(p); }
                            if(c.UsagePage!=0xFF00 || c.InputLength<20 || c.OutputLength<20) continue;
                            var name=new StringBuilder(256);
                            if (!HidD_GetProductString(h,name,512)) throw new Win32Exception();
                            result.Add(new HidDevice { Path=path,Product=name.ToString(),Pid=a.Product,UsagePage=c.UsagePage,Usage=c.Usage,InputLength=c.InputLength,OutputLength=c.OutputLength });
                        }
                    }
                    catch(Win32Exception e) { if(rejected!=null) rejected.Add(path+": "+e.Message); }
                    catch(IOException e) { if(rejected!=null) rejected.Add(path+": "+e.Message); }
                } finally { Marshal.FreeHGlobal(detail); }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
}
internal sealed class HidConnection : IDisposable {
    readonly SafeFileHandle handle;
    readonly HidDevice device;
    internal Action<string> Trace;
    private byte softwareId=8;
    internal HidConnection(HidDevice d) {
        device=d; handle=Native.CreateFile(d.Path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        if(handle.IsInvalid) throw new Win32Exception();
    }
    private int Transfer(byte[] bytes, bool write, int timeout) {
        IntPtr buffer=Marshal.AllocHGlobal(bytes.Length), ov=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Overlapped)));
        IntPtr evt=Native.CreateEvent(IntPtr.Zero,true,false,null);
        if(evt==IntPtr.Zero) { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(ov); throw new Win32Exception(); }
        try {
            Marshal.StructureToPtr(new Native.Overlapped { Event=evt },ov,false);
            if(write) Marshal.Copy(bytes,0,buffer,bytes.Length);
            uint count;
            bool ok=write ? Native.WriteFile(handle,buffer,(uint)bytes.Length,out count,ov) : Native.ReadFile(handle,buffer,(uint)bytes.Length,out count,ov);
            if(!ok) {
                int error=Marshal.GetLastWin32Error();
                if(error!=997) throw new Win32Exception(error);
                uint wait=Native.WaitForSingleObject(evt,(uint)timeout);
                if(wait!=0) {
                    Native.CancelIoEx(handle,ov);
                    Native.GetOverlappedResult(handle,ov,out count,true);
                    if(wait==258) throw new TimeoutException("HID response timeout");
                    throw new Win32Exception("HID wait failed: "+wait);
                }
                if(!Native.GetOverlappedResult(handle,ov,out count,false)) throw new Win32Exception();
            }
            if(!write) Marshal.Copy(buffer,bytes,0,(int)count);
            return (int)count;
        } finally { Native.CloseHandle(evt); Marshal.FreeHGlobal(ov); Marshal.FreeHGlobal(buffer); }
    }
    internal byte[] Query(byte address, byte feature, byte function, params byte[] args) {
        softwareId=(byte)(softwareId==15 ? 8 : softwareId+1);
        byte fn=(byte)(function|softwareId);
        byte[] request=new byte[device.OutputLength]; request[0]=0x11; request[1]=address; request[2]=feature; request[3]=fn;
        Array.Copy(args,0,request,4,args.Length);
        if(Trace!=null) Trace("TX "+BitConverter.ToString(request));
        Transfer(request,true,700);
        var clock=Stopwatch.StartNew();
        // C54D replies can arrive after 900 ms, including while the mouse is idle.
        // The window stays 2.2x that worst case, but no longer: an outstanding
        // request into a sleeping link shares the same RF hop as user motion, so
        // every idle second held here is a second the cursor can stutter.
        const int responseTimeoutMs=2000;
        while(clock.ElapsedMilliseconds<responseTimeoutMs) {
            byte[] response=new byte[device.InputLength];
            int count=Transfer(response,false,(int)Math.Max(1,responseTimeoutMs-clock.ElapsedMilliseconds));
            if(Trace!=null) Trace("RX "+BitConverter.ToString(response,0,count));
            if(count<7 || (response[0]!=0x10 && response[0]!=0x11) || response[1]!=address) continue;
            if((response[2]==0xFF || response[2]==0x8F) && response[3]==feature && response[4]==fn) throw new IOException("HID++ error 0x"+response[5].ToString("X2"));
            if(response[2]!=feature || response[3]!=fn) continue;
            byte[] payload=new byte[count-4]; Array.Copy(response,4,payload,0,payload.Length); return payload;
        }
        throw new TimeoutException("No matching HID++ reply");
    }
    internal byte Feature(byte address, ushort id) { return Query(address,0,0,(byte)(id>>8),(byte)id,0)[0]; }
    public void Dispose() { handle.Dispose(); }
}
}
