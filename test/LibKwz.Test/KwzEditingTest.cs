using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LibKwz.Test;

public class KwzEditingTest
{
    private static KwzFile Sample() => new(Path.Combine(AppContext.BaseDirectory,"sound.kwz"));
    private static RSA GameKey() => KwzSigning.CreateSigningKey();
    private static byte[] Pixels(byte value) { var p=new byte[KwzFrameData.FramePixelCount]; p.AsSpan().Fill(value); return p; }
    private static KwzFile Read(byte[] bytes) => new(new MemoryStream(bytes),verifyChecksums:true);

    [Fact]
    public void AssemblyResourcesProvideIndependentSigningAndPublicOnlyVerificationKeys()
    {
        var assembly=typeof(KwzSigning).Assembly;
        using var privateResource=assembly.GetManifestResourceStream("LibKwz.Resources.KwzSigningKey.der");
        using var publicResource=assembly.GetManifestResourceStream("LibKwz.Resources.KwzVerificationKey.der");
        Assert.NotNull(privateResource); Assert.Equal(1192,privateResource.Length);
        Assert.NotNull(publicResource); Assert.Equal(270,publicResource.Length);
        using var signer=KwzSigning.CreateSigningKey();
        using var verifier=KwzSigning.CreateVerificationKey();
        Assert.Equal(signer.ExportRSAPublicKey(),verifier.ExportRSAPublicKey());
        Assert.Throws<CryptographicException>(()=>verifier.ExportRSAPrivateKey());
        using var anotherSigner=KwzSigning.CreateSigningKey();
        signer.Dispose();
        using var note=KwzFile.CreateBlank();
        Assert.True(KwzSigning.Verify(note.ToArray(anotherSigner),verifier));
    }

    [Fact]
    public void BuiltInSigningApisWorkWithoutExternalKeyOrExecutablePaths()
    {
        using var note=KwzFile.CreateBlank(2);
        Assert.False(note.VerifySignature());
        byte[] unsigned=note.ToArray();
        byte[] signed=KwzSigning.Sign(unsigned);
        Assert.True(KwzSigning.Verify(signed));
        Assert.Equal(unsigned.AsSpan(0,unsigned.Length-256).ToArray(),signed.AsSpan(0,signed.Length-256).ToArray());
        Assert.Equal(signed,note.ToSignedArray());
        Assert.True(note.VerifySignature());
        signed[16]^=1; Assert.False(KwzSigning.Verify(signed));
        Assert.False(KwzSigning.Verify(new byte[12]));
        string path=Path.Combine(AppContext.BaseDirectory,"embedded-signing-"+Guid.NewGuid().ToString("N")+".kwz");
        try
        {
            note.SaveSigned(path);
            using var reread=new KwzFile(path,verifyChecksums:true);
            Assert.True(reread.VerifySignature());
            Assert.Equal(note.ToSignedArray(),File.ReadAllBytes(path));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void NewAnimationHasAllSectionsAndCanBeSignedAndReopened()
    {
        using var note=KwzFile.CreateBlank(3,24);
        Assert.Equal(3,note.Header.FrameCount); Assert.Equal(24f,note.Header.FramesPerSecond);
        Assert.Equal(28,note.Header.CurrentFilename.Length);
        using var key=GameKey(); byte[] bytes=note.ToArray(key);
        Assert.Equal(0,bytes.Length%4);
        using var reread=Read(bytes);
        Assert.True(reread.VerifySignature(key));
        Assert.Equal(5,reread.SectionOrder.Count);
        Assert.Equal(bytes,reread.ToArray());
        for(int f=0;f<3;f++) Assert.Equal(Pixels(0),reread.FrameData.DecodeFrame(f));
        for(int t=0;t<5;t++) Assert.Empty(reread.Sound!.GetAudioTrack(t));
        using var thumb=Image.Load(reread.Thumbnail!.Data);
        Assert.Equal(80,thumb.Width); Assert.Equal(64,thumb.Height);
    }

    [Fact]
    public void EmbeddedKeyReproducesEveryRomfsSignature()
    {
        using var key=GameKey();
        var files=Directory.GetFiles(Path.Combine(KwzFileTest.CorpusRoot,"unpack"),"*.kwz",SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach(string path in files)
        {
            byte[] original=File.ReadAllBytes(path);
            Assert.True(KwzSigning.Verify(original,key),path);
            using var note=Read(original);
            Assert.Equal(original,note.ToArray(key));
        }
    }

    [Fact]
    public void EditedContentRequiresFreshSignatureAndSurvivesSerialization()
    {
        using var note=Sample(); using var key=GameKey();
        note.Header.CurrentAuthorName="中文汉化";
        note.FrameData.SetLayerPixels(0,1,Enumerable.Repeat((byte)2,KwzLayerCodec.PixelCount).ToArray());
        Assert.False(note.VerifySignature(key));
        byte[] signed=note.ToArray(key);
        Assert.True(KwzSigning.Verify(signed,key));
        Assert.False(note.SignatureMatchesOriginalBody);
        using var reread=Read(signed);
        Assert.Equal("中文汉化",reread.Header.CurrentAuthorName);
        Assert.Equal(signed,reread.ToArray());
        signed[12]^=1;
        Assert.False(KwzSigning.Verify(signed,key));
        using var wrong=RSA.Create(2048); Assert.False(reread.VerifySignature(wrong));
    }

    [Fact]
    public void TimelineOperationsPreservePixelsAuthorsEffectsAndThumbnailIndex()
    {
        using var note=Sample();
        int last=note.Header.FrameCount-1;
        note.Header.ThumbnailFrameIndex=(ushort)last;
        note.Meta.Entries[last].SoundEffectFlags=9;
        note.Meta.Entries[last].Unknown=0x1234;
        var indices=new[]{last,0,last,-1,1};
        var expected=indices.Select(i=>i<0?Pixels(0):note.FrameData.DecodeFrame(i)).ToArray();
        byte[] author=(byte[])note.Meta.Entries[last].FrameAuthorId.Clone();
        note.FrameData.RemapFrames(indices);
        Assert.Equal(0,note.Header.ThumbnailFrameIndex);
        Assert.Equal(5,note.Header.FrameCount);
        Assert.Equal(9,note.Meta.Entries[2].SoundEffectFlags);
        Assert.Equal(0x1234,note.Meta.Entries[2].Unknown);
        Assert.Equal(author,note.Meta.Entries[2].FrameAuthorId);
        note.Meta.Entries[0].FrameAuthorId[0]^=1;
        Assert.Equal(author,note.Meta.Entries[2].FrameAuthorId);
        Assert.Equal(0,note.Meta.Entries[3].SoundEffectFlags);
        using var reread=Read(note.ToArray());
        for(int i=0;i<5;i++) Assert.Equal(expected[i],reread.FrameData.DecodeFrame(i));
    }

    [Fact]
    public void InsertDuplicateMoveAndDeleteHandleFirstAndLastFrames()
    {
        using var note=KwzFile.CreateBlank(2);
        note.FrameData.SetFramePixels(0,Pixels(1));
        note.FrameData.SetFramePixels(1,Pixels(2));
        note.FrameData.DuplicateFrame(1,0); // 2,1,2
        note.FrameData.InsertBlankFrame(3); // 2,1,2,0
        note.FrameData.MoveFrame(0,3); // 1,2,0,2
        note.FrameData.DeleteFrame(0); // 2,0,2
        using var reread=Read(note.ToArray());
        Assert.Equal(Pixels(2),reread.FrameData.DecodeFrame(0));
        Assert.Equal(Pixels(0),reread.FrameData.DecodeFrame(1));
        Assert.Equal(Pixels(2),reread.FrameData.DecodeFrame(2));
    }

    [Fact]
    public void IdentityRemappingAndInvalidOperationsDoNotRewriteOriginalData()
    {
        using var note=Sample(); byte[] original=note.ToArray();
        note.FrameData.RemapFrames(Enumerable.Range(0,note.Header.FrameCount).ToArray());
        note.FrameData.MoveFrame(0,0);
        note.FrameData.SetFramePixels(0,note.FrameData.DecodeFrame(0));
        note.Sound!.SetAudioTrackRaw(0,note.Sound.GetAudioTrackRaw(0));
        Assert.Equal(original,note.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(()=>note.FrameData.RemapFrames([]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>note.FrameData.RemapFrames([note.Header.FrameCount]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>note.FrameData.InsertBlankFrame(-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>note.FrameData.DuplicateFrame(0,note.Header.FrameCount+1));
        Assert.Throws<ArgumentException>(()=>note.FrameData.SetLayerPixels(0,0,new byte[10]));
        Assert.Equal(original,note.ToArray());
        using var blank=KwzFile.CreateBlank();
        Assert.Throws<InvalidOperationException>(()=>blank.FrameData.DeleteFrame(0));
    }

    [Fact]
    public void LayerEditDoesNotLeakIntoOtherLayersOrFollowingFrames()
    {
        using var note=Sample();
        byte[] before=note.FrameData.DecodeFrame(0), next=note.FrameData.DecodeFrame(1);
        byte[] layer=Enumerable.Repeat((byte)2,KwzLayerCodec.PixelCount).ToArray();
        note.FrameData.SetLayerPixels(0,1,layer);
        byte[] after=note.FrameData.DecodeFrame(0);
        Assert.Equal(before[..KwzLayerCodec.PixelCount],after[..KwzLayerCodec.PixelCount]);
        Assert.Equal(before[(2*KwzLayerCodec.PixelCount)..],after[(2*KwzLayerCodec.PixelCount)..]);
        Assert.Equal(layer,note.FrameData.DecodeLayer(0,1));
        Assert.Equal(next,note.FrameData.DecodeFrame(1));
    }

    [Fact]
    public void ThumbnailIsUpdatedOnlyWhenRequestedAndKeepsItsDimensions()
    {
        using var note=Sample();
        byte[] old=(byte[])note.Thumbnail!.Data.Clone();
        var size=Image.Identify(old);
        note.FrameData.SetFramePixels(0,Pixels(1));
        note.Meta.Entries[0].LayerAFirstColorIndex=2;
        note.Header.LayerVisibilityFlags=0;
        note.Meta.Entries[0].LayerADepth=0;
        note.Meta.Entries[0].LayerBDepth=note.Meta.Entries[0].LayerCDepth=1;
        note.ToArray(); Assert.Equal(old,note.Thumbnail.Data);
        note.UpdateThumbnail(0);
        using var reread=Read(note.ToArray());
        using var jpeg=Image.Load<Rgb24>(reread.Thumbnail!.Data);
        Assert.Equal(size.Width,jpeg.Width); Assert.Equal(size.Height,jpeg.Height);
        Assert.True(jpeg[0,0].R>200 && jpeg[0,0].G<60);
        Assert.NotEqual(old,reread.Thumbnail.Data);
    }

    [Fact]
    public void StrictChecksumReadingRejectsCorruptionWithoutSilentlyRepairingIt()
    {
        using var note=Sample(); byte[] bytes=note.ToArray(); bytes[12]^=1;
        Assert.Throws<InvalidDataException>(()=>Read(bytes));
        using var permissive=new KwzFile(new MemoryStream(bytes));
        Assert.Single(permissive.OriginalChecksumErrors);
        using var repaired=Read(permissive.ToArray());
        Assert.Empty(repaired.OriginalChecksumErrors);
    }

    [Fact]
    public void AudioReplacementPreservesOtherTracksAndRegeneratesAlignment()
    {
        using var note=Sample(); var sound=note.Sound!;
        var other=Enumerable.Range(1,4).Select(sound.GetAudioTrackRaw).ToArray();
        sound.SetAudioTrackRaw(0,[0,0,0]);
        using var reread=Read(note.ToArray());
        Assert.Equal(new byte[]{0,0,0},reread.Sound!.GetAudioTrackRaw(0));
        for(int t=1;t<5;t++) Assert.Equal(other[t-1],reread.Sound.GetAudioTrackRaw(t));
        Assert.Throws<ArgumentException>(()=>sound.SetAudioTrack(0,[1]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>sound.SetAudioTrackRaw(5,[]));
        sound.SetAudioTrack(0,[]); Assert.Empty(sound.GetAudioTrack(0));
    }

    [Fact]
    public void CompatibilityImageViewsRefreshAfterPaletteAndVisibilityChanges()
    {
        using var note=KwzFile.CreateBlank();
        note.FrameData.SetFramePixels(0,Pixels(1));
        _=note.FrameData.CompositeFrames;
        note.Meta.Entries[0].LayerAFirstColorIndex=2;
        var red=(Image<Rgba32>)note.FrameData.CompositeFrames[0];
        Assert.Equal(note.Meta.Entries[0].LayerAFirstColor.ToPixel<Rgba32>(),red[0,0]);
        note.Header.LayerAVisible=false;
        var beneath=(Image<Rgba32>)note.FrameData.CompositeFrames[0];
        Assert.Equal(note.Meta.Entries[0].LayerBFirstColor.ToPixel<Rgba32>(),beneath[0,0]);
    }

    [Fact]
    public void FailedSaveRetainsExistingFileAndSuccessfulSaveLeavesNoTemporaryFiles()
    {
        string dir=Directory.CreateTempSubdirectory("libkwz-save-").FullName;
        string path=Path.Combine(dir,"note.kwz");
        try
        {
            byte[] sentinel=[1,2,3,4]; File.WriteAllBytes(path,sentinel);
            using var note=KwzFile.CreateBlank(); using var wrong=RSA.Create(1024);
            Assert.Throws<ArgumentException>(()=>note.Save(path,wrong));
            Assert.Equal(sentinel,File.ReadAllBytes(path));
            using var key=GameKey(); note.Save(path,key);
            using var reread=new KwzFile(path,true); Assert.True(reread.VerifySignature(key));
            Assert.Single(Directory.GetFiles(dir));
            using(var locked=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                var error=Record.Exception(()=>note.Save(path,key));
                Assert.True(error is IOException or UnauthorizedAccessException);
            }
            Assert.Single(Directory.GetFiles(dir));
            Assert.Equal(reread.ToArray(),File.ReadAllBytes(path));
        }
        finally { foreach(var p in Directory.GetFiles(dir)) File.Delete(p); Directory.Delete(dir); }
    }
}
