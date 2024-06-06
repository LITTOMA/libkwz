using System.Buffers.Binary;

namespace LibKwz.Test;

public class KwzHeaderTest
{
    [Fact]
    public void TestReadFrom()
    {
        var data = new byte[]
        {
            0x4B, 0x46, 0x48, 0x14, 0xCC, 0x00, 0x00, 0x00, 0xF5, 0x58, 0xF6, 0xF8, 0x0C, 0x83, 0x63, 0x19,
            0x0C, 0x83, 0x63, 0x19, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4E, 0x00, 0x69, 0x00, 0x6E, 0x00, 0x74, 0x00, 0x65, 0x00,
            0x6E, 0x00, 0x64, 0x00, 0x6F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4E, 0x00, 0x69, 0x00,
            0x6E, 0x00, 0x74, 0x00, 0x65, 0x00, 0x6E, 0x00, 0x64, 0x00, 0x6F, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x4E, 0x00, 0x69, 0x00, 0x6E, 0x00, 0x74, 0x00, 0x65, 0x00, 0x6E, 0x00, 0x64, 0x00,
            0x6F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x63, 0x6A, 0x63, 0x63, 0x63, 0x63, 0x63, 0x77,
            0x63, 0x63, 0x63, 0x63, 0x63, 0x63, 0x63, 0x61, 0x6E, 0x6C, 0x74, 0x74, 0x68, 0x66, 0x6A, 0x66,
            0x61, 0x61, 0x61, 0x6E, 0x63, 0x6A, 0x63, 0x63, 0x63, 0x63, 0x63, 0x77, 0x63, 0x63, 0x63, 0x63,
            0x63, 0x63, 0x63, 0x61, 0x6E, 0x6C, 0x74, 0x74, 0x68, 0x66, 0x6A, 0x66, 0x61, 0x61, 0x61, 0x6E,
            0x63, 0x6A, 0x63, 0x63, 0x63, 0x63, 0x63, 0x77, 0x63, 0x63, 0x63, 0x63, 0x63, 0x63, 0x63, 0x61,
            0x6E, 0x6C, 0x74, 0x74, 0x68, 0x66, 0x6A, 0x66, 0x61, 0x61, 0x61, 0x6E, 0x23, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x08, 0x00
        };


        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        var header = KwzHeader.ReadFrom(reader);
        byte[] rootFileNameDecoded = header.RootFilename.KwzBase32Decode();
        byte[] parentFileNameDecoded = header.ParentFilename.KwzBase32Decode();
        byte[] currentFileNameDecoded = header.CurrentFilename.KwzBase32Decode();

        // Assert
        Assert.Equal([.. "KFH\x14"], header.Magic);
        Assert.Equal(204u, header.Size);
        Assert.Equal(0xF8F658F5u, header.Crc32);
        Assert.Equal(0x1963830Cu, header.CreationTimestamp);
        Assert.Equal(0x1963830Cu, header.LastEditTimestamp);
        Assert.Equal(0x00000000u, header.AppVersion);
        Assert.Equal([0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00], header.RootAuthorId);
        Assert.Equal([0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00], header.ParentAuthorId);
        Assert.Equal([0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00], header.CurrentAuthorId);
        Assert.Equal("Nintendo", header.RootAuthorName);
        Assert.Equal("Nintendo", header.ParentAuthorName);
        Assert.Equal("Nintendo", header.CurrentAuthorName);
        Assert.Equal("cjcccccwcccccccanltthfjfaaan", header.RootFilename);
        Assert.Equal("cjcccccwcccccccanltthfjfaaan", header.ParentFilename);
        Assert.Equal("cjcccccwcccccccanltthfjfaaan", header.CurrentFilename);
        Assert.Equal(35, header.FrameCount);
        Assert.Equal(0, header.ThumbnailFrameIndex);
        Assert.Equal(2, header.Flags);
        Assert.Equal(8, header.FrameSpeed);
        Assert.Equal(0, header.LayerVisibilityFlags);

        Assert.Equal(rootFileNameDecoded.AsSpan()[..9].ToArray(), header.RootAuthorId.AsSpan()[..9].ToArray());
        Assert.Equal(parentFileNameDecoded.AsSpan()[..9].ToArray(), header.ParentAuthorId.AsSpan()[..9].ToArray());
        Assert.Equal(currentFileNameDecoded.AsSpan()[..9].ToArray(), header.CurrentAuthorId.AsSpan()[..9].ToArray());

        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(rootFileNameDecoded.AsSpan()[9..]), header.CreationTimestamp);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(rootFileNameDecoded.AsSpan()[13..]), header.LastEditTimestamp);

        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(parentFileNameDecoded.AsSpan()[9..]), header.CreationTimestamp);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(parentFileNameDecoded.AsSpan()[13..]), header.LastEditTimestamp);

        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(currentFileNameDecoded.AsSpan()[9..]), header.CreationTimestamp);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(currentFileNameDecoded.AsSpan()[13..]), header.LastEditTimestamp);        
    }

    [Fact]
    public void TestWriteTo()
    {
        var header = new KwzHeader
        {
            Crc32 = 0xF8F658F5,
            CreationTimestamp = 0x1963830C,
            LastEditTimestamp = 0x1963830C,
            AppVersion = 0,
            RootAuthorId = [0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00],
            ParentAuthorId = [0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00],
            CurrentAuthorId = [0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00],
            RootAuthorName = "Nintendo",
            ParentAuthorName = "Nintendo",
            CurrentAuthorName = "Nintendo",
            RootFilename = "cjcccccwcccccccanltthfjfaaan",
            ParentFilename = "cjcccccwcccccccanltthfjfaaan",
            CurrentFilename = "cjcccccwcccccccanltthfjfaaan",
            FrameCount = 35,
            ThumbnailFrameIndex = 0,
            Flags = 2,
            FrameSpeed = 8,
            LayerVisibilityFlags = 0
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        header.WriteTo(writer);
        stream.Seek(0, SeekOrigin.Begin);
        var reader = new BinaryReader(stream);
        var newHeader = KwzHeader.ReadFrom(reader);

        Assert.Equal(header.Magic, newHeader.Magic);
        Assert.Equal(header.Size, newHeader.Size);
        Assert.Equal(header.Crc32, newHeader.Crc32);
        Assert.Equal(header.CreationTimestamp, newHeader.CreationTimestamp);
        Assert.Equal(header.LastEditTimestamp, newHeader.LastEditTimestamp);
        Assert.Equal(header.AppVersion, newHeader.AppVersion);
        Assert.Equal(header.RootAuthorId, newHeader.RootAuthorId);
        Assert.Equal(header.ParentAuthorId, newHeader.ParentAuthorId);
        Assert.Equal(header.CurrentAuthorId, newHeader.CurrentAuthorId);
        Assert.Equal(header.RootAuthorName, newHeader.RootAuthorName);
        Assert.Equal(header.ParentAuthorName, newHeader.ParentAuthorName);
        Assert.Equal(header.CurrentAuthorName, newHeader.CurrentAuthorName);
        Assert.Equal(header.RootFilename, newHeader.RootFilename);
        Assert.Equal(header.ParentFilename, newHeader.ParentFilename);
        Assert.Equal(header.CurrentFilename, newHeader.CurrentFilename);
        Assert.Equal(header.FrameCount, newHeader.FrameCount);
        Assert.Equal(header.ThumbnailFrameIndex, newHeader.ThumbnailFrameIndex);
        Assert.Equal(header.Flags, newHeader.Flags);
        Assert.Equal(header.FrameSpeed, newHeader.FrameSpeed);
        Assert.Equal(header.LayerVisibilityFlags, newHeader.LayerVisibilityFlags);
    }

    [Fact]
    public void TestWriteTo_ChangeDateTime()
    {
        var header = new KwzHeader
        {
            Crc32 = 0xF8F658F5,
            CreationTimestamp = 0x1963830C,
            LastEditTimestamp = 0x1963830C,
            AppVersion = 0,
            RootAuthorId = [0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00],
            ParentAuthorId = [0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00],
            CurrentAuthorId = [0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00],
            RootAuthorName = "Nintendo",
            ParentAuthorName = "Nintendo",
            CurrentAuthorName = "Nintendo",
            RootFilename = "cjcccccwcccccccanltthfjfaaan",
            ParentFilename = "cjcccccwcccccccanltthfjfaaan",
            CurrentFilename = "cjcccccwcccccccanltthfjfaaan",
            FrameCount = 35,
            ThumbnailFrameIndex = 0,
            Flags = 2,
            FrameSpeed = 8,
            LayerVisibilityFlags = 0
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        header.WriteTo(writer);
        stream.Seek(0, SeekOrigin.Begin);
        var reader = new BinaryReader(stream);
        var newHeader = KwzHeader.ReadFrom(reader);

        newHeader.CreationDateTime = new DateTime(2022, 1, 1);
        newHeader.LastEditDateTime = new DateTime(2022, 1, 1);

        using var stream2 = new MemoryStream();
        using var writer2 = new BinaryWriter(stream2);
        newHeader.WriteTo(writer2);
        stream2.Seek(0, SeekOrigin.Begin);
        var reader2 = new BinaryReader(stream2);
        var newHeader2 = KwzHeader.ReadFrom(reader2);

        Assert.Equal(header.Magic, newHeader2.Magic);
        Assert.Equal(header.Size, newHeader2.Size);
        Assert.NotEqual(header.Crc32, newHeader2.Crc32);
        Assert.NotEqual(header.CreationTimestamp, newHeader2.CreationTimestamp);
        Assert.NotEqual(header.LastEditTimestamp, newHeader2.LastEditTimestamp);
        Assert.Equal(header.AppVersion, newHeader2.AppVersion);
        Assert.Equal(header.RootAuthorId, newHeader2.RootAuthorId);
        Assert.Equal(header.ParentAuthorId, newHeader2.ParentAuthorId);
        Assert.Equal(header.CurrentAuthorId, newHeader2.CurrentAuthorId);
        Assert.Equal(header.RootAuthorName, newHeader2.RootAuthorName);
        Assert.Equal(header.ParentAuthorName, newHeader2.ParentAuthorName);
        Assert.Equal(header.CurrentAuthorName, newHeader2.CurrentAuthorName);
        Assert.NotEqual(header.RootFilename, newHeader2.RootFilename);
        Assert.NotEqual(header.ParentFilename, newHeader2.ParentFilename);
        Assert.NotEqual(header.CurrentFilename, newHeader2.CurrentFilename);
        Assert.Equal(header.FrameCount, newHeader2.FrameCount);
        Assert.Equal(header.ThumbnailFrameIndex, newHeader2.ThumbnailFrameIndex);
        Assert.Equal(header.Flags, newHeader2.Flags);
        Assert.Equal(header.FrameSpeed, newHeader2.FrameSpeed);
        Assert.Equal(header.LayerVisibilityFlags, newHeader2.LayerVisibilityFlags);

        Assert.Equal(new DateTime(2022, 1, 1), newHeader2.CreationDateTime);
        Assert.Equal(new DateTime(2022, 1, 1), newHeader2.LastEditDateTime);

        byte[] rootFileNameDecoded = newHeader2.RootFilename.KwzBase32Decode();
        byte[] parentFileNameDecoded = newHeader2.ParentFilename.KwzBase32Decode();
        byte[] currentFileNameDecoded = newHeader2.CurrentFilename.KwzBase32Decode();

        Assert.Equal(rootFileNameDecoded.AsSpan()[..9].ToArray(), newHeader2.RootAuthorId.AsSpan()[..9].ToArray());
        Assert.Equal(parentFileNameDecoded.AsSpan()[..9].ToArray(), newHeader2.ParentAuthorId.AsSpan()[..9].ToArray());
        Assert.Equal(currentFileNameDecoded.AsSpan()[..9].ToArray(), newHeader2.CurrentAuthorId.AsSpan()[..9].ToArray());

        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(rootFileNameDecoded.AsSpan()[9..]), newHeader2.CreationTimestamp);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(rootFileNameDecoded.AsSpan()[13..]), newHeader2.LastEditTimestamp);

        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(parentFileNameDecoded.AsSpan()[9..]), newHeader2.CreationTimestamp);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(parentFileNameDecoded.AsSpan()[13..]), newHeader2.LastEditTimestamp);

        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(currentFileNameDecoded.AsSpan()[9..]), newHeader2.CreationTimestamp);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(currentFileNameDecoded.AsSpan()[13..]), newHeader2.LastEditTimestamp);
    }
}