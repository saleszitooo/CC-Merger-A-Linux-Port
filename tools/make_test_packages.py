#!/usr/bin/env python3
"""Creates a folder of fake (but structurally valid) Sims 2 packages for testing.

    make_test_packages.py OUTPUT_DIR [--count 40] [--seed 1]

The packages are written the way the game's own tools do it (data first, index at the
end, DBPF 1.1 with index 7.1 or 7.2, some with a DIR resource, some with local group IDs),
which is NOT how CCMerger writes them, so a merge exercises the real code paths. It also
adds a few broken files, a package in a sub folder and one with an upper case extension.
"""
import argparse
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dbpf  # noqa: E402


def write_package(path, resources, index_minor, compressed_sizes=None, version_minor=1):
    """resources: list of (type, group, instance, instance2, bytes).
    compressed_sizes: {resource position: uncompressed size} -> adds a DIR resource."""
    body = bytearray()
    index = []
    pos = 96
    for t, g, i, i2, data in resources:
        index.append((t, g, i, i2, pos, len(data)))
        body += data
        pos += len(data)
    if compressed_sizes is not None:
        rec = bytearray()
        for n, unc in compressed_sizes.items():
            t, g, i, i2, _ = resources[n]
            rec += struct.pack("<III", t, g, i)
            if index_minor >= 2:
                rec += struct.pack("<I", i2)
            rec += struct.pack("<I", unc)
        index.append((dbpf.DIR_TYPE, dbpf.DIR_GROUP, dbpf.DIR_INSTANCE, 0, pos, len(rec)))
        body += rec
        pos += len(rec)
    index_blob = bytearray()
    for t, g, i, i2, off, size in index:
        index_blob += struct.pack("<III", t, g, i)
        if index_minor >= 2:
            index_blob += struct.pack("<I", i2)
        index_blob += struct.pack("<II", off, size)
    hdr = bytearray(b"DBPF")
    hdr += struct.pack("<II", 1, version_minor) + bytes(12)
    hdr += struct.pack("<ii", 1234567, 1234568)
    hdr += struct.pack("<I", 7)
    hdr += struct.pack("<III", len(index), pos, len(index_blob))
    hdr += struct.pack("<III", 0, 0, 0)
    hdr += struct.pack("<I", index_minor) + bytes(32)
    assert len(hdr) == 96
    with open(path, "wb") as f:
        f.write(hdr + body + index_blob)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("output_dir")
    ap.add_argument("--count", type=int, default=40)
    ap.add_argument("--seed", type=int, default=1)
    a = ap.parse_args()
    rnd = random.Random(a.seed)
    os.makedirs(os.path.join(a.output_dir, "Sub Folder"), exist_ok=True)

    types = [0xE519C933, 0xAC506764, 0x1C0532FA, 0x0C560F39, 0x534C5442, 0xEBCF3E27]
    for n in range(a.count):
        index_minor = rnd.choice([1, 1, 2])
        resources = []
        for _ in range(rnd.randint(1, 30)):
            group = rnd.choice([0x1C0532FA, 0x7F000001, 0xFFFFFFFF, 0xFFFFFFFF])
            inst2 = rnd.choice([0, 0, 0x1234]) if index_minor >= 2 else 0
            data = bytes(rnd.getrandbits(8) for _ in range(rnd.randint(0, 3000)))
            resources.append((rnd.choice(types), group, rnd.getrandbits(32), inst2, data))
        # one duplicate TGIR inside a package: the last one wins
        if n % 7 == 0 and len(resources) > 2:
            t, g, i, i2, _ = resources[0]
            resources.append((t, g, i, i2, b"the winner"))
        comp = None
        if n % 3 == 0:
            comp = {k: len(resources[k][4]) * 3 + 1 for k in range(0, len(resources), 2)}
        folder = a.output_dir if n % 5 else os.path.join(a.output_dir, "Sub Folder")
        name = "cc_%03d%s" % (n, ".PACKAGE" if n == 4 else ".package")
        write_package(os.path.join(folder, name), resources, index_minor, comp, version_minor=rnd.choice([1, 1, 2]))

    # an empty one, and some broken ones that must be skipped and reported
    write_package(os.path.join(a.output_dir, "empty.package"), [], 2)
    with open(os.path.join(a.output_dir, "garbage.package"), "wb") as f:
        f.write(b"this is not a package" * 20)
    good = os.path.join(a.output_dir, "cc_001.package")
    with open(good, "rb") as f:
        data = f.read()
    with open(os.path.join(a.output_dir, "truncated.package"), "wb") as f:
        f.write(data[:len(data) // 2])
    print("wrote %d packages + 3 broken ones in %s" % (a.count + 1, a.output_dir))


if __name__ == "__main__":
    main()
