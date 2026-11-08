"""Minimal Sims 2 DBPF (.package) reader shared by the tools in this folder.

Same parsing rules as linux/CCMerger.Linux/Dbpf/DbpfPackage.cs.
"""
import os
import struct

DIR_TYPE = 0xE86B1EEF
DIR_GROUP = 0xE86B1EEF
DIR_INSTANCE = 0x286B1F03


def group_hash(name):
    """GroupID the game derives from a package file name (no extension)."""
    crc = 0x00B704CE
    for ch in name.strip().lower():
        crc ^= ord(ch) << 16
        for _ in range(8):
            crc = (crc << 1) & 0xFFFFFFFF
            if crc & 0x1000000:
                crc ^= 0x01864CFB
    return (crc & 0x00FFFFFF) | 0x7F000000


class Entry:
    __slots__ = ("type", "group", "instance", "instance2", "offset", "size", "uncompressed")

    def __init__(self, type_, group, instance, instance2, offset, size):
        self.type, self.group, self.instance, self.instance2 = type_, group, instance, instance2
        self.offset, self.size = offset, size
        self.uncompressed = 0

    @property
    def key(self):
        return (self.type, self.group, self.instance, self.instance2)


class Package:
    """Parsed package. `entries` excludes the DIR resource, duplicates collapsed (last wins)."""

    def __init__(self, path):
        self.path = path
        with open(path, "rb") as f:
            self.data = f.read()
        self.size = len(self.data)
        self.entries = []
        self.has_dir = False
        self.header_count = 0
        self._parse()

    def _u32(self, pos):
        return struct.unpack_from("<I", self.data, pos)[0]

    def _parse(self):
        d = self.data
        if len(d) < 96 or d[:4] != b"DBPF":
            raise ValueError("Not a DBPF file")
        major, minor = self._u32(4), self._u32(8)
        if major != 1 or minor > 2:
            raise ValueError("Unsupported DBPF version %d.%d" % (major, minor))
        count = self._u32(36)
        index_offset = self._u32(40)
        index_minor = self._u32(60)
        self.header_count = count
        self.index_minor = index_minor
        esize = 24 if index_minor >= 2 else 20
        if index_offset > self.size or count * esize > self.size - index_offset:
            raise ValueError("The index points outside of the file")

        local_group = group_hash(os.path.splitext(os.path.basename(self.path))[0])
        slots, allents = {}, []
        pos = index_offset
        for i in range(count):
            t, g, inst = struct.unpack_from("<III", d, pos)
            pos += 12
            inst2 = 0
            if index_minor >= 2:
                inst2 = self._u32(pos)
                pos += 4
            off, size = struct.unpack_from("<II", d, pos)
            pos += 8
            if g == 0xFFFFFFFF:
                g = local_group
            if off + size > self.size:
                raise ValueError("Resource #%d points outside of the file" % i)
            e = Entry(t, g, inst, inst2, off, size)
            if e.key in slots:
                allents[slots[e.key]] = e
            else:
                slots[e.key] = len(allents)
                allents.append(e)

        dir_key = (DIR_TYPE, DIR_GROUP, DIR_INSTANCE, 0)
        if dir_key in slots:
            self.has_dir = True
            de = allents[slots[dir_key]]
            raw = d[de.offset:de.offset + de.size]
            rec = 20 if index_minor >= 2 else 16
            for p in range(0, len(raw) - rec + 1, rec):
                t, g, inst = struct.unpack_from("<III", raw, p)
                inst2 = struct.unpack_from("<I", raw, p + 12)[0] if index_minor >= 2 else 0
                unc = struct.unpack_from("<I", raw, p + rec - 4)[0]
                if g == 0xFFFFFFFF:
                    g = local_group
                k = (t, g, inst, inst2)
                if k in slots:
                    allents[slots[k]].uncompressed = unc
        self.entries = [e for e in allents if e.type != DIR_TYPE]

    def read(self, entry):
        return self.data[entry.offset:entry.offset + entry.size]


def walk_packages(root):
    """Same order as the C# version: files of a folder (case-insensitive), then sub folders."""
    found = []

    def key(name):
        return (name.upper(), name)

    def walk(d, depth):
        try:
            names = sorted(os.listdir(d), key=key)
        except OSError:
            return
        files = [n for n in names if os.path.isfile(os.path.join(d, n))]
        dirs = [n for n in names if os.path.isdir(os.path.join(d, n))]
        for n in files:
            if n.lower().endswith(".package"):
                found.append(os.path.join(d, n))
        if depth < 64:
            for n in dirs:
                walk(os.path.join(d, n), depth + 1)

    walk(root, 0)
    return found
