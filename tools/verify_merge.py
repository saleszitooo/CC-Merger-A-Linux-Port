#!/usr/bin/env python3
"""Check that a merge didn't lose or change anything.

    verify_merge.py SOURCE_DIR OUTPUT_DIR [--prefix mergedcc]

Compares every resource of the original packages in SOURCE_DIR with the merged packages
(OUTPUT_DIR/<prefix>0.package, <prefix>1.package, ...): same type/group/instance, byte for
byte identical data, and same DIR (compression) records. Works on the output of the C#
app and of ccmerge_ref.py alike. Read only; it never modifies anything.

Exit code 0 = everything matches, 1 = something is wrong.
"""
import argparse
import collections
import hashlib
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dbpf  # noqa: E402


def digest(pack, entry):
    return hashlib.sha1(pack.read(entry)).hexdigest()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("source_dir")
    ap.add_argument("output_dir")
    ap.add_argument("--prefix", default="mergedcc")
    a = ap.parse_args()

    pattern = re.compile(r"^%s\d+\.package$" % re.escape(a.prefix), re.IGNORECASE)
    outputs = sorted(
        (os.path.join(a.output_dir, n) for n in os.listdir(a.output_dir) if pattern.match(n)),
        key=lambda p: int(re.search(r"(\d+)\.package$", p, re.IGNORECASE).group(1)))
    if not outputs:
        print("FAIL: no %s<N>.package files in %s" % (a.prefix, a.output_dir))
        return 1
    output_set = {os.path.realpath(p) for p in outputs}

    problems = []
    src_res = collections.Counter()   # (type, group, inst, inst2, sha1)
    src_dir = collections.Counter()   # (type, group, inst, inst2, uncompressed size)
    src_count = skipped = 0
    for path in dbpf.walk_packages(a.source_dir):
        if os.path.realpath(path) in output_set:
            continue
        try:
            pack = dbpf.Package(path)
        except Exception as exc:
            skipped += 1
            print("note: source %s is unreadable (%s); expected to be skipped by the merge" % (path, exc))
            continue
        src_count += 1
        for e in pack.entries:
            src_res[e.key + (digest(pack, e),)] += 1
            if e.uncompressed:
                src_dir[e.key + (e.uncompressed,)] += 1

    out_res = collections.Counter()
    out_dir = collections.Counter()
    for path in outputs:
        try:
            raw = open(path, "rb").read(96)
            hdr = struct.unpack_from("<4sII", raw, 0)
            idx_minor = struct.unpack_from("<I", raw, 60)[0]
            if hdr != (b"DBPF", 1, 2) or idx_minor != 2:
                problems.append("%s: unexpected header (version %s.%s, index minor %s)" % (path, hdr[1], hdr[2], idx_minor))
            pack = dbpf.Package(path)
        except Exception as exc:
            problems.append("%s: cannot be read: %s" % (path, exc))
            continue
        if pack.header_count != len(pack.entries) + (1 if pack.has_dir else 0):
            problems.append("%s: duplicate resources inside the merged package" % path)
        for e in pack.entries:
            out_res[e.key + (digest(pack, e),)] += 1
            if e.uncompressed:
                out_dir[e.key + (e.uncompressed,)] += 1

    for name, a_, b_ in (("resource", src_res, out_res), ("DIR record", src_dir, out_dir)):
        missing = a_ - b_
        extra = b_ - a_
        for k in list(missing)[:10]:
            problems.append("missing %s %s x%d" % (name, k, missing[k]))
        for k in list(extra)[:10]:
            problems.append("unexpected %s %s x%d" % (name, k, extra[k]))
        if len(missing) > 10 or len(extra) > 10:
            problems.append("... and more %s differences" % name)

    print("%d source packages (%d skipped as unreadable), %d resources -> %d merged packages, %d resources"
          % (src_count, skipped, sum(src_res.values()), len(outputs), sum(out_res.values())))
    if problems:
        print("FAIL")
        for p in problems:
            print("  -", p)
        return 1
    print("PASS: every resource and every DIR record is present and identical")
    return 0


if __name__ == "__main__":
    sys.exit(main())
