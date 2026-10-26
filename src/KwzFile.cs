using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
namespace LibKwz;

/// <summary>A complete animation KWZ, with lossless section ordering and signature preservation.</summary>
public sealed class KwzFile : IDisposable
{
    private readonly List<(string Magic, byte[]? Unknown)> _sections = [];
    private byte[] _originalBodyHash = [];
    private readonly List<string> _originalChecksumErrors = [];
    /// <summary>Checksum mismatches observed in the original input, before any edits or serialization.</summary>
    public IReadOnlyList<string> OriginalChecksumErrors => _originalChecksumErrors.AsReadOnly();
    public KwzHeader Header { get; private set; } = null!;
    public KwzThumbnail? Thumbnail { get; private set; }
    public KwzFrameData FrameData { get; private set; } = null!;
    public KwzMeta Meta { get; private set; } = null!;
    public KwzSound? Sound { get; private set; }
    public byte[] Signature { get; private set; } = [];
    /// <summary>Updated when writing. A changed body cannot use the original Nintendo RSA signature.</summary>
    public bool SignatureMatchesOriginalBody { get; private set; } = true;
    public IReadOnlyList<string> SectionOrder => _sections.Select(s => s.Magic).ToArray();
    private KwzFile() { }

    /// <summary>Create a blank editable animation. Use SaveSigned or ToSignedArray when exporting for the game.</summary>
    public static KwzFile CreateBlank(int frameCount=1, float framesPerSecond=12)
    {
        if(frameCount is <1 or >ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(frameCount));
        var result = new KwzFile {
            Header = new KwzHeader { FrameCount=1, FrameSpeed=7, LoopPlayback=true,
                CurrentAuthorName="KWZ Studio", ParentAuthorName="KWZ Studio", RootAuthorName="KWZ Studio",
                CreationDateTime=DateTime.UtcNow, LastEditDateTime=DateTime.UtcNow },
            Meta = new KwzMeta(), Thumbnail = new KwzThumbnail(), Sound = new KwzSound { Speed=7 }, Signature = new byte[256]
        };
        result.Header.RegenerateFilenames();
        result.Header.FramesPerSecond=framesPerSecond;
        result.Sound.Speed=result.Header.FrameSpeed;
        result.FrameData = KwzFrameData.CreateBlank(result.Header,result.Meta);
        if(frameCount>1) result.FrameData.RemapFrames(Enumerable.Repeat(0,frameCount).ToArray());
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(80,64,SixLabors.ImageSharp.Color.White);
        using var jpeg = new MemoryStream();
        image.Save(jpeg,new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality=90 });
        while((jpeg.Length&3)!=0) jpeg.WriteByte(0);
        result.Thumbnail.Data=jpeg.ToArray();
        foreach (var magic in new[]{KwzHeader.MAGIC,KwzThumbnail.MAGIC,KwzFrameData.MAGIC,KwzMeta.MAGIC,KwzSound.MAGIC}) result._sections.Add((magic,null));
        var bytes=result.ToArray();
        result._originalBodyHash=SHA256.HashData(bytes.AsSpan(0,bytes.Length-256));
        return result;
    }

    public KwzFile(string filePath,bool verifyChecksums=false)
    {
        using var stream = File.OpenRead(filePath);
        Read(stream,verifyChecksums);
    }
    /// <summary>Reads from the current stream position; leaves the stream open.</summary>
    public KwzFile(Stream stream,bool verifyChecksums=false) => Read(stream,verifyChecksums);
    public static KwzFile ReadFrom(BinaryReader reader) => new(reader.BaseStream);

    private void Read(Stream stream,bool verifyChecksums)
    {
        if (!stream.CanSeek) throw new ArgumentException("KWZ input must be seekable.", nameof(stream));
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var seen = new HashSet<string>();
        byte[]? frames = null;
        byte[]? metadata = null;
        while (stream.Length - stream.Position > 256)
        {
            byte[] sectionHeader = reader.ReadExactlyBytes(8);
            string magic = Encoding.Latin1.GetString(sectionHeader, 0, 4);
            uint size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(sectionHeader.AsSpan(4));
            if (size > int.MaxValue || size > stream.Length - stream.Position - 256) throw new InvalidDataException("KWZ section exceeds file body.");
            if (!seen.Add(magic)) throw new InvalidDataException($"Duplicate KWZ section {magic[..3]}.");
            byte[] payload = reader.ReadExactlyBytes((int)size);
            CheckOriginalChecksum(magic,payload,verifyChecksums);
            hash.AppendData(sectionHeader); hash.AppendData(payload);
            using var section = new MemoryStream(checked((int)size + 8));
            section.Write(sectionHeader); section.Write(payload); section.Position = 0;
            using var br = new BinaryReader(section);
            switch (magic)
            {
                case KwzHeader.MAGIC: Header = KwzHeader.ReadFrom(br); break;
                case KwzThumbnail.MAGIC: Thumbnail = KwzThumbnail.ReadFrom(br); break;
                case KwzFrameData.MAGIC: frames = section.ToArray(); break;
                case KwzMeta.MAGIC: metadata = section.ToArray(); break;
                case KwzSound.MAGIC: Sound = KwzSound.ReadFrom(br); break;
                default: _sections.Add((magic, payload)); continue;
            }
            _sections.Add((magic, null));
        }
        Signature = reader.ReadExactlyBytes(256);
        _originalBodyHash = hash.GetHashAndReset();
        if (Header == null || frames == null || metadata == null) throw new InvalidDataException("Animation KWZ requires KFH, KMI and KMC sections.");
        using var mr = new BinaryReader(new MemoryStream(metadata, writable: false));
        Meta = KwzMeta.ReadFrom(mr, Header);
        using var fr = new BinaryReader(new MemoryStream(frames, writable: false));
        FrameData = KwzFrameData.ReadFrom(fr, Header, Meta);
    }

    private void CheckOriginalChecksum(string magic,byte[] payload,bool strict)
    {
        int crcOffset=magic switch { KwzHeader.MAGIC or KwzThumbnail.MAGIC or KwzFrameData.MAGIC=>0,KwzSound.MAGIC=>24,_=>-1 };
        if(crcOffset<0 || payload.Length<crcOffset+4) return;
        uint stored=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(crcOffset));
        uint computed=System.IO.Hashing.Crc32.HashToUInt32(payload.AsSpan(crcOffset+4));
        if(stored==computed) return;
        string error=$"{magic[..3]} CRC32 mismatch: stored {stored:X8}, computed {computed:X8}.";
        _originalChecksumErrors.Add(error);
        if(strict) throw new InvalidDataException(error);
    }

    /// <summary>Explicitly update the embedded JPEG. Reading or saving alone never regenerates it.</summary>
    public void UpdateThumbnail(int frameIndex,int quality=90)
    {
        if(quality is <1 or >100) throw new ArgumentOutOfRangeException(nameof(quality));
        var (layers,composite)=FrameData.FrameToImage(frameIndex);
        try
        {
            int width=80,height=64;
            if(Thumbnail?.Data.Length>0)
            {
                var info=SixLabors.ImageSharp.Image.Identify(Thumbnail.Data);
                width=info.Width; height=info.Height;
            }
            // JPEG has no alpha. Composite transparent paper over white before
            // discarding alpha, or transparent regions would become black.
            using var image=new Image<Rgb24>(composite.Width,composite.Height,Color.White);
            image.Mutate(ctx=>ctx.DrawImage(composite,1).Resize(width,height));
            using var jpeg=new MemoryStream();
            image.Save(jpeg,new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality=quality });
            while((jpeg.Length&3)!=0) jpeg.WriteByte(0);
            if(Thumbnail==null)
            {
                Thumbnail=new KwzThumbnail();
                int header=_sections.FindIndex(s=>s.Magic==KwzHeader.MAGIC);
                _sections.Insert(header+1,(KwzThumbnail.MAGIC,null));
            }
            Thumbnail.Data=jpeg.ToArray();
            Header.ThumbnailFrameIndex=checked((ushort)frameIndex);
        }
        finally { composite.Dispose(); foreach(var layer in layers) layer.Dispose(); }
    }

    /// <summary>Rebuild every known section, including KMC from pixels and its encoding plan, and preserve opaque fields.</summary>
    public void WriteTo(BinaryWriter writer) => WriteTo(writer,null);
    public void WriteTo(BinaryWriter writer,RSA? signingKey)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if(signingKey!=null && signingKey.KeySize!=2048) throw new ArgumentException("KWZ requires RSA-2048.",nameof(signingKey));
        if (Header.FrameCount != FrameData.FrameCount) throw new InvalidDataException("Header frame count differs from KMI.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (magic, unknown) in _sections)
        {
            using var section = new MemoryStream();
            using var bw = new BinaryWriter(section, Encoding.UTF8, leaveOpen: true);
            switch (magic)
            {
                case KwzHeader.MAGIC: Header.WriteTo(bw); break;
                case KwzThumbnail.MAGIC: Thumbnail!.WriteTo(bw); break;
                case KwzFrameData.MAGIC: FrameData.WriteTo(bw); break;
                case KwzMeta.MAGIC: Meta.WriteTo(bw); break;
                case KwzSound.MAGIC: Sound!.WriteTo(bw); break;
                default: bw.Write(Encoding.Latin1.GetBytes(magic)); bw.Write((uint)unknown!.Length); bw.Write(unknown); break;
            }
            var data = section.GetBuffer().AsSpan(0, (int)section.Length);
            hash.AppendData(data); writer.Write(data);
        }
        byte[] bodyHash=hash.GetHashAndReset();
        SignatureMatchesOriginalBody = bodyHash.AsSpan().SequenceEqual(_originalBodyHash);
        if(signingKey!=null) Signature=signingKey.SignHash(bodyHash,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        writer.Write(Signature);
    }
    public void WriteTo(Stream stream,RSA? signingKey=null)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteTo(writer,signingKey);
    }
    public byte[] ToArray(RSA? signingKey=null)
    {
        using var stream = new MemoryStream(); WriteTo(stream,signingKey); return stream.ToArray();
    }
    public void Save(string path,RSA? signingKey=null)
    {
        // Serialize first, then atomically replace on the same volume. No partial destination or abandoned temporary file.
        byte[] data=ToArray(signingKey);
        string target=Path.GetFullPath(path);
        string temporary=Path.Combine(Path.GetDirectoryName(target)!,"."+Path.GetFileName(target)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { output.Write(data); output.Flush(true); }
            File.Move(temporary,target,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    /// <summary>Serialize and sign using the embedded game-compatible key.</summary>
    public byte[] ToSignedArray()
    {
        using RSA key=KwzSigning.CreateSigningKey();
        return ToArray(key);
    }
    /// <summary>Atomically save and sign using the embedded game-compatible key.</summary>
    public void SaveSigned(string path)
    {
        using RSA key=KwzSigning.CreateSigningKey();
        Save(path,key);
    }
    public bool VerifySignature() => KwzSigning.Verify(ToArray());
    public bool VerifySignature(RSA publicKey) => KwzSigning.Verify(ToArray(),publicKey);
    public void Dispose() => FrameData?.Dispose();
}
