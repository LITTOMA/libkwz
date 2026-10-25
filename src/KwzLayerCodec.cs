namespace LibKwz;

/// <summary>Lossless 320x240, three-value KWZ layer codec. Pixels are 0, 1 or 2.</summary>
public static class KwzLayerCodec
{
    public const int Width = 320, Height = 240, PixelCount = Width * Height;
    internal static readonly int[] Common =
    [
        0x0000, 0x0CD0, 0x19A0, 0x02D9, 0x088B, 0x0051, 0x00F3, 0x0009,
        0x001B, 0x0001, 0x0003, 0x05B2, 0x1116, 0x00A2, 0x01E6, 0x0012,
        0x0036, 0x0002, 0x0006, 0x0B64, 0x08DC, 0x0144, 0x00FC, 0x0024,
        0x001C, 0x0004, 0x0334, 0x099C, 0x0668, 0x1338, 0x1004, 0x166C
    ];
    internal static readonly byte[][] Lines = BuildLineTable();
    internal static readonly int[] Shift = BuildShiftTable();
    internal static readonly int[] CommonIndex = BuildCommonIndex();
    internal static readonly int[] TileOffsets = BuildTileOffsets();
    internal static readonly int[][] Patterns =
    [ [0,1,0,1,0,1,0,1], [0,0,1,0,0,1,0,0], [0,1,0,0,1,0,0,1], [0,1,1,0,1,1,0,1] ];

    public static byte[][] BuildLineTable()
    {
        var result = new byte[6561][];
        for (int index = 0; index < result.Length; index++)
        {
            var row = new byte[8];
            int n = index;
            for (int digit = 7; digit >= 0; digit--) { row[digit ^ 1] = (byte)(n % 3); n /= 3; }
            result[index] = row;
        }
        return result;
    }

    internal static int LineIndex(ReadOnlySpan<byte> row)
    {
        int n = 0;
        for (int i = 0; i < 8; i++) n = n * 3 + row[i ^ 1];
        return n;
    }

    private static int[] BuildShiftTable()
    {
        var result = new int[6561];
        Span<byte> shifted = stackalloc byte[8];
        for (int i = 0; i < result.Length; i++)
        {
            for (int j = 0; j < 8; j++) shifted[j] = Lines[i][(j + 1) % 8];
            result[i] = LineIndex(shifted);
        }
        return result;
    }

    private static int[] BuildCommonIndex()
    {
        var result = Enumerable.Repeat(-1, 6561).ToArray();
        for (int i = 0; i < Common.Length; i++) result[Common[i]] = i;
        return result;
    }

    private static int[] BuildTileOffsets()
    {
        var result = new List<int>(1200);
        for (int by = 0; by < Height; by += 128)
        for (int bx = 0; bx < Width; bx += 128)
        for (int y = by; y < Math.Min(by + 128, Height); y += 8)
        for (int x = bx; x < Math.Min(bx + 128, Width); x += 8)
            result.Add(y * Width + x);
        return result.ToArray();
    }

    public static void Decode(ReadOnlyMemory<byte> data, Span<byte> pixels)
        => KwzLayerEncoding.Read(data).Decode(pixels);

    /// <summary>Encode pixels, optionally using the previous layer for skip runs (up to 32 tiles).</summary>
    public static byte[] Encode(ReadOnlySpan<byte> pixels, ReadOnlySpan<byte> previous = default)
    {
        ValidatePixels(pixels);
        if (!previous.IsEmpty) ValidatePixels(previous);
        var writer = new KwzBitWriter();
        Span<int> rows = stackalloc int[8];
        int tile = 0;
        while (tile < TileOffsets.Length)
        {
            if (!previous.IsEmpty && TileEquals(pixels, previous, tile))
            {
                int count = 1;
                while (count < 32 && tile + count < TileOffsets.Length && TileEquals(pixels, previous, tile + count)) count++;
                writer.Write(5, 3); writer.Write(count - 1, 5); tile += count;
                continue;
            }
            ReadRows(pixels, tile++, rows);
            int first = rows[0], common = CommonIndex[first];
            if (AllRows(rows, first, first, Patterns[0]))
            {
                writer.Write(common >= 0 ? 0 : 1, 3);
                writer.Write(common >= 0 ? common : first, common >= 0 ? 5 : 13);
                continue;
            }
            if (AllRows(rows, first, Shift[first], Patterns[0]))
            {
                writer.Write(common >= 0 ? 2 : 3, 3);
                writer.Write(common >= 0 ? common : first, common >= 0 ? 5 : 13);
                continue;
            }
            bool patterned = false;
            for (int pattern = 0; pattern < 4; pattern++)
            {
                int second = rows[pattern == 1 ? 2 : 1];
                if (!AllRows(rows, first, second, Patterns[pattern])) continue;
                bool useCommon = common >= 0 && CommonIndex[second] >= 0;
                writer.Write(7, 3); writer.Write(useCommon ? (pattern + 3) % 4 : pattern, 2);
                writer.Write(useCommon ? 1 : 0, 1);
                writer.Write(useCommon ? common : first, useCommon ? 5 : 13);
                writer.Write(useCommon ? CommonIndex[second] : second, useCommon ? 5 : 13);
                patterned = true; break;
            }
            if (patterned) continue;
            int mask = 0;
            for (int i = 0; i < 8; i++) if (CommonIndex[rows[i]] >= 0) mask |= 1 << i;
            writer.Write(4, 3); writer.Write(mask, 8);
            for (int i = 0; i < 8; i++)
                writer.Write((mask & (1 << i)) != 0 ? CommonIndex[rows[i]] : rows[i], (mask & (1 << i)) != 0 ? 5 : 13);
        }
        // The native decoder fetches little-endian 16-bit words.
        return writer.FinishAligned();
    }

    internal static void ValidatePixels(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != PixelCount) throw new ArgumentException("A KWZ layer must contain 320x240 pixels.");
        foreach (byte p in pixels) if (p > 2) throw new ArgumentException("KWZ layer pixels must be 0, 1 or 2.");
    }

    internal static bool TileEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int tile)
    {
        int offset = TileOffsets[tile];
        for (int y = 0; y < 8; y++) if (!a.Slice(offset + y * Width, 8).SequenceEqual(b.Slice(offset + y * Width, 8))) return false;
        return true;
    }

    internal static void ReadRows(ReadOnlySpan<byte> pixels, int tile, Span<int> rows)
    {
        for (int y = 0; y < 8; y++) rows[y] = LineIndex(pixels.Slice(TileOffsets[tile] + y * Width, 8));
    }

    internal static bool AllRows(ReadOnlySpan<int> rows, int a, int b, int[] pattern)
    {
        for (int i = 0; i < 8; i++) if (rows[i] != (pattern[i] == 0 ? a : b)) return false;
        return true;
    }
}

/// <summary>Parsed compression instructions and unused bits. Retaining these makes exact reconstruction possible.</summary>
public sealed class KwzLayerEncoding
{
    private sealed record Tile(int Type, int Parameter, int[] Values);
    private readonly List<Tile> tiles = [];
    private byte[] padding = [];
    private int paddingBits;
    public int InstructionCount => tiles.Count;
    public bool UsesPreviousLayer => tiles.Any(t => t.Type == 5);
    public IEnumerable<int> TileTypes => tiles.Select(t => t.Type);

    public static KwzLayerEncoding Read(ReadOnlyMemory<byte> bytes)
    {
        if ((bytes.Length & 1) != 0) throw new InvalidDataException("KWZ layers must be word-aligned.");
        var reader = new KwzBitReader(bytes);
        var result = new KwzLayerEncoding();
        int position = 0;
        while (position < 1200)
        {
            int type = reader.Read(3), parameter = 0;
            int[] values;
            switch (type)
            {
                case 0: case 2: values = [reader.Read(5)]; break;
                case 1: case 3: values = [ReadLine(reader)]; break;
                case 4:
                    parameter = reader.Read(8); values = new int[8];
                    for (int i = 0; i < 8; i++) values[i] = (parameter & (1 << i)) != 0 ? reader.Read(5) : ReadLine(reader);
                    break;
                case 5:
                    parameter = reader.Read(5); values = []; position += parameter;
                    break;
                case 7:
                    parameter = reader.Read(3); // low two bits: pattern; high bit: common table
                    values = (parameter & 4) != 0 ? [reader.Read(5), reader.Read(5)] : [ReadLine(reader), ReadLine(reader)];
                    break;
                default: throw new InvalidDataException("Reserved KWZ tile type 6.");
            }
            result.tiles.Add(new Tile(type, parameter, values));
            position++;
        }
        // Native encoders may finish with a skip run extending past the last tile.
        result.paddingBits = bytes.Length * 8 - reader.Position;
        var tail = new KwzBitWriter();
        for (int i = 0; i < result.paddingBits; i++) tail.Write(reader.Read(1), 1);
        result.padding = tail.Finish();
        return result;
    }

    private static int ReadLine(KwzBitReader reader)
    {
        int index = reader.Read(13);
        if (index >= 6561) throw new InvalidDataException($"Invalid KWZ line index {index}.");
        return index;
    }

    private static int Line(Tile tile, int i) => tile.Type is 0 or 2 || tile.Type == 7 && (tile.Parameter & 4) != 0 || tile.Type == 4 && (tile.Parameter & (1 << i)) != 0
        ? KwzLayerCodec.Common[tile.Values[i]] : tile.Values[i];

    private static void Rows(Tile tile, Span<int> rows)
    {
        int a = Line(tile, 0);
        if (tile.Type == 4) { for (int i = 0; i < 8; i++) rows[i] = Line(tile, i); return; }
        int b = tile.Type is 2 or 3 ? KwzLayerCodec.Shift[a] : tile.Type == 7 ? Line(tile, 1) : a;
        int pattern = tile.Type == 7 ? ((tile.Parameter & 3) + ((tile.Parameter >> 2) & 1)) % 4 : 0;
        for (int i = 0; i < 8; i++) rows[i] = KwzLayerCodec.Patterns[pattern][i] == 0 ? a : b;
    }

    /// <summary>Apply this layer to an existing previous-layer buffer, or to a zeroed buffer for frame zero.</summary>
    public void Decode(Span<byte> pixels)
    {
        if (pixels.Length != KwzLayerCodec.PixelCount) throw new ArgumentException("Invalid layer buffer size.");
        Span<int> rows = stackalloc int[8];
        int position = 0;
        foreach (var tile in tiles)
        {
            if (tile.Type == 5) { position += tile.Parameter + 1; continue; }
            Rows(tile, rows);
            for (int y = 0; y < 8; y++) KwzLayerCodec.Lines[rows[y]].CopyTo(pixels.Slice(KwzLayerCodec.TileOffsets[position] + y * 320, 8));
            position++;
        }
    }

    /// <summary>Serialize parsed fields and padding, without copying the original compressed buffer.</summary>
    public byte[] ToBytes() => Write(default, default);

    /// <summary>Re-encode decoded pixels using the original instruction choices. Throws if they cannot represent the pixels.</summary>
    public byte[] EncodePixels(ReadOnlySpan<byte> pixels, ReadOnlySpan<byte> previous)
    {
        KwzLayerCodec.ValidatePixels(pixels); KwzLayerCodec.ValidatePixels(previous);
        return Write(pixels, previous);
    }

    private byte[] Write(ReadOnlySpan<byte> pixels, ReadOnlySpan<byte> previous)
    {
        var writer = new KwzBitWriter();
        Span<int> rows = stackalloc int[8];
        int position = 0;
        foreach (var tile in tiles)
        {
            writer.Write(tile.Type, 3);
            if (tile.Type == 5)
            {
                if (!pixels.IsEmpty)
                    for (int n = position; n < Math.Min(1200, position + tile.Parameter + 1); n++)
                        if (!KwzLayerCodec.TileEquals(pixels, previous, n)) throw new InvalidOperationException("Original skip run no longer matches the preceding frame.");
                writer.Write(tile.Parameter, 5); position += tile.Parameter + 1; continue;
            }
            if (!pixels.IsEmpty)
            {
                KwzLayerCodec.ReadRows(pixels, position, rows);
                int second = tile.Type is 2 or 3 ? KwzLayerCodec.Shift[rows[0]] : rows[0];
                int shape = 0;
                if (tile.Type == 7)
                {
                    shape = ((tile.Parameter & 3) + ((tile.Parameter >> 2) & 1)) % 4;
                    second = rows[shape == 1 ? 2 : 1];
                }
                if (tile.Type != 4 && !KwzLayerCodec.AllRows(rows, rows[0], second, KwzLayerCodec.Patterns[shape]))
                    throw new InvalidOperationException("Pixels no longer fit the original tile pattern.");
            }
            else Rows(tile, rows);
            // Derive line references from pixels; retain only the choice of opcode, table and pattern.
            switch (tile.Type)
            {
                case 0: case 2: writer.Write(KwzLayerCodec.CommonIndex[rows[0]], 5); break;
                case 1: case 3: writer.Write(rows[0], 13); break;
                case 4:
                    writer.Write(tile.Parameter, 8);
                    for (int i = 0; i < 8; i++) writer.Write((tile.Parameter & (1 << i)) != 0 ? KwzLayerCodec.CommonIndex[rows[i]] : rows[i], (tile.Parameter & (1 << i)) != 0 ? 5 : 13);
                    break;
                case 7:
                    writer.Write(tile.Parameter, 3);
                    int pattern = ((tile.Parameter & 3) + ((tile.Parameter >> 2) & 1)) % 4;
                    int b = rows[pattern == 1 ? 2 : 1];
                    bool common = (tile.Parameter & 4) != 0;
                    writer.Write(common ? KwzLayerCodec.CommonIndex[rows[0]] : rows[0], common ? 5 : 13);
                    writer.Write(common ? KwzLayerCodec.CommonIndex[b] : b, common ? 5 : 13);
                    break;
            }
            position++;
        }
        var tail = new KwzBitReader(padding);
        for (int i = 0; i < paddingBits; i++) writer.Write(tail.Read(1), 1);
        return writer.Finish();
    }
}

internal sealed class KwzBitReader(ReadOnlyMemory<byte> data)
{
    public int Position { get; private set; }
    public int Read(int count)
    {
        if ((long)Position + count > (long)data.Length * 8) throw new InvalidDataException("Truncated KWZ bitstream.");
        int value = 0;
        for (int i = 0; i < count; i++, Position++) value |= ((data.Span[Position >> 3] >> (Position & 7)) & 1) << i;
        return value;
    }
}

internal sealed class KwzBitWriter
{
    private readonly List<byte> bytes = [];
    private int position;
    public void Write(int value, int count)
    {
        if (value < 0 || value >= (1 << count)) throw new InvalidDataException("Bit field does not fit.");
        for (int i = 0; i < count; i++, position++)
        {
            if ((position & 7) == 0) bytes.Add(0);
            bytes[position >> 3] |= (byte)(((value >> i) & 1) << (position & 7));
        }
    }
    public byte[] Finish() => bytes.ToArray();
    public byte[] FinishAligned() { if ((bytes.Count & 1) != 0) bytes.Add(0); return Finish(); }
}
