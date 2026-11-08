/*
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at
 * http://mozilla.org/MPL/2.0/.
 */

// Reader for The Sims 2 DBPF (.package) files. Only the index is parsed; the data of each
// resource is copied byte for byte when merging, it's never decoded or decompressed.
//
// Ported from the original CCMerger (Lazy Duchess), whose package code was modified from
// FreeSO (https://github.com/riperiperi/FreeSO, MPL 2.0) and SimUnity2.

using System.Text;

namespace CCMerger.Dbpf;

/// <summary>One resource in a package's index.</summary>
internal sealed class DbpfEntry
{
    public uint TypeId;
    public uint GroupId;
    public uint InstanceId;
    public uint InstanceId2;
    public uint FileOffset;
    public uint FileSize;

    /// <summary>
    /// Uncompressed size, taken from the package's DIR resource. 0 means the resource
    /// isn't listed there (i.e. it's not compressed).
    /// </summary>
    public uint UncompressedSize;
}

internal sealed class DbpfPackage
{
    // The DIR resource: lists every compressed resource and its uncompressed size.
    public const uint DirType = 0xE86B1EEF;
    public const uint DirGroup = 0xE86B1EEF;
    public const uint DirInstance = 0x286B1F03;

    private readonly record struct EntryKey(uint Type, uint Group, uint Instance, uint Instance2);

    public string Path { get; }

    /// <summary>Size of the file on disk, in bytes.</summary>
    public long Length { get; }

    /// <summary>Entry count as written in the package header (includes the DIR resource).</summary>
    public uint HeaderEntryCount { get; private set; }

    /// <summary>True if the package has a DIR resource (= has compressed resources).</summary>
    public bool HasCompression { get; private set; }

    /// <summary>Every resource except DIR, in index order.</summary>
    public IReadOnlyList<DbpfEntry> Entries => _entries;

    private readonly List<DbpfEntry> _entries = new();

    private DbpfPackage(string path, long length)
    {
        Path = path;
        Length = length;
    }

    /// <summary>
    /// Reads the header and the index of a package, then closes the file. (The original
    /// kept every package open at once, which runs into the open-files limit on Linux
    /// when you have thousands of CC files.)
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a (supported) package.</exception>
    public static DbpfPackage Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var package = new DbpfPackage(path, stream.Length);
        package.Read(stream);
        return package;
    }

    private void Read(FileStream stream)
    {
        using var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true); // little endian

        if (Length < 96)
            throw new InvalidDataException("Not a DBPF file (too small).");

        string magic = Encoding.ASCII.GetString(r.ReadBytes(4));
        if (magic != "DBPF")
            throw new InvalidDataException("Not a DBPF file.");

        uint major = r.ReadUInt32();
        uint minor = r.ReadUInt32();
        if (major != 1 || minor > 2)
            throw new InvalidDataException($"Unsupported DBPF version {major}.{minor} (The Sims 2 uses 1.1).");

        r.ReadBytes(12);              // unknown, always 0
        r.ReadInt32();                // date created
        r.ReadInt32();                // date modified
        r.ReadUInt32();               // index major version
        uint numEntries = r.ReadUInt32();
        uint indexOffset = r.ReadUInt32();
        r.ReadUInt32();               // index size
        r.ReadUInt32();               // trash entry count
        r.ReadUInt32();               // trash index offset
        r.ReadUInt32();               // trash index size
        uint indexMinor = r.ReadUInt32();
        r.ReadBytes(32);              // padding

        HeaderEntryCount = numEntries;

        // Index minor version 2 and up has a second instance ID in every entry.
        int entrySize = indexMinor >= 2 ? 24 : 20;
        if (indexOffset > Length || (long)numEntries * entrySize > Length - indexOffset)
            throw new InvalidDataException("The index points outside of the file.");

        uint localGroup = Hash.GroupHash(System.IO.Path.GetFileNameWithoutExtension(Path));

        stream.Seek(indexOffset, SeekOrigin.Begin);

        // Same TGIR twice in one package: the last one wins but keeps the first one's slot
        // (that's what the Dictionary in the original did).
        var slotByKey = new Dictionary<EntryKey, int>();
        var all = new List<DbpfEntry>();

        for (uint i = 0; i < numEntries; i++)
        {
            var e = new DbpfEntry
            {
                TypeId = r.ReadUInt32(),
                GroupId = r.ReadUInt32()
            };
            if (e.GroupId == 0xFFFFFFFF)
                e.GroupId = localGroup;
            e.InstanceId = r.ReadUInt32();
            if (indexMinor >= 2)
                e.InstanceId2 = r.ReadUInt32();
            e.FileOffset = r.ReadUInt32();
            e.FileSize = r.ReadUInt32();

            // Catch truncated/damaged packages now, not half way through writing the output.
            if ((long)e.FileOffset + e.FileSize > Length)
                throw new InvalidDataException($"Resource #{i} points outside of the file (truncated package?).");

            var key = new EntryKey(e.TypeId, e.GroupId, e.InstanceId, e.InstanceId2);
            if (slotByKey.TryGetValue(key, out int slot))
            {
                all[slot] = e;
            }
            else
            {
                slotByKey[key] = all.Count;
                all.Add(e);
            }
        }

        // DIR resource -> fill in the uncompressed sizes.
        var dirKey = new EntryKey(DirType, DirGroup, DirInstance, 0);
        if (slotByKey.TryGetValue(dirKey, out int dirSlot))
        {
            HasCompression = true;
            ReadDir(stream, all[dirSlot], all, slotByKey, indexMinor, localGroup);
        }

        // The DIR resource is regenerated when writing, so we never copy the old one.
        foreach (var e in all)
        {
            if (e.TypeId != DirType)
                _entries.Add(e);
        }
    }

    private static void ReadDir(FileStream stream, DbpfEntry dir, List<DbpfEntry> all,
        Dictionary<EntryKey, int> slotByKey, uint indexMinor, uint localGroup)
    {
        stream.Seek(dir.FileOffset, SeekOrigin.Begin);
        var data = new byte[dir.FileSize];
        stream.ReadExactly(data, 0, data.Length);

        int recordSize = indexMinor >= 2 ? 20 : 16;
        using var r = new BinaryReader(new MemoryStream(data));

        for (int pos = 0; pos + recordSize <= data.Length; pos += recordSize)
        {
            uint type = r.ReadUInt32();
            uint group = r.ReadUInt32();
            if (group == 0xFFFFFFFF)
                group = localGroup;
            uint instance = r.ReadUInt32();
            uint instance2 = indexMinor >= 2 ? r.ReadUInt32() : 0;
            uint uncompressedSize = r.ReadUInt32();

            // A DIR record that points at a resource that isn't in the package is just ignored.
            if (slotByKey.TryGetValue(new EntryKey(type, group, instance, instance2), out int slot))
                all[slot].UncompressedSize = uncompressedSize;
        }
    }
}
