using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace LibKwz;

/// <summary>The ordered !!.lst catalog used by Flipnote Studio 3D SD folders.</summary>
public sealed class KwzSdList
{
    public const int MaxEntries = 100;
    public const int MaxFileSize = 6 + 68 * MaxEntries;
    private const string Alphabet = "cwmfjordvegbalksnthpyxquiz012345";
    // This is the game's public obfuscation table, not an encryption/signing secret.
    private static readonly byte[] XorTable = Convert.FromHexString(
        "F74C6A3AFB82A6376E1138CFA0DD85C0C79BC4D8DD288A875320EEE00BEB43A0" +
        "DB550F753637EB356A347FB50F99F7EF4325CEA02946D9D44DBB046668");
    public IReadOnlyList<string> Filenames { get; }

    public KwzSdList(IEnumerable<string> filenames)
    {
        string[] names = filenames.Take(MaxEntries + 1).ToArray();
        if (names.Length > MaxEntries) throw new InvalidDataException("An SD animation folder supports at most 100 entries.");
        if (names.Any(n => !IsValidFilename(n))) throw new InvalidDataException("Invalid SD animation filename.");
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidDataException("Duplicate SD animation filenames.");
        Filenames = Array.AsReadOnly(names);
    }

    public static bool IsValidFilename(string? name) => name is { Length: 32 } &&
        name.EndsWith(".kwz", StringComparison.OrdinalIgnoreCase) && name.Take(28).All(Alphabet.Contains);

    public static KwzSdList Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6 || data.Length > MaxFileSize || (data.Length - 6) % 68 != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 10)
            throw new InvalidDataException("Unsupported or truncated !!.lst header.");
        byte[] plain = data[6..].ToArray();
        Transform(plain);
        uint crc = ((uint)BinaryPrimitives.ReadUInt16LittleEndian(data) << 16) |
            BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        if (Crc32.HashToUInt32(plain) != crc) throw new InvalidDataException("The !!.lst checksum does not match.");
        int count = plain.Length / 68;
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = Encoding.Unicode.GetString(plain, i * 66, 64);
            if (BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(i * 66 + 64)) != 10)
                throw new InvalidDataException("Invalid !!.lst line ending.");
        }
        // The game reserves 34 UTF-16 units per name but writes 32 characters + LF.
        // The resulting N NULs are at the end of the entire stream, not in each record.
        if (plain.AsSpan(count * 66).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Unsupported !!.lst trailing data.");
        return new KwzSdList(names);
    }

    public byte[] ToArray()
    {
        byte[] data = new byte[6 + 68 * Filenames.Count];
        Span<byte> plain = data.AsSpan(6);
        for (int i = 0; i < Filenames.Count; i++)
        {
            Encoding.Unicode.GetBytes(Filenames[i], plain.Slice(i * 66, 64));
            BinaryPrimitives.WriteUInt16LittleEndian(plain[(i * 66 + 64)..], 10);
        }
        uint crc = Crc32.HashToUInt32(plain);
        BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)(crc >> 16));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)crc);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 10);
        Transform(plain);
        return data;
    }

    private static void Transform(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++) data[i] ^= XorTable[i % XorTable.Length];
    }
}
