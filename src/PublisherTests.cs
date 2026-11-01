using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using MouseBattery;
internal static class PublisherTests {
    static int count;
    static void Check(bool value,string name) { if(!value) throw new Exception("FAIL "+name); count++; Console.WriteLine("PASS "+name); }
    static int Main() {
        string mapName=TaskbarPublisher.MapName+".Test."+Guid.NewGuid().ToString("N");
        var publisher=new TaskbarPublisher(mapName);
        using(var map=MemoryMappedFile.OpenExisting(mapName))
        using(var reader=map.CreateViewAccessor(0,512,MemoryMappedFileAccess.Read)) {
            var state=new DisplayState();
            publisher.Publish(state,0,0,true);
            Check(reader.ReadUInt32(0)==0x3254424D && (reader.ReadInt32(4)&1)==0,"native wire magic and committed sequence");
            Check(reader.ReadInt32(16)==0,"no reading stays hidden");
            var wired=BatteryReading.Parse(new byte[]{95,8,1,1},"PRO X 2 DEX","unit-prox","有线");
            wired.Path="hid#pid_c54d-test";
            state.Apply(new[]{wired},1000);
            publisher.MarkSuccess(); publisher.Publish(state,1000,3,true);
            Check(reader.ReadUInt64(8)>0 && reader.ReadInt32(16)==1 && reader.ReadInt32(20)==95,"successful reading carries monotonic freshness and percentage");
            Check(reader.ReadInt32(24)==1 && reader.ReadInt32(28)==1,"charging and wired transport offsets agree with native ABI");
            Check(reader.ReadInt32(36)==3 && reader.ReadInt32(40)==1,"position and autostart offsets agree with native ABI");
            byte[] bytes=new byte[464]; reader.ReadArray(48,bytes,0,464);
            Check(Encoding.Unicode.GetString(bytes).Contains("PRO X 2 DEX"),"Unicode tooltip reaches native text offset");
            ulong originalTick=reader.ReadUInt64(8);
            state.MarkFailed(new[]{wired.Path},6000); publisher.Publish(state,6000,1,false);
            Check(reader.ReadInt32(16)==2 && reader.ReadUInt64(8)==originalTick,"failed reading is stale and does not refresh timestamp");
            publisher.Publish(state,16000,1,false);
            Check(reader.ReadInt32(16)==0,"15-second expiry is exported as hidden");
            var ble=BatteryReading.Parse(new byte[]{0,1,0,0},"PRO X 2 DEX","unit-prox","蓝牙");
            ble.Path="hid#pid&b02c-test";
            state.Apply(new[]{ble},17000);
            publisher.MarkSuccess(); publisher.Publish(state,17000,0,true);
            Check(reader.ReadInt32(16)==1 && reader.ReadInt32(20)==0 && reader.ReadInt32(28)==3,"zero percent bluetooth recovers as a valid visible reading");
            var m750=BatteryReading.Parse(new byte[]{85,8,0,0},"M750L","unit-m750","蓝牙");
            m750.Path="hid#pid&b02c-test2";
            var proX=BatteryReading.Parse(new byte[]{95,8,0,0},"PRO X 2 DEX","unit-prox","2.4G");
            proX.Path=wired.Path;
            state.Apply(new[]{ble,proX,m750},18000);
            publisher.Publish(state,18000,0,true);
            Check(reader.ReadInt32(16)==1 && reader.ReadInt32(20)==85 && reader.ReadInt32(28)==3,"newest device leads the snapshot fields");
            byte[] text2=new byte[464]; reader.ReadArray(48,text2,0,464);
            Check(Encoding.Unicode.GetString(text2).Contains("M750L") && Encoding.Unicode.GetString(text2).Contains("\nPRO X 2 DEX · 95% · 2.4G"),"companion device reaches the native tooltip second line");
            publisher.Dispose();
            Check(reader.ReadInt32(16)==0 && (reader.ReadInt32(4)&1)==0,"normal exit immediately hides native display while mapping stays open");
        }
        Console.WriteLine("PASS "+count+" IPC checks"); return 0;
    }
}
