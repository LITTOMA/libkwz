using System.IO.Hashing;

namespace LibKwz;

public class KwzThumbnail
{
    public const string MAGIC = "KTN\x02";

    private char[] _magic = [.. MAGIC];
    private uint _size;
    private uint _crc32;
    private byte[] _data = [];

    public char[] Magic => _magic;
    public uint Size => _size;
    public uint Crc32 { get => _crc32; set => _crc32 = value; }
    public byte[] Data { get => _data; set => _data = value; }

    public static KwzThumbnail ReadFrom(BinaryReader reader)
    {
        var thumbnail = new KwzThumbnail();
        reader
            .ReadChars(4, ref thumbnail._magic, Encoding.ASCII)
            .ReadUInt32(ref thumbnail._size)
            .ReadUInt32(ref thumbnail._crc32);

        if (new string(thumbnail._magic) != MAGIC || thumbnail._size < 4 || thumbnail._size > int.MaxValue) throw new InvalidDataException("Invalid KTN section.");
        thumbnail._data = reader.ReadExactlyBytes((int)thumbnail._size - 4);
        return thumbnail;
    }

    public void WriteTo(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(_data);

        _size = (uint)_data.Length + 4;
        _crc32 = System.IO.Hashing.Crc32.HashToUInt32(_data);

        writer
            .WriteValue(_magic, Encoding.ASCII)
            .WriteValue(_size)
            .WriteValue(_crc32)
            .WriteValue(_data);
    }
}
