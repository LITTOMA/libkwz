using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace LibKwz;

public class KwzHeader
{
    public readonly DateTime Epoch = new(2000, 1, 1);

    // Private fields for internal use
    private char[] _magic = [.."KFH\x14"];
    private uint _size = 0xCC;
    private uint _crc32;
    private uint _creationTimestamp;
    private uint _lastEditTimestamp;
    private uint _appVersion;
    private byte[] _rootAuthorId = new byte[10];
    private byte[] _parentAuthorId = new byte[10];
    private byte[] _currentAuthorId = new byte[10];
    private char[] _rootAuthorName = new char[11];
    private char[] _parentAuthorName = new char[11];
    private char[] _currentAuthorName = new char[11];
    private byte[] _rootFilename = new byte[28];
    private byte[] _parentFilename = new byte[28];
    private byte[] _currentFilename = new byte[28];
    private ushort _frameCount;
    private ushort _thumbnailFrameIndex;
    private ushort _flags;
    private byte _frameSpeed;
    private byte _layerVisibilityFlags;

    // Public properties for external access
    public char[] Magic { get => _magic; }
    public uint Size { get => _size; }
    public uint Crc32 { get => _crc32; set => _crc32 = value; }
    public uint CreationTimestamp { get => _creationTimestamp; set => _creationTimestamp = value; }
    public uint LastEditTimestamp { get => _lastEditTimestamp; set => _lastEditTimestamp = value; }
    // Timestamps are stored as the number of seconds since midnight 1 Jan 2000.
    public DateTime CreationDateTime { get => Epoch.AddSeconds(_creationTimestamp); set => _creationTimestamp = (uint)(value - Epoch).TotalSeconds; }
    public DateTime LastEditDateTime { get => Epoch.AddSeconds(_lastEditTimestamp); set => _lastEditTimestamp = (uint)(value - Epoch).TotalSeconds; }
    public uint AppVersion { get => _appVersion; set => _appVersion = value; }
    public byte[] RootAuthorId { get => _rootAuthorId; set => _rootAuthorId = value; }
    public byte[] ParentAuthorId { get => _parentAuthorId; set => _parentAuthorId = value; }
    public byte[] CurrentAuthorId { get => _currentAuthorId; set => _currentAuthorId = value; }
    public string RootAuthorName { get => _rootAuthorName.ToStringTrimed(); set => Utils.SetStringValueStrict(ref _rootAuthorName, value, 11); }
    public string ParentAuthorName { get => _parentAuthorName.ToStringTrimed(); set => Utils.SetStringValueStrict(ref _parentAuthorName, value, 11); }
    public string CurrentAuthorName { get => _currentAuthorName.ToStringTrimed(); set => Utils.SetStringValueStrict(ref _currentAuthorName, value, 11); }
    public string RootFilename { get => Encoding.ASCII.GetString(_rootFilename); set => Utils.SetBytesValueStrict(ref _rootFilename, Encoding.ASCII.GetBytes(value), 28); }
    public string ParentFilename { get => Encoding.ASCII.GetString(_parentFilename); set => Utils.SetBytesValueStrict(ref _parentFilename, Encoding.ASCII.GetBytes(value), 28); }
    public string CurrentFilename { get => Encoding.ASCII.GetString(_currentFilename); set => Utils.SetBytesValueStrict(ref _currentFilename, Encoding.ASCII.GetBytes(value), 28); }
    public ushort FrameCount { get => _frameCount; set => _frameCount = value; }
    public ushort ThumbnailFrameIndex { get => _thumbnailFrameIndex; set => _thumbnailFrameIndex = value; }
    public ushort Flags { get => _flags; set => _flags = value; }
    public byte FrameSpeed { get => _frameSpeed; set => _frameSpeed = value; }
    public byte LayerVisibilityFlags { get => _layerVisibilityFlags; set => _layerVisibilityFlags = value; }


    public bool Locked { get => (_flags & 0x1) != 0; set => _flags = value ? (ushort)(_flags | 0x1) : (ushort)(_flags & ~0x1); }
    public bool LoopPlayback { get => (_flags & 0x2) != 0; set => _flags = value ? (ushort)(_flags | 0x2) : (ushort)(_flags & ~0x2); }
    public bool Toolset { get => (_flags & 0x4) != 0; set => _flags = value ? (ushort)(_flags | 0x4) : (ushort)(_flags & ~0x4); }


    public bool LayerAVisible { get => (_layerVisibilityFlags & 0x1) == 0; set => _layerVisibilityFlags = value ? (byte)(_layerVisibilityFlags & ~0x1) : (byte)(_layerVisibilityFlags | 0x1); }
    public bool LayerBVisible { get => (_layerVisibilityFlags & 0x2) == 0; set => _layerVisibilityFlags = value ? (byte)(_layerVisibilityFlags & ~0x2) : (byte)(_layerVisibilityFlags | 0x2); }
    public bool LayerCVisible { get => (_layerVisibilityFlags & 0x4) == 0; set => _layerVisibilityFlags = value ? (byte)(_layerVisibilityFlags & ~0x4) : (byte)(_layerVisibilityFlags | 0x4); }


    public static KwzHeader ReadFrom(BinaryReader reader)
    {
        var header = new KwzHeader();

        reader
            .ReadChars(4, ref header._magic, Encoding.ASCII)
            .ReadUInt32(ref header._size)
            .ReadUInt32(ref header._crc32)
            .ReadUInt32(ref header._creationTimestamp)
            .ReadUInt32(ref header._lastEditTimestamp)
            .ReadUInt32(ref header._appVersion)
            .ReadBytes(10, ref header._rootAuthorId)
            .ReadBytes(10, ref header._parentAuthorId)
            .ReadBytes(10, ref header._currentAuthorId)
            .ReadChars(11, ref header._rootAuthorName)
            .ReadChars(11, ref header._parentAuthorName)
            .ReadChars(11, ref header._currentAuthorName)
            .ReadBytes(28, ref header._rootFilename)
            .ReadBytes(28, ref header._parentFilename)
            .ReadBytes(28, ref header._currentFilename)
            .ReadUInt16(ref header._frameCount)
            .ReadUInt16(ref header._thumbnailFrameIndex)
            .ReadUInt16(ref header._flags)
            .ReadByte(ref header._frameSpeed)
            .ReadByte(ref header._layerVisibilityFlags);

        return header;
    }

    public void WriteTo(BinaryWriter writer)
    {
        Span<byte> rootFileNameSpan = stackalloc byte[17];
        Span<byte> parentFileNameSpan = stackalloc byte[17];
        Span<byte> currentFileNameSpan = stackalloc byte[17];

        Utils.SetBytesValueStrict(rootFileNameSpan[..9], _rootAuthorId.AsSpan()[..9], 9);
        BinaryPrimitives.WriteUInt32LittleEndian(rootFileNameSpan[9..], _creationTimestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(rootFileNameSpan[13..], _lastEditTimestamp);

        Utils.SetBytesValueStrict(parentFileNameSpan[..9], _parentAuthorId.AsSpan()[..9], 9);
        BinaryPrimitives.WriteUInt32LittleEndian(parentFileNameSpan[9..], _creationTimestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(parentFileNameSpan[13..], _lastEditTimestamp);

        Utils.SetBytesValueStrict(currentFileNameSpan[..9], _currentAuthorId.AsSpan()[..9], 9);
        BinaryPrimitives.WriteUInt32LittleEndian(currentFileNameSpan[9..], _creationTimestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(currentFileNameSpan[13..], _lastEditTimestamp);

        using var mem = new MemoryStream();
        using var memWriter = new BinaryWriter(mem);
        memWriter
            .WriteValue(_creationTimestamp)
            .WriteValue(_lastEditTimestamp)
            .WriteValue(_appVersion)
            .WriteValue(_rootAuthorId)
            .WriteValue(_parentAuthorId)
            .WriteValue(_currentAuthorId)
            .WriteValue(_rootAuthorName)
            .WriteValue(_parentAuthorName)
            .WriteValue(_currentAuthorName)
            .WriteValue(rootFileNameSpan.ToArray().KwzBase32Encode())
            .WriteValue(parentFileNameSpan.ToArray().KwzBase32Encode())
            .WriteValue(currentFileNameSpan.ToArray().KwzBase32Encode())
            .WriteValue(_frameCount)
            .WriteValue(_thumbnailFrameIndex)
            .WriteValue(_flags)
            .WriteValue(_frameSpeed)
            .WriteValue(_layerVisibilityFlags);
        var data = mem.ToArray();
        _crc32 = System.IO.Hashing.Crc32.HashToUInt32(data);

        writer
            .WriteValue([.."KFH\x14"], Encoding.ASCII)
            .WriteValue(4 + (uint)data.Length)
            .WriteValue(_crc32)
            .WriteValue(data);
    }
}
