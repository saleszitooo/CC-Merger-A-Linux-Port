// The merge itself: scan a folder for packages, group them according to the size / file
// count limits, and write each group as one new package.
//
// Ported from CCMerger's Form1.cs (Lazy Duchess). The output layout is the same one the
// Windows version writes: DBPF 1.2, index 7.2, DIR resource right after the index.

using System.Text;

namespace CCMerger.Dbpf;

internal sealed class MergeOptions
{
    public required string SourceDirectory { get; init; }

    /// <summary>Where to save. "dir/mergedcc.package" produces dir/mergedcc0.package, dir/mergedcc1.package...</summary>
    public required string TargetPath { get; init; }

    /// <summary>Max size of a merged package in bytes. 0 = no limit.</summary>
    public long MaxPackageBytes { get; init; }

    /// <summary>Max number of resources in a merged package. 0 = no limit.</summary>
    public uint MaxFilesPerPackage { get; init; }

    /// <summary>Also write a .txt next to each merged package listing the packages inside it.</summary>
    public bool WriteContentLists { get; init; }
}

internal sealed record PackageError(string File, string Trace);

/// <param name="Preparing">True while the source packages are still being read.</param>
/// <param name="Percent">0-100, only meaningful when Preparing is false.</param>
internal readonly record struct MergeProgress(bool Preparing, int Percent);

internal sealed class MergeResult
{
    public int PackagesRead;
    public long FileCount;
    public readonly List<string> OutputFiles = new();
    public readonly List<PackageError> Errors = new();
    public bool NothingToMerge;
    public bool Failed;
    public string FailureTrace = "";
}

internal static class Merger
{
    private const int HeaderSize = 96;
    private const int IndexEntrySize = 24; // type, group, instance, instance2, offset, size

    private sealed class PendingPackage
    {
        public readonly List<DbpfPackage> Sources = new();
        public bool Compress; // write a DIR resource
    }

    /// <summary>Runs the whole merge. Blocking: call it from a background thread.</summary>
    /// <exception cref="OperationCanceledException">Cancelled; nothing is left behind.</exception>
    public static MergeResult Run(MergeOptions options, IProgress<MergeProgress>? progress, CancellationToken ct)
    {
        var result = new MergeResult();
        progress?.Report(new MergeProgress(true, 0));

        // ---- 1. Read every package and decide which merged package it goes into ----
        var pending = new List<PendingPackage>();
        var current = new PendingPackage();
        pending.Add(current);
        long currentSize = 0;
        uint currentFileCount = 0;

        foreach (string path in EnumeratePackages(options.SourceDirectory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var pack = DbpfPackage.Load(path);

                // Same rule as the original: the package that makes the counters reach the
                // limit starts the next merged package.
                currentSize += pack.Length;
                currentFileCount += pack.HeaderEntryCount;
                if ((options.MaxPackageBytes != 0 && currentSize >= options.MaxPackageBytes) ||
                    (options.MaxFilesPerPackage != 0 && currentFileCount >= options.MaxFilesPerPackage))
                {
                    current = new PendingPackage();
                    pending.Add(current);
                    currentSize = 0;
                    currentFileCount = 0;
                }

                if (pack.HasCompression)
                    current.Compress = true;
                current.Sources.Add(pack);
                result.FileCount += pack.Entries.Count;
                result.PackagesRead++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                result.Errors.Add(new PackageError(path, e.ToString()));
            }
        }

        pending.RemoveAll(p => p.Sources.Count == 0);

        string targetPath = Path.GetFullPath(options.TargetPath);
        string targetDir = Path.GetDirectoryName(targetPath) ?? options.SourceDirectory;
        string baseName = Path.GetFileNameWithoutExtension(targetPath);

        if (result.FileCount == 0)
        {
            result.NothingToMerge = true;
            if (result.Errors.Count > 0)
                WriteLog(options, result, pending.Count, targetDir);
            return result;
        }

        // ---- 2. Write the merged packages (to temporary files first) ----
        // Writing to temp files and renaming at the end means a previous merged package that
        // is also one of the inputs can't get truncated while we're still reading from it.
        var written = new List<(string Temp, string Final)>();
        long done = 0;
        int lastPercent = -1;

        void OnResourceCopied()
        {
            done++;
            int percent = (int)Math.Min(100, done * 100 / result.FileCount);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress?.Report(new MergeProgress(false, percent));
            }
        }

        try
        {
            progress?.Report(new MergeProgress(false, 0));
            for (int i = 0; i < pending.Count; i++)
            {
                string final = Path.Combine(targetDir, baseName + i.ToString() + ".package");
                string temp = final + ".ccmerger-tmp";
                written.Add((temp, final));
                WritePackage(pending[i], temp, OnResourceCopied, ct);
            }

            foreach (var (temp, final) in written)
            {
                File.Move(temp, final, overwrite: true);
                result.OutputFiles.Add(final);
            }
        }
        catch (OperationCanceledException)
        {
            DeleteQuietly(written.Select(w => w.Temp));
            throw;
        }
        catch (Exception e)
        {
            DeleteQuietly(written.Select(w => w.Temp));
            result.Failed = true;
            result.FailureTrace = e.ToString();
        }

        // ---- 3. Logs ----
        WriteLog(options, result, pending.Count, targetDir);

        if (options.WriteContentLists && !result.Failed)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                string packagePath = result.OutputFiles[i];
                string txtPath = Path.ChangeExtension(packagePath, ".txt");
                var sb = new StringBuilder();
                sb.AppendLine("Contents of " + Path.GetFileName(packagePath) + ":");
                sb.AppendLine();
                foreach (var source in pending[i].Sources)
                    sb.AppendLine(Path.GetFileName(source.Path));
                sb.AppendLine();
                File.WriteAllText(txtPath, sb.ToString(), new UTF8Encoding(false));
            }
        }

        return result;
    }

    // ------------------------------------------------------------------------------------

    private static void WritePackage(PendingPackage group, string tempPath, Action onResourceCopied, CancellationToken ct)
    {
        // Every resource that goes in this package, in index order.
        var items = new List<(DbpfPackage Source, DbpfEntry Entry)>();
        foreach (var source in group.Sources)
            foreach (var entry in source.Entries)
                items.Add((source, entry));

        byte[] dir = group.Compress ? BuildDir(items) : Array.Empty<byte>();

        // Work out the whole layout up front, so we can write the file front to back.
        long entryCount = items.Count + (group.Compress ? 1 : 0);
        long indexOffset = HeaderSize;
        long indexSize = entryCount * IndexEntrySize;
        long dirOffset = indexOffset + indexSize;
        long dataStart = dirOffset + dir.Length;

        var offsets = new long[items.Count];
        long pos = dataStart;
        for (int i = 0; i < items.Count; i++)
        {
            offsets[i] = pos;
            pos += items[i].Entry.FileSize;
        }
        if (pos > uint.MaxValue)
            throw new InvalidOperationException("A merged package would be bigger than 4 GB, which the format can't hold. Lower \"Max package size\".");

        using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var w = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);

        // Header
        w.Write(Encoding.ASCII.GetBytes("DBPF"));
        w.Write(1u);                    // major version
        w.Write(2u);                    // minor version
        w.Write(new byte[12]);
        w.Write(0);                     // date created
        w.Write(0);                     // date modified
        w.Write(7u);                    // index major version
        w.Write((uint)entryCount);
        w.Write((uint)indexOffset);
        w.Write((uint)indexSize);
        w.Write(0u);                    // trash entry count
        w.Write(0u);                    // trash index offset
        w.Write(0u);                    // trash index size
        w.Write(2u);                    // index minor version
        w.Write(new byte[32]);

        // Index
        if (group.Compress)
        {
            w.Write(DbpfPackage.DirType);
            w.Write(DbpfPackage.DirGroup);
            w.Write(DbpfPackage.DirInstance);
            w.Write(0u);
            w.Write((uint)dirOffset);
            w.Write((uint)dir.Length);
        }
        for (int i = 0; i < items.Count; i++)
        {
            var e = items[i].Entry;
            w.Write(e.TypeId);
            w.Write(e.GroupId);
            w.Write(e.InstanceId);
            w.Write(e.InstanceId2);
            w.Write((uint)offsets[i]);
            w.Write(e.FileSize);
        }

        // DIR data, then the resources themselves
        w.Write(dir);
        w.Flush();

        var buffer = new byte[1 << 16];
        foreach (var source in group.Sources)
        {
            if (source.Entries.Count == 0)
                continue;

            // One open file at a time, so it doesn't matter how many packages there are.
            using var src = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (src.Length != source.Length)
                throw new IOException(source.Path + " was changed while merging. Try again.");

            foreach (var e in source.Entries)
            {
                ct.ThrowIfCancellationRequested();
                CopyRange(src, e.FileOffset, e.FileSize, fs, buffer);
                onResourceCopied();
            }
        }

        if (fs.Position != pos)
            throw new InvalidOperationException("Internal error: the merged package has an unexpected size.");
    }

    private static byte[] BuildDir(List<(DbpfPackage Source, DbpfEntry Entry)> items)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var item in items)
        {
            var e = item.Entry;
            if (e.UncompressedSize == 0)
                continue;
            w.Write(e.TypeId);
            w.Write(e.GroupId);
            w.Write(e.InstanceId);
            w.Write(e.InstanceId2);
            w.Write(e.UncompressedSize);
        }
        w.Flush();
        return ms.ToArray();
    }

    private static void CopyRange(Stream source, long offset, long length, Stream destination, byte[] buffer)
    {
        source.Seek(offset, SeekOrigin.Begin);
        long remaining = length;
        while (remaining > 0)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
                throw new EndOfStreamException("Unexpected end of file while copying a resource.");
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void WriteLog(MergeOptions options, MergeResult result, int mergedCount, string targetDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CCMerger log - version " + AppInfo.Version);
        sb.AppendLine();
        sb.AppendLine("[Settings Used]");
        sb.AppendLine("Max files per package: " + options.MaxFilesPerPackage);
        sb.AppendLine("Max package size: " + options.MaxPackageBytes / 1000000 + " mb");
        sb.AppendLine();
        sb.AppendLine("[Results]");
        sb.AppendLine("Merged " + result.PackagesRead + " packages containing " + result.FileCount + " files into " + mergedCount + " packages.");
        sb.AppendLine();
        sb.AppendLine("[Errors]");
        sb.AppendLine(result.Errors.Count + " packages failed to merge.");
        foreach (var error in result.Errors)
        {
            sb.AppendLine("Package name: " + error.File);
            sb.AppendLine(error.Trace);
        }
        if (result.Failed)
        {
            sb.AppendLine("Failed to merge packages :(");
            sb.AppendLine(result.FailureTrace);
        }
        else if (!result.NothingToMerge)
        {
            sb.AppendLine("Packages were merged successfully :)");
        }
        File.WriteAllText(Path.Combine(targetDir, "CCMerger.log"), sb.ToString(), new UTF8Encoding(false));
    }

    private static void DeleteQuietly(IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
    }

    // ------------------------------------------------------------------------------------

    private static readonly Comparison<string> ByName = (a, b) =>
    {
        int c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.CompareOrdinal(a, b);
    };

    /// <summary>
    /// Every *.package under <paramref name="root"/>, in a stable order: the files of a folder
    /// (alphabetical, ignoring case) and then its sub folders. Linux returns directory
    /// entries in no particular order and the file system is case sensitive, so both are
    /// handled explicitly (the order decides which duplicate resource "wins" in game).
    /// </summary>
    public static List<string> EnumeratePackages(string root)
    {
        var found = new List<string>();
        Walk(root, 0, found);
        return found;
    }

    private static void Walk(string dir, int depth, List<string> found)
    {
        string[] files;
        string[] subDirs;
        try
        {
            files = Directory.GetFiles(dir);
            subDirs = Directory.GetDirectories(dir);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return; // can't read this folder, skip it
        }

        Array.Sort(files, ByName);
        Array.Sort(subDirs, ByName);

        foreach (string file in files)
        {
            if (string.Equals(Path.GetExtension(file), ".package", StringComparison.OrdinalIgnoreCase))
                found.Add(file);
        }

        if (depth >= 64)
            return; // protects against symlink loops
        foreach (string subDir in subDirs)
            Walk(subDir, depth + 1, found);
    }
}
