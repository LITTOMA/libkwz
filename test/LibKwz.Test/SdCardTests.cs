using LibKwz;

namespace LibKwz.Test;

public sealed class SdCardTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.GetEnvironmentVariable("KWZ_TEST_TEMP") ?? Path.GetTempPath(), "kwz-sd-" + Guid.NewGuid().ToString("N"));
    private string Folder => Path.Combine(root, "private", "Nintendo 3DS", "app", "JKZJ", "000");
    private string ListPath => Path.Combine(Folder, "!!.lst");
    private static string Name(char c) => new string(c, 28) + ".kwz";
    public SdCardTests() => Directory.CreateDirectory(Folder);

    [Fact]
    public void IndependentBinaryVectorRoundtripsExactly()
    {
        // Python struct.pack + zlib.crc32 + a 61-byte XOR loop; anonymous synthetic names.
        byte[] binary = Convert.FromBase64String("KYsqsgoAlEwJOpiCxTcNEVvPw93mwKSbp9i+KOmHMCCN4GjrIKC4VWx1VTeINQk0HLVsmZTvICWtoEpGutRju29mH/c2ajD79aZAbmY4uKCqhbfH7MSv3V+K8FNX7pcLnEPX2yIPAjZA60JqQ3/CD+73mENSztcpMdmjTcwEEWjZTAE6jILcN2QROM+g3Q==");
        // Includes the final N NULs, the unusual CRC word order, and repeated XOR cycles.
        var expected = new KwzSdList([Name('c'), Name('w')]);
        Assert.Equal(expected.Filenames, KwzSdList.Read(binary).Filenames);
        Assert.Equal(binary, expected.ToArray());
    }

    [Fact]
    public void RejectsCorruptTruncatedAndUnsafeLists()
    {
        byte[] bytes = new KwzSdList([Name('c')]).ToArray();
        bytes[6] ^= 1;
        Assert.Throws<InvalidDataException>(() => KwzSdList.Read(bytes));
        Assert.Throws<InvalidDataException>(() => KwzSdList.Read(new byte[73]));
        Assert.Throws<InvalidDataException>(() => new KwzSdList(["../outside.kwz"]));
        Assert.Throws<InvalidDataException>(() => new KwzSdList([Name('c'), Name('c')]));
        Assert.Throws<InvalidDataException>(() => new KwzSdList(Enumerable.Repeat(Name('c'), 101)));
        Assert.Equal(new KwzSdList([]).ToArray(), KwzSdList.Read(new KwzSdList([]).ToArray()).ToArray());
    }

    [Fact]
    public void DiscoversOnlyExistingGameFolders()
    {
        Assert.Equal([Folder], KwzSdCard.FindFolders(root));
        Assert.Equal([Folder], KwzSdCard.FindFolders(Directory.GetParent(Folder)!.FullName));
        Assert.Equal([Folder], KwzSdCard.FindFolders(Folder));
        Assert.Empty(KwzSdCard.FindFolders(Path.Combine(root, "missing")));
        Directory.CreateDirectory(Path.Combine(root, "000"));
        Assert.Empty(KwzSdCard.FindFolders(Path.Combine(root, "000")));
    }

    [Fact]
    public void RootSelectionRejectsSubfoldersAndMigratesFormerPreferences()
    {
        Assert.True(KwzSdCard.IsSdRoot(root));
        Assert.False(KwzSdCard.IsSdRoot(Folder));
        Assert.False(KwzSdCard.IsSdRoot(Directory.GetParent(Folder)!.FullName));
        Assert.Equal(root, KwzSdCard.GetSdRoot(Folder));
        Assert.Equal(root, KwzSdCard.GetSdRoot(Directory.GetParent(Folder)!.FullName));
        Assert.Equal(root, KwzSdCard.GetSdRoot(root));
        Assert.Null(KwzSdCard.GetSdRoot(Path.Combine(root, "other")));
    }

    [Fact]
    public void CustomEmptyFoldersAreDiscoveredAndCanReceiveImports()
    {
        string parent = Directory.GetParent(Folder)!.FullName;
        string custom = Path.Combine(parent, "my animations");
        Directory.CreateDirectory(custom);
        Assert.False(File.Exists(Path.Combine(custom, "!!.lst")));
        Assert.Equal(new[] { Folder, custom }, KwzSdCard.FindFolders(Folder));
        Assert.Equal(KwzSdCard.FindFolders(root), KwzSdCard.FindFolders(custom));
        using var source = KwzFile.CreateBlank();
        var result = KwzSdCard.Import(custom, source);
        Assert.Single(KwzSdList.Read(File.ReadAllBytes(result.ListPath)).Filenames);
        string later = Path.Combine(parent, "新建文件夹");
        Directory.CreateDirectory(later);
        Assert.Contains(later, KwzSdCard.FindFolders(Folder));
        Directory.Delete(later);
        Assert.DoesNotContain(later, KwzSdCard.FindFolders(Folder));
        Assert.Contains(custom, KwzSdCard.FindFolders(later));
    }

    [Fact]
    public void ImportsSignedCopiesAndPreservesExistingFilesOrderAndSource()
    {
        string first = Name('c'), orphan = Name('w');
        byte[] oldList = new KwzSdList([first]).ToArray();
        File.WriteAllBytes(ListPath, oldList);
        File.WriteAllBytes(Path.Combine(Folder, first), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(Folder, orphan), [4, 5, 6]);
        using var source = KwzFile.CreateBlank(2);
        byte[] pixels = source.FrameData.DecodeFrame(0); pixels[1234] = 1;
        source.FrameData.SetFramePixels(0, pixels);
        byte[] original = source.ToArray();
        var result = KwzSdCard.Import(Folder, source);
        Assert.Equal(oldList, File.ReadAllBytes(result.BackupPath!));
        Assert.Equal(new[] { Path.GetFileName(result.AnimationPath), first, orphan }, KwzSdList.Read(File.ReadAllBytes(ListPath)).Filenames);
        using var imported = new KwzFile(result.AnimationPath, verifyChecksums: true);
        Assert.True(imported.VerifySignature());
        Assert.Equal(imported.Header.CurrentFilename + ".kwz", Path.GetFileName(result.AnimationPath));
        Assert.Equal(source.Header.RootFilename, imported.Header.RootFilename);
        Assert.Equal(source.Header.ParentFilename, imported.Header.ParentFilename);
        Assert.Equal(source.Header.CurrentAuthorId, imported.Header.CurrentAuthorId);
        for (int f = 0; f < 2; f++) Assert.Equal(source.FrameData.DecodeFrame(f), imported.FrameData.DecodeFrame(f));
        for (int t = 0; t < 5; t++) Assert.Equal(source.Sound!.GetAudioTrackRaw(t), imported.Sound!.GetAudioTrackRaw(t));
        var second = KwzSdCard.Import(Folder, source);
        Assert.NotEqual(result.AnimationPath, second.AnimationPath);
        Assert.Equal(original, source.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Folder, first)));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(Path.Combine(Folder, orphan)));
        Assert.Empty(Directory.GetFiles(Folder, "*.tmp"));
        Assert.False(File.Exists(Path.Combine(Folder, ".kwz-studio.lock")));
    }

    [Fact]
    public void MissingListIsCreatedWithoutDroppingUnlistedFiles()
    {
        File.WriteAllBytes(Path.Combine(Folder, Name('w')), [42]);
        using var source = KwzFile.CreateBlank();
        var result = KwzSdCard.Import(Folder, source);
        Assert.Null(result.BackupPath);
        Assert.Equal(2, KwzSdList.Read(File.ReadAllBytes(ListPath)).Filenames.Count);
    }

    [Fact]
    public void CorruptOrFullListLeavesExistingDataUntouched()
    {
        using var source = KwzFile.CreateBlank();
        File.WriteAllBytes(ListPath, [1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => KwzSdCard.Import(Folder, source));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(ListPath));
        var names = Enumerable.Range(0, 100).Select(i =>
        {
            byte[] id = new byte[17]; id[0] = (byte)i;
            return Utils.Base32Codec.Encode(id) + ".kwz";
        });
        byte[] full = new KwzSdList(names).ToArray();
        File.WriteAllBytes(ListPath, full);
        Assert.Throws<InvalidDataException>(() => KwzSdCard.Import(Folder, source));
        Assert.Equal(full, File.ReadAllBytes(ListPath));
        Assert.Single(Directory.GetFiles(Folder));
    }

    [Fact]
    public void FailedListReplacementRemovesOnlyTheNewCopy()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows sharing denies the final rename.
        byte[] original = new KwzSdList([Name('c')]).ToArray();
        File.WriteAllBytes(ListPath, original);
        using var source = KwzFile.CreateBlank();
        using var lockedList = new FileStream(ListPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Record.Exception(() => KwzSdCard.Import(Folder, source));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(original, File.ReadAllBytes(ListPath));
        Assert.Empty(Directory.GetFiles(Folder, "*.kwz"));
        Assert.Empty(Directory.GetFiles(Folder, "*.tmp"));
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(Folder, "*.bak"))));
    }

    [Fact]
    public void OptionalPrivateSampleRoundtripsWithoutPublishingItsContents()
    {
        string? path = Environment.GetEnvironmentVariable("KWZ_SD_LIST_SAMPLE");
        if (path == null) return;
        byte[] original = File.ReadAllBytes(path);
        Assert.True(original.AsSpan().SequenceEqual(KwzSdList.Read(original).ToArray()), "Private catalog must roundtrip byte-for-byte.");
    }

    public void Dispose()
    {
        string absolute = Path.GetFullPath(root);
        string parent = Path.GetFullPath(Environment.GetEnvironmentVariable("KWZ_TEST_TEMP") ?? Path.GetTempPath());
        if (!absolute.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unexpected test directory.");
        Directory.Delete(absolute, recursive: true);
    }
}
