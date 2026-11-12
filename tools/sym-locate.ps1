param([string]$DllPath = 'C:\Windows\System32\taskbar.dll', [string]$PdbDir = 'E:\Agent任务\进行中\MouseBattery\evidence\pdb')
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class DbgHelp2 {
    public static IntPtr hProc = (IntPtr)(-1);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymSetOptions(uint options);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymSetSearchPath(IntPtr hProcess, string searchPath);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymInitialize(IntPtr hProcess, string userSearchPath, bool fInvadeProcess);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile, string imageName, string moduleName, ulong baseOfDll, uint sizeOfDll, IntPtr data, uint flags);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymCleanup(IntPtr hProcess);
    [DllImport("dbghelp.dll", EntryPoint="SymFromAddr", SetLastError=true)]
    public static extern bool SymFromAddrBuf(IntPtr hProcess, ulong address, out ulong displacement, IntPtr symbolInfo);
    [StructLayout(LayoutKind.Sequential, Pack=8)]
    public struct SYMINFO_HEAD {
        public uint SizeOfStruct;
        public uint TypeIndex;
        public ulong Reserved1;
        public ulong Reserved2;
        public uint Index;
        public uint Size;
        public ulong ModBase;
        public uint Flags;
        public ulong Value;
        public ulong Address;
        public uint Register;
        public uint Scope;
        public uint Tag;
        public uint NameLen;
        public uint MaxNameLen;
    }
    public static string NameAt(ulong baseAddr, ulong rva) {
        int total = Marshal.SizeOf(typeof(SYMINFO_HEAD)) + 512;
        IntPtr buf = Marshal.AllocHGlobal(total);
        try {
            var head = new SYMINFO_HEAD();
            head.SizeOfStruct = (uint)Marshal.SizeOf(typeof(SYMINFO_HEAD));
            head.MaxNameLen = 512;
            Marshal.StructureToPtr(head, buf, false);
            ulong disp;
            if(!SymFromAddrBuf(hProc, baseAddr + rva, out disp, buf)) return null;
            // SYMBOL_INFO.Name starts at 84, before the structure's tail padding.
            IntPtr namePtr = (IntPtr)((long)buf + (int)Marshal.OffsetOf(typeof(SYMINFO_HEAD), "MaxNameLen") + 4);
            return Marshal.PtrToStringAnsi(namePtr);
        } finally { Marshal.FreeHGlobal(buf); }
    }
}
'@

$h = [IntPtr]::new(-1)
[DbgHelp2]::SymSetOptions(0x00000012) | Out-Null
if(-not [DbgHelp2]::SymInitialize($h, $PdbDir, $false)) { throw "SymInitialize failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
$base = [DbgHelp2]::SymLoadModuleEx($h, [IntPtr]::Zero, $DllPath, $null, 0x180000000, 0, [IntPtr]::Zero, 0)
if($base -eq 0) { throw ("SymLoadModuleEx failed: " + [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
Write-Output ("module loaded at 0x{0:X}" -f $base)

$d = [IO.File]::ReadAllBytes($DllPath)
function SecText([byte[]]$d) {
    $e = [BitConverter]::ToUInt32($d, 0x3c)
    $n = [BitConverter]::ToUInt16($d, $e+6)
    $so = [BitConverter]::ToUInt16($d, $e+20)
    $o = $e+24+$so
    for($i=0; $i -lt $n; $i++) {
        $name = [Text.Encoding]::ASCII.GetString($d, $o, 8).TrimEnd([char]0)
        $vsize = [BitConverter]::ToUInt32($d, $o+8)
        $vaddr = [BitConverter]::ToUInt32($d, $o+12)
        if($name -eq '.text') { return @($vaddr, $vsize) }
        $o += 40
    }
    return @(4096, 1850000)
}
function RvaToOff([byte[]]$d, [int]$rva) {
    $e = [BitConverter]::ToUInt32($d, 0x3c)
    $n = [BitConverter]::ToUInt16($d, $e+6)
    $so = [BitConverter]::ToUInt16($d, $e+20)
    $o = $e+24+$so
    for($i=0; $i -lt $n; $i++) {
        $vsize = [BitConverter]::ToUInt32($d, $o+8)
        $vaddr = [BitConverter]::ToUInt32($d, $o+12)
        $rsize = [BitConverter]::ToUInt32($d, $o+16)
        $rptr  = [BitConverter]::ToUInt32($d, $o+20)
        if($vaddr -le $rva -and $rva -lt $vaddr + [Math]::Max($vsize,$rsize)) { return $rptr + ($rva - $vaddr) }
        $o += 40
    }
    return -1
}
function Find-All([byte[]]$hay, [byte[]]$needle) {
    $out = @()
    $i = 0
    while($true) {
        $i = [Array]::IndexOf($hay, $needle[0], $i)
        if($i -lt 0 -or $i -gt $hay.Length - $needle.Length) { break }
        $m = $true
        for($j = 0; $j -lt $needle.Length; $j++) {
            if($needle[$j] -ne 0xEE -and $hay[$i+$j] -ne $needle[$j]) { $m = $false; break }
        }
        if($m) { $out += $i }
        $i++
    }
    return $out
}
$t = SecText $d
$tRva = $t[0]; $tEnd = $t[0]+$t[1]
Write-Output ('.text rva 0x{0:X}..0x{1:X}' -f $tRva, $tEnd)
function NameAtRva([int]$rva) { $n = [DbgHelp2]::NameAt($base, [uint64]$rva); if($n) { return $n } else { return '(no symbol)' } }

# 1) frameHeight prologue: sub rsp,X; add rcx,Y; cmp [rcx],0; je  -- element offset lives at code[7]
$fh = @(0x48,0x83,0xEC,0xEE,0x48,0x83,0xC1,0xEE,0x48,0x83,0x39,0x00,0x74)
foreach($off in (Find-All $d $fh)) {
    if($off -lt (RvaToOff $d $tRva) -or $off -gt (RvaToOff $d ($tEnd-16))) { continue }
    $rva = $off - (RvaToOff $d $tRva) + $tRva
    Write-Output ('frameHeight? rva=0x{0:X} elementOffset=0x{1:X} sym={2}' -f $rva, $d[$off+7], (NameAtRva $rva))
}
# 2) getHost: mov rcx,[rcx+X]; mov rax,[rax]; mov rax,[rax+0x3B0]; call
$gh = @(0x48,0x8B,0x89,0xEE,0xEE,0xEE,0xEE,0x48,0x8B,0x01,0x48,0x8B,0x80,0xB0,0x03,0x00,0x00,0xE8)
foreach($off in (Find-All $d $gh)) {
    if($off -lt (RvaToOff $d $tRva) -or $off -gt (RvaToOff $d ($tEnd-18))) { continue }
    $rva = $off - (RvaToOff $d $tRva) + $tRva
    Write-Output ('getHost? rva=0x{0:X} sym={1}' -f $rva, (NameAtRva $rva))
}
# 3) decref: push rbx; sub rsp,20h; mov rbx,rcx; or eax,-1; lock xadd [rcx+8],eax; cmp eax,1
$dc = @(0x40,0x53,0x48,0x83,0xEC,0x20,0x48,0x8B,0xD9,0x83,0xC8,0xFF,0xF0,0x0F,0xC1,0x41,0x08,0x83,0xF8,0x01)
foreach($off in (Find-All $d $dc)) {
    if($off -lt (RvaToOff $d $tRva) -or $off -gt (RvaToOff $d ($tEnd-20))) { continue }
    $rva = $off - (RvaToOff $d $tRva) + $tRva
    Write-Output ('decref? rva=0x{0:X} sym={1}' -f $rva, (NameAtRva $rva))
}
[DbgHelp2]::SymCleanup($h) | Out-Null
