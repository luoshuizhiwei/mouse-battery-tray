using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
namespace MouseBattery {
internal sealed class TaskbarPublisher : IDisposable {
    internal const string MapName=@"Local\MouseBatteryTaskbar.v2";
    internal const int Capacity=512;
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;
    readonly int processId=System.Diagnostics.Process.GetCurrentProcess().Id;
    int sequence;
    ulong lastSuccessTick;
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    internal TaskbarPublisher(string mapName=MapName) { mapping=MemoryMappedFile.CreateOrOpen(mapName,Capacity,MemoryMappedFileAccess.ReadWrite); view=mapping.CreateViewAccessor(0,Capacity); }
    internal void MarkSuccess() { lastSuccessTick=GetTickCount64(); }
    internal void Publish(DisplayState state,long now,int position,bool startup) {
        byte[] payload=new byte[Capacity];
        bool visible=state.Visible(now);
        Buffer.BlockCopy(BitConverter.GetBytes(0x3254424Du),0,payload,0,4);
        Buffer.BlockCopy(BitConverter.GetBytes(lastSuccessTick),0,payload,8,8);
        Buffer.BlockCopy(BitConverter.GetBytes(visible ? (state.Failed ? 2 : 1) : 0),0,payload,16,4);
        if(state.Last!=null) {
            Buffer.BlockCopy(BitConverter.GetBytes(state.Last.Percent),0,payload,20,4);
            Buffer.BlockCopy(BitConverter.GetBytes(state.Last.Charging),0,payload,24,4);
            Buffer.BlockCopy(BitConverter.GetBytes(state.Last.Connection=="有线" ? 1 : 2),0,payload,28,4);
        }
        Buffer.BlockCopy(BitConverter.GetBytes(processId),0,payload,32,4);
        Buffer.BlockCopy(BitConverter.GetBytes(position),0,payload,36,4);
        Buffer.BlockCopy(BitConverter.GetBytes(startup ? 1 : 0),0,payload,40,4);
        byte[] text=Encoding.Unicode.GetBytes(state.Tooltip(now));
        Buffer.BlockCopy(text,0,payload,48,Math.Min(text.Length,Capacity-50));
        view.Write(4,++sequence); Thread.MemoryBarrier();
        view.WriteArray(0,payload,0,4); view.WriteArray(8,payload,8,Capacity-8);
        Thread.MemoryBarrier(); view.Write(4,++sequence);
    }
    public void Dispose() {
        view.Write(4,++sequence); Thread.MemoryBarrier(); view.Write(16,0);
        Thread.MemoryBarrier(); view.Write(4,++sequence);
        view.Dispose(); mapping.Dispose();
    }
}
}
