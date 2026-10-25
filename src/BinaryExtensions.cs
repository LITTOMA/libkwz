using System.Text;

namespace LibKwz;

public static class BinaryExtensions
{
    public static byte[] ReadExactlyBytes(this BinaryReader reader, int count)
    {
        if (count < 0) throw new InvalidDataException("Negative field length.");
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new InvalidDataException("Truncated KWZ field.");
        return bytes;
    }
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
        var bytes = reader.ReadExactlyBytes(count);
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

        // KWZ strings are fixed-width code units, including null padding and unpaired surrogates.
        if (encoding.CodePage == Encoding.Unicode.CodePage)
            for (int i = 0; i < count; i++) value[i] = (char)reader.ReadUInt16();
        else
            encoding.GetChars(reader.ReadExactlyBytes(count)).CopyTo(value, 0);
        return reader;
    }

    public static BinaryReader PeekChars(this BinaryReader reader, int count, out char[] value, Encoding? encoding = default)
    {
        value = new char[count];
        var position = reader.BaseStream.Position;
        reader.ReadChars(count, ref value, encoding);
        reader.BaseStream.Position = position;
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

        if (encoding.CodePage == Encoding.Unicode.CodePage)
            foreach (char c in value) writer.Write((ushort)c);
        else writer.Write(encoding.GetBytes(value));
        return writer;
    }
}
