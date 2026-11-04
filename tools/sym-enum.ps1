param([string]$DllPath = 'C:\Windows\System32\taskbar.dll', [string]$PdbDir = 'E:\MouseBattery\evidence\pdb', [string]$OutFile = 'E:\MouseBattery\evidence\pdb\taskbar-symbols.txt')
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public class DbgHelp3 {
    public static IntPtr hProc = (IntPtr)(-1);
    public static List<string> Symbols = new List<string>();
    public delegate bool EnumSymProc(IntPtr symInfo, IntPtr userContext);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymSetOptions(uint options);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymSetSearchPath(IntPtr hProcess, string searchPath);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymInitialize(IntPtr hProcess, string userSearchPath, bool fInvadeProcess);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile, string imageName, string moduleName, ulong baseOfDll, uint sizeOfDll, IntPtr data, uint flags);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymEnumSymbols(IntPtr hProcess, ulong baseOfDll, string mask, EnumSymProc proc, IntPtr userContext);
    [DllImport("dbghelp.dll", SetLastError=true)]
    public static extern bool SymCleanup(IntPtr hProcess);
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
    public static bool OnSymbol(IntPtr symInfo, IntPtr ctx) {
        uint nameLen = (uint)Marshal.ReadInt32(symInfo, (int)Marshal.OffsetOf(typeof(SYMINFO_HEAD), "NameLen"));
        uint maxLen  = (uint)Marshal.ReadInt32(symInfo, (int)Marshal.OffsetOf(typeof(SYMINFO_HEAD), "MaxNameLen"));
        ulong addr   = (ulong)Marshal.ReadInt64(symInfo, (int)Marshal.OffsetOf(typeof(SYMINFO_HEAD), "Address"));
        IntPtr namePtr = (IntPtr)((long)symInfo + Marshal.SizeOf(typeof(SYMINFO_HEAD)));
        string name = Marshal.PtrToStringAnsi(namePtr, (int)nameLen);
        Symbols.Add(name + "|" + addr);
        return true;
    }
    public static bool EnumAll(ulong baseOfDll) {
        return SymEnumSymbols(hProc, baseOfDll, "*!*", OnSymbol, IntPtr.Zero);
    }
}
'@

[DbgHelp3]::SymSetOptions(0x00000012) | Out-Null
if(-not [DbgHelp3]::SymInitialize([DbgHelp3]::hProc, $PdbDir, $false)) { throw "SymInitialize failed" }
$base = [DbgHelp3]::SymLoadModuleEx([DbgHelp3]::hProc, [IntPtr]::Zero, $DllPath, $null, 0x180000000, 0, [IntPtr]::Zero, 0)
if($base -eq 0) { throw ("SymLoadModuleEx failed: " + [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
Write-Output ("module at 0x{0:X}" -f $base)
if(-not [DbgHelp3]::EnumAll($base)) { throw ("SymEnumSymbols failed: " + [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
Write-Output ("total symbols: " + [DbgHelp3]::Symbols.Count)
[IO.File]::WriteAllLines($OutFile, [DbgHelp3]::Symbols.ToArray())
Write-Output ("written: " + $OutFile)
[DbgHelp3]::SymCleanup([DbgHelp3]::hProc) | Out-Null
