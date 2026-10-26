using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LibKwz;

public sealed record KwzSdImportResult(string AnimationPath, string ListPath, string? BackupPath);

/// <summary>Imports a new signed copy into an existing game SD folder. Close the game first.</summary>
public static class KwzSdCard
{
    private static readonly string[] Regions = ["JKZJ", "JKZE", "JKZP"];

    public static bool IsSdRoot(string path) => Regions.Any(r =>
        Directory.Exists(Path.Combine(Path.GetFullPath(path), "private", "Nintendo 3DS", "app", r)));

    /// <summary>Converts an explicitly supplied former target preference into its SD root.</summary>
    public static string? GetSdRoot(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (IsAnimationFolder(directory)) directory = directory.Parent!;
        if (IsAppFolder(directory)) directory = directory.Parent!.Parent!.Parent!.Parent!;
        return IsSdRoot(directory.FullName) ? directory.FullName : null;
    }

    /// <summary>Rescans an SD root, a region folder, or an animation folder (including its siblings).</summary>
    public static IReadOnlyList<string> FindFolders(string selectedPath)
    {
        var selected = new DirectoryInfo(Path.GetFullPath(selectedPath));
        // Selecting one work folder still lists its siblings, including custom-named empty folders.
        // Resolve the scope before checking existence so a deleted selection does not hide siblings.
        if (IsAnimationFolder(selected)) selected = selected.Parent!;
        if (!selected.Exists) return [];
        IEnumerable<DirectoryInfo> apps = IsAppFolder(selected)
            ? [selected] : Regions.Select(r => new DirectoryInfo(Path.Combine(selected.FullName, "private", "Nintendo 3DS", "app", r)));
        return apps.Where(a => a.Exists).SelectMany(a => a.EnumerateDirectories())
            .Where(d => IsAnimationFolder(d) && (d.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(a => a.FullName).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsAnimationFolder(DirectoryInfo folder) =>
        folder.Parent is { } app && IsAppFolder(app);

    private static bool IsAppFolder(DirectoryInfo app) =>
        Regions.Contains(app.Name, StringComparer.Ordinal) && app.Parent?.Name == "app" &&
        app.Parent.Parent?.Name == "Nintendo 3DS" && app.Parent.Parent.Parent?.Name == "private";

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("SD import does not follow symbolic links inside the game's folders.");
    }

    /// <summary>
    /// Keeps existing animations and list order, includes unlisted valid KWZ filenames, then prepends
    /// the imported copy. The caller's note is unchanged. Every import gets an unused metadata filename.
    /// </summary>
    public static KwzSdImportResult Import(string folder, KwzFile source)
    {
        folder = Path.GetFullPath(folder);
        var directory = new DirectoryInfo(folder);
        if (!directory.Exists || !IsAnimationFolder(directory))
            throw new DirectoryNotFoundException("Choose an existing animation folder under private/Nintendo 3DS/app/JKZJ, JKZE or JKZP.");
        for (DirectoryInfo? d = directory; d != null && d.Name != "private"; d = d.Parent) RejectLink(d.FullName);
        RejectLink(directory.Parent!.Parent!.Parent!.Parent!.FullName);
        // Cooperating Studio processes cannot interleave the two-file transaction.
        string lockPath = Path.Combine(folder, ".kwz-studio.lock");
        if (File.Exists(lockPath)) RejectLink(lockPath);
        using var importLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, 1, FileOptions.DeleteOnClose);
        string listPath = Path.Combine(folder, "!!.lst");
        byte[]? original = ReadList(listPath);
        var names = original == null ? new List<string>() : KwzSdList.Read(original).Filenames.ToList();
        string[] existing = AnimationNames(folder);
        var occupied = new HashSet<string>(names.Concat(existing), StringComparer.OrdinalIgnoreCase);
        foreach (string name in existing.Where(KwzSdList.IsValidFilename))
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        if (names.Count >= KwzSdList.MaxEntries)
            throw new InvalidDataException("This folder is full (100 animations). Choose another game folder.");

        using var note = new KwzFile(new MemoryStream(source.ToArray()), verifyChecksums: true);
        uint timestamp = checked((uint)(DateTime.UtcNow - new DateTime(2000, 1, 1)).TotalSeconds);
        string filename;
        do
        {
            note.Header.LastEditTimestamp = timestamp++;
            byte[] identity = new byte[17];
            note.Header.CurrentAuthorId.AsSpan(0, 9).CopyTo(identity);
            BinaryPrimitives.WriteUInt32LittleEndian(identity.AsSpan(9), note.Header.CreationTimestamp);
            BinaryPrimitives.WriteUInt32LittleEndian(identity.AsSpan(13), note.Header.LastEditTimestamp);
            note.Header.CurrentFilename = Encoding.ASCII.GetString(identity.KwzBase32Encode());
            filename = note.Header.CurrentFilename + ".kwz";
        } while (occupied.Contains(filename) || File.Exists(Path.Combine(folder, filename)) || Directory.Exists(Path.Combine(folder, filename)));
        note.UpdateThumbnail(Math.Min(note.Header.ThumbnailFrameIndex, note.Header.FrameCount - 1));
        byte[] animation = note.ToSignedArray();
        using (var check = new KwzFile(new MemoryStream(animation), verifyChecksums: true))
        {
            if (!check.VerifySignature()) throw new InvalidDataException("The exported KWZ signature is invalid.");
            for (int f = 0; f < check.Header.FrameCount; f++) _ = check.FrameData.DecodeFrame(f);
        }
        names.Insert(0, filename);
        byte[] updated = new KwzSdList(names).ToArray();
        string id = Guid.NewGuid().ToString("N");
        string animationPath = Path.Combine(folder, filename);
        string animationTemp = Path.Combine(folder, ".kwz-studio-" + id + ".kwz.tmp");
        string listTemp = Path.Combine(folder, ".kwz-studio-" + id + ".lst.tmp");
        string? backup = original == null ? null : Path.Combine(folder, "!!.lst.studio-" + id + ".bak");
        string backupTemp = Path.Combine(folder, ".kwz-studio-" + id + ".bak.tmp");
        bool installed = false, committed = false;
        try
        {
            WriteNew(animationTemp, animation);
            WriteNew(listTemp, updated);
            VerifyBytes(animationTemp, animation);
            VerifyBytes(listTemp, updated);
            EnsureUnchanged(listPath, original, folder, existing);
            if (backup != null)
            {
                WriteNew(backupTemp, original!); VerifyBytes(backupTemp, original!);
                File.Move(backupTemp, backup, overwrite: false);
            }
            EnsureUnchanged(listPath, original, folder, existing);
            // Install the animation first: the list must never point at a partial/missing import.
            File.Move(animationTemp, animationPath, overwrite: false);
            installed = true;
            File.Move(listTemp, listPath, overwrite: original != null);
            committed = true;
            VerifyBytes(animationPath, animation);
            VerifyBytes(listPath, updated);
            return new(animationPath, listPath, backup);
        }
        finally
        {
            // After a committed rename, retain the consistent pair even if a readback failed.
            // Never roll back another process's changes. The tiny original list backup is retained.
            if (installed && !committed && File.Exists(animationPath) && HasBytes(animationPath, animation)) File.Delete(animationPath);
            if (File.Exists(animationTemp)) File.Delete(animationTemp);
            if (File.Exists(listTemp)) File.Delete(listTemp);
            if (File.Exists(backupTemp)) File.Delete(backupTemp);
        }
    }

    private static string[] AnimationNames(string folder) => Directory.EnumerateFiles(folder)
        .Select(Path.GetFileName).Where(n => n != null && n.EndsWith(".kwz", StringComparison.OrdinalIgnoreCase))
        .Select(n => n!).Order(StringComparer.Ordinal).ToArray();

    private static byte[]? ReadList(string path)
    {
        if (!File.Exists(path)) return null;
        RejectLink(path);
        using var file = File.OpenRead(path);
        if (file.Length > KwzSdList.MaxFileSize) throw new InvalidDataException("The !!.lst file exceeds the game's size limit.");
        byte[] data = new byte[checked((int)file.Length)]; file.ReadExactly(data); return data;
    }

    private static void EnsureUnchanged(string listPath, byte[]? original, string folder, string[] existing)
    {
        byte[]? current = ReadList(listPath);
        if ((original == null) != (current == null) || (original != null && !original.AsSpan().SequenceEqual(current)) ||
            !AnimationNames(folder).SequenceEqual(existing))
            throw new IOException("The SD folder changed during import. Close the game and try again.");
    }

    private static void WriteNew(string path, byte[] data)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(data); file.Flush(flushToDisk: true);
    }
    private static bool HasBytes(string path, byte[] expected)
    {
        using var file = File.OpenRead(path);
        return file.Length == expected.Length && SHA256.HashData(file).AsSpan().SequenceEqual(SHA256.HashData(expected));
    }
    private static void VerifyBytes(string path, byte[] expected)
    {
        if (!HasBytes(path, expected)) throw new IOException("SD import readback verification failed. Retain the list backup.");
    }
}
