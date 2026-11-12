param(
    [string]$ProgramPath = (Join-Path $PSScriptRoot '..\src\Program.cs'),
    [string]$OutputDirectory = 'E:\Agent任务\进行中\MouseBattery\heap-rca-20261105\raw-name-probe'
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$program=Get-Content -LiteralPath $ProgramPath -Raw
$start=$program.IndexOf('void HandleRawInput(')
if($start -lt 0){throw 'HandleRawInput not found'}
$allocation=[regex]::Match($program.Substring($start),'var buf=Marshal\.AllocHGlobal\(([^\r\n]+)\);')
if(-not $allocation.Success){throw 'Raw Input name allocation not found'}
$expression=$allocation.Groups[1].Value
$source=@'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
class RawNameProbe {
 [StructLayout(LayoutKind.Sequential)] struct Device { public IntPtr Handle;public uint Type; }
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceList(IntPtr list,ref uint count,uint size);
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceInfoW(IntPtr device,uint command,IntPtr data,ref uint size);
 static int Main() {
  uint count=0;uint stride=(uint)Marshal.SizeOf(typeof(Device));
  if(GetRawInputDeviceList(IntPtr.Zero,ref count,stride)==uint.MaxValue) throw new Win32Exception();
  IntPtr list=Marshal.AllocHGlobal(checked((int)(count*stride)));
  int tested=0,failed=0;
  try {
   uint devices=GetRawInputDeviceList(list,ref count,stride);
   if(devices==uint.MaxValue)throw new Win32Exception();
   for(int i=0;i<devices;i++) {
    var device=(Device)Marshal.PtrToStructure(IntPtr.Add(list,checked(i*(int)stride)),typeof(Device));
    if(device.Type!=0)continue;
    uint size=0;
    if(GetRawInputDeviceInfoW(device.Handle,0x20000007,IntPtr.Zero,ref size)==uint.MaxValue)throw new Win32Exception();
    int logicalBytes=__ALLOCATION__;
    uint capacity=size+16;
    // Extra physical storage contains the old overrun without corrupting any heap.
    int physicalBytes=checked(Math.Max(logicalBytes,(int)capacity*2)+32);
    IntPtr buffer=Marshal.AllocHGlobal(physicalBytes);
    try {
     var bytes=new byte[physicalBytes];for(int k=0;k<bytes.Length;k++)bytes[k]=0xA5;
     Marshal.Copy(bytes,0,buffer,bytes.Length);
     uint chars=capacity;
     uint copied=GetRawInputDeviceInfoW(device.Handle,0x20000007,buffer,ref chars);
     if(copied==uint.MaxValue)throw new Win32Exception();
     Marshal.Copy(buffer,bytes,0,bytes.Length);
     int overwritten=0;
     for(int k=logicalBytes;k<bytes.Length;k++)if(bytes[k]!=0xA5)overwritten++;
     string path=Marshal.PtrToStringUni(buffer);
     bool ok=overwritten==0;
     Console.WriteLine((ok ? "PASS" : "FAIL")+" requiredChars="+size+" logicalBytes="+logicalBytes+" pathChars="+path.Length+" pathBytesWithNul="+((path.Length+1)*2)+" overwrittenBytesBeyondAllocation="+overwritten+" path="+path);
     tested++;if(!ok)failed++;
    } finally {Marshal.FreeHGlobal(buffer);}
   }
  } finally {Marshal.FreeHGlobal(list);}
  if(tested==0)throw new Exception("No mouse Raw Input devices available; no verification performed");
  Console.WriteLine("Mouse devices tested="+tested+" failures="+failed+"; real API, physical canary storage; no heap corruption induced");
  return failed==0 ? 0 : 1;
 }
}
'@
$source=$source.Replace('__ALLOCATION__',$expression)
$cs=Join-Path $OutputDirectory 'RawNameProbe.cs'
$exe=Join-Path $OutputDirectory 'RawNameProbe.exe'
[IO.File]::WriteAllText($cs,$source,[Text.UTF8Encoding]::new($false))
Write-Output ('ProductionAllocationExpression='+$expression)
Write-Output ('ProductionSourceSHA256='+(Get-FileHash -LiteralPath $ProgramPath).Hash)
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 "/out:$exe" $cs
if($LASTEXITCODE -ne 0){throw 'Canary probe build failed'}
& $exe
exit $LASTEXITCODE
