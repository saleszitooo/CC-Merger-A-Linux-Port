#!/usr/bin/env python3
"""Reference implementation of the CCMerger merge, in Python.

It follows the C# code in CCMerger.Linux/Dbpf/Merger.cs step by step and exists so the
algorithm and the file layout can be tested without a .NET SDK. It also works as a tiny
command line merger on any machine with Python 3 (no dependencies).

    ccmerge_ref.py SOURCE_DIR OUTPUT.package [--max-mb 100] [--max-files 1000]
"""
import argparse
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dbpf  # noqa: E402


def merge(source_dir, target, max_mb=100, max_files=1000, log=print):
    max_bytes = max_mb * 1000000
    groups = [{"sources": [], "compress": False}]
    cur = groups[0]
    cur_size = cur_files = 0
    errors = []
    packages_read = file_count = 0

    for path in dbpf.walk_packages(source_dir):
        try:
            pack = dbpf.Package(path)
        except Exception as exc:  # unreadable package: skip it, report it
            errors.append((path, str(exc)))
            continue
        cur_size += pack.size
        cur_files += pack.header_count
        if (max_bytes and cur_size >= max_bytes) or (max_files and cur_files >= max_files):
            cur = {"sources": [], "compress": False}
            groups.append(cur)
            cur_size = cur_files = 0
        if pack.has_dir:
            cur["compress"] = True
        cur["sources"].append(pack)
        file_count += len(pack.entries)
        packages_read += 1

    groups = [g for g in groups if g["sources"]]
    if file_count == 0:
        log("Nothing to merge.")
        return [], errors

    target = os.path.abspath(target)
    out_dir = os.path.dirname(target)
    base = os.path.splitext(os.path.basename(target))[0]
    outputs = []
    for i, g in enumerate(groups):
        out = os.path.join(out_dir, "%s%d.package" % (base, i))
        write_group(g, out)
        outputs.append(out)
    log("Merged %d packages containing %d files into %d packages." % (packages_read, file_count, len(outputs)))
    return outputs, errors


def write_group(group, out_path):
    items = [(p, e) for p in group["sources"] for e in p.entries]

    dir_bytes = b""
    if group["compress"]:
        dir_bytes = b"".join(
            struct.pack("<IIIII", e.type, e.group, e.instance, e.instance2, e.uncompressed)
            for _, e in items if e.uncompressed != 0)

    count = len(items) + (1 if group["compress"] else 0)
    index_size = count * 24
    dir_offset = 96 + index_size
    pos = dir_offset + len(dir_bytes)
    offsets = []
    for _, e in items:
        offsets.append(pos)
        pos += e.size
    if pos > 0xFFFFFFFF:
        raise ValueError("merged package would be bigger than 4 GB")

    out = bytearray()
    out += b"DBPF"
    out += struct.pack("<II", 1, 2)
    out += bytes(12)
    out += struct.pack("<ii", 0, 0)
    out += struct.pack("<I", 7)
    out += struct.pack("<III", count, 96, index_size)
    out += struct.pack("<III", 0, 0, 0)
    out += struct.pack("<I", 2)
    out += bytes(32)
    assert len(out) == 96

    if group["compress"]:
        out += struct.pack("<IIIIII", dbpf.DIR_TYPE, dbpf.DIR_GROUP, dbpf.DIR_INSTANCE, 0, dir_offset, len(dir_bytes))
    for (_, e), off in zip(items, offsets):
        out += struct.pack("<IIIIII", e.type, e.group, e.instance, e.instance2, off, e.size)
    out += dir_bytes
    for p, e in items:
        out += p.read(e)
    assert len(out) == pos
    with open(out_path, "wb") as f:
        f.write(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("source_dir")
    ap.add_argument("target", help="e.g. /somewhere/mergedcc.package -> mergedcc0.package, mergedcc1.package...")
    ap.add_argument("--max-mb", type=int, default=100, help="0 = no limit")
    ap.add_argument("--max-files", type=int, default=1000, help="0 = no limit")
    a = ap.parse_args()
    outputs, errors = merge(a.source_dir, a.target, a.max_mb, a.max_files)
    for o in outputs:
        print("wrote", o)
    for path, msg in errors:
        print("SKIPPED", path, "-", msg)


if __name__ == "__main__":
    main()
