using System.Text;

namespace LibKwz;

public static class BinaryExtensions
{
    public static BinaryReader ReadUInt32(this BinaryReader reader, ref uint value)
    {
        value = reader.ReadUInt32();
        return reader;
    }

    public static BinaryReader ReadUInt16(this BinaryReader reader, ref ushort value)
    {
        value = reader.ReadUInt16();
        return reader;
    }

    public static BinaryReader ReadByte(this BinaryReader reader, ref byte value)
    {
        value = reader.ReadByte();
        return reader;
    }

    public static BinaryReader ReadBytes(this BinaryReader reader, int count, ref byte[] value)
    {
        var bytes = reader.ReadBytes(count);
        value.AsSpan().Clear();
        bytes.AsSpan().CopyTo(value.AsSpan());
        return reader;
    }

    public static BinaryReader ReadChars(this BinaryReader reader, int count, ref char[] value, Encoding? encoding = default)
    {
        if (encoding == default)
        {
            encoding = Encoding.Unicode;
        }

        var streamPosition = reader.BaseStream.Position;
        var maxByteCount = encoding.GetMaxByteCount(count);
        var bytes = reader.ReadBytes(maxByteCount);
        var chars = encoding.GetChars(bytes).AsSpan(0, count);
        var actualByteCount = encoding.GetByteCount(chars);
        reader.BaseStream.Position = streamPosition + actualByteCount;

        value.AsSpan().Clear();
        chars.CopyTo(value.AsSpan());
        return reader;
    }

    public static BinaryWriter WriteValue(this BinaryWriter writer, uint value)
    {
        writer.Write(value);
        return writer;
    }

    public static BinaryWriter WriteValue(this BinaryWriter writer, ushort value)
    {
        writer.Write(value);
        return writer;
    }

    public static BinaryWriter WriteValue(this BinaryWriter writer, byte value)
    {
        writer.Write(value);
        return writer;
    }

    public static BinaryWriter WriteValue(this BinaryWriter writer, byte[] value)
    {
        writer.Write(value);
        return writer;
    }

    public static BinaryWriter WriteValue(this BinaryWriter writer, char[] value, Encoding? encoding = default)
    {
        if (encoding == default)
        {
            encoding = Encoding.Unicode;
        }

        var bytes = encoding.GetBytes(value);
        writer.Write(bytes);
        return writer;
    }
}
