import struct, glob, os

def sections(d):
    e = struct.unpack_from('<I', d, 0x3c)[0]
    n = struct.unpack_from('<H', d, e+6)[0]
    so = struct.unpack_from('<H', d, e+20)[0]
    o = e+24+so
    out = []
    for i in range(n):
        name = d[o:o+8].rstrip(b'\0').decode('ascii','replace')
        vsize, va, rsize, rptr = struct.unpack_from('<IIII', d, o+8)
        out.append((name, va, vsize, rptr, rsize))
        o += 40
    return out

def rva2off(secs, rva):
    for name, va, vsize, rptr, rsize in secs:
        if va <= rva < va + max(vsize, rsize):
            return rptr + (rva - va)
    return None

def pdb_of(path, label):
    d = open(path, 'rb').read()
    e = struct.unpack_from('<I', d, 0x3c)[0]
    magic = struct.unpack_from('<H', d, e+24)[0]
    dd = e+24+112 if magic == 0x20b else e+24+96
    dbg_rva, dbg_size = struct.unpack_from('<II', d, dd+6*8)
    secs = sections(d)
    f = rva2off(secs, dbg_rva)
    if f is None:
        print(label, 'no debug mapping')
        return
    for i in range(dbg_size//28):
        typ = struct.unpack_from('<I', d, f+i*28+12)[0]
        sz = struct.unpack_from('<I', d, f+i*28+16)[0]
        ptr = struct.unpack_from('<I', d, f+i*28+24)[0]
        if typ == 2:
            cv = d[ptr:ptr+sz]
            if cv[:4] == b'RSDS':
                guid = cv[4:20]
                age = struct.unpack_from('<I', cv, 20)[0]
                name = cv[24:].split(b'\0')[0].decode('ascii','replace')
                g = '%08X%04X%04X%s' % (struct.unpack_from('<I',guid,0)[0], struct.unpack_from('<H',guid,4)[0], struct.unpack_from('<H',guid,6)[0], guid[8:16].hex().upper())
                print(label, 'PDB:', name, 'key:', g+str(age))
            else:
                print(label, 'cv sig', cv[:4])

old = glob.glob(r'C:\Windows\WinSxS\amd64_microsoft-windows-taskbar-dll_*\Taskbar.dll')
old = [p for p in old if not p.endswith('\\r\\Taskbar.dll')]
for p in old:
    d = open(p,'rb').read()
    import hashlib
    h = hashlib.sha256(d).hexdigest().upper()
    if h.startswith('CA1E335A'):
        print('OLD MATCH FOUND:', p, 'size', len(d))
        pdb_of(p, 'OLD')
        break
else:
    print('no CA1E335A copy found in WinSxS; listing all')
    for p in old:
        print(p, len(open(p,'rb').read()))
pdb_of(r'C:\Windows\System32\taskbar.dll', 'NEW')
