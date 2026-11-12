param([string]$DllPath = 'C:\Windows\System32\taskbar.dll', [string]$PdbDir = 'E:\Agent任务\进行中\MouseBattery\evidence\pdb')
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class DbgHelp4 {
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
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymFromAddr(IntPtr hProcess, ulong address, out ulong displacement, IntPtr symbolInfo);
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
    public static string NameAt(ulong rva) {
        int total = Marshal.SizeOf(typeof(SYMINFO_HEAD)) + 700;
        IntPtr buf = Marshal.AllocHGlobal(total);
        try {
            var head = new SYMINFO_HEAD();
            head.SizeOfStruct = (uint)Marshal.SizeOf(typeof(SYMINFO_HEAD));
            head.MaxNameLen = 700;
            Marshal.StructureToPtr(head, buf, false);
            ulong disp;
            if(!SymFromAddr(hProc, rva, out disp, buf)) return null;
            IntPtr namePtr = (IntPtr)((long)buf + (int)Marshal.OffsetOf(typeof(SYMINFO_HEAD), "MaxNameLen") + 4);
            return Marshal.PtrToStringAnsi(namePtr);
        } finally { Marshal.FreeHGlobal(buf); }
    }
}
'@

[DbgHelp4]::SymSetOptions(0x00000012) | Out-Null
if(-not [DbgHelp4]::SymInitialize([DbgHelp4]::hProc, $PdbDir, $false)) { throw "SymInitialize failed" }
$base = [DbgHelp4]::SymLoadModuleEx([DbgHelp4]::hProc, [IntPtr]::Zero, $DllPath, $null, 0x180000000, 0, [IntPtr]::Zero, 0)
if($base -eq 0) { throw "SymLoadModuleEx failed" }

$d = [IO.File]::ReadAllBytes($DllPath)
function SecInfo([byte[]]$d, [string]$want) {
    $e = [BitConverter]::ToUInt32($d, 0x3c)
    $n = [BitConverter]::ToUInt16($d, $e+6)
    $so = [BitConverter]::ToUInt16($d, $e+20)
    $o = $e+24+$so
    for($i=0; $i -lt $n; $i++) {
        $name = [Text.Encoding]::ASCII.GetString($d, $o, 8).TrimEnd([char]0)
        $vsize = [BitConverter]::ToUInt32($d, $o+8)
        $vaddr = [BitConverter]::ToUInt32($d, $o+12)
        if($name -eq $want) { return @($vaddr, $vsize) }
        $o += 40
    }
    return @()
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
$imgbase = [BitConverter]::ToUInt64($d, ([BitConverter]::ToUInt32($d, 0x3c))+24+24)
$t = SecInfo $d '.text'
$tva = $t[0]; $tvs = $t[1]
function InText([uint64]$rva) { return $rva -ge [uint64]$tva -and $rva -lt ([uint64]$tva + [uint64]$tvs) }
function IsThunk([byte[]]$d, [uint64]$rva) {
    $off = RvaToOff $d ([int]$rva)
    if($off -lt 0) { return $false }
    # sub rcx,imm8 ; jmp rel32   OR   jmp rel32 (tail jump)
    if($d[$off] -eq 0x48 -and $d[$off+1] -eq 0x83 -and $d[$off+2] -eq 0xE9 -and $d[$off+3] -le 0x7F -and $d[$off+4] -eq 0xE9) { return $true }
    return $false
}
# Struct scan of .rdata: arrays of >=6 qwords that all point into .text and all look like thunks
$rd = SecInfo $d '.rdata'
$rdva = $rd[0]; $rdvs = $rd[1]
$roff = RvaToOff $d $rdva
Write-Output ('.rdata rva 0x{0:X} size {1}' -f $rdva, $rdvs)
$candidates = @()
for($off = $roff; $off -lt $roff + $rdvs - 48; $off += 8) {
    $ok = $true
    for($i = 0; $i -lt 6; $i++) {
        $p = ([BitConverter]::ToUInt64($d, $off + $i*8) - $imgbase) -band 0xFFFFFFFFFF
        if(-not (InText $p) -or -not (IsThunk $d ([int]$p))) { $ok = $false; break }
    }
    if($ok) { $candidates += ($off - $roff + $rdva) }
}
Write-Output ('thunk-vtable candidates: ' + $candidates.Count)
foreach($rva in $candidates) {
    $sym = [DbgHelp4]::NameAt($base + [uint64]$rva)
    Write-Output ('vtable? rva=0x{0:X} sym={1}' -f $rva, $sym)
}
# Also verify known functions resolve
foreach($rva in @(0xB10A4, 0x290B0, 0x1237B0)) {
    Write-Output ('known rva=0x{0:X} sym={1}' -f $rva, [DbgHelp4]::NameAt($base + [uint64]$rva))
}
[DbgHelp4]::SymCleanup([DbgHelp4]::hProc) | Out-Null
