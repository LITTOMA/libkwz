using System;
using System.Buffers.Binary;
using System.IO;

namespace LibKwz;

public class KwzFrameData
{
    private static readonly ushort[] commonLineIndexTable =
    [
        0x0000, 0x0CD0, 0x19A0, 0x02D9, 0x088B, 0x0051, 0x00F3, 0x0009,
        0x001B, 0x0001, 0x0003, 0x05B2, 0x1116, 0x00A2, 0x01E6, 0x0012,
        0x0036, 0x0002, 0x0006, 0x0B64, 0x08DC, 0x0144, 0x00FC, 0x0024,
        0x001C, 0x0004, 0x0334, 0x099C, 0x0668, 0x1338, 0x1004, 0x166C
    ];

    private static readonly ushort[] commonShiftedLineIndexTable =
    [
        0x0000, 0x0CD0, 0x19A0, 0x0003, 0x02D9, 0x088B, 0x0051, 0x00F3,
        0x0009, 0x001B, 0x0001, 0x0006, 0x05B2, 0x1116, 0x00A2, 0x01E6,
        0x0012, 0x0036, 0x0002, 0x02DC, 0x0B64, 0x08DC, 0x0144, 0x00FC,
        0x0024, 0x001C, 0x099C, 0x0334, 0x1338, 0x0668, 0x166C, 0x1004
    ];

    private static readonly byte[][] lineTable = BuildLineTable();
    private static readonly ushort[] shiftedLineIndexTable = BuildShiftedLineIndexTable();

    public static byte[][] BuildLineTable()
    {
        var combinations = from a in Enumerable.Range(0, 3)
                           from b in Enumerable.Range(0, 3)
                           from c in Enumerable.Range(0, 3)
                           from d in Enumerable.Range(0, 3)
                           from e in Enumerable.Range(0, 3)
                           from f in Enumerable.Range(0, 3)
                           from g in Enumerable.Range(0, 3)
                           from h in Enumerable.Range(0, 3)
                           select new byte[] { (byte)b, (byte)a, (byte)d, (byte)c, (byte)f, (byte)e, (byte)h, (byte)g };

        return combinations.ToArray();
    }

    public static ushort[] BuildShiftedLineIndexTable()
    {
        var combinations = from a in RangeWithStep(0, 2187, 729)
                           from b in RangeWithStep(0, 729, 243)
                           from c in RangeWithStep(0, 243, 81)
                           from d in RangeWithStep(0, 81, 27)
                           from e in RangeWithStep(0, 27, 9)
                           from f in RangeWithStep(0, 9, 3)
                           from g in RangeWithStep(0, 3, 1)
                           from h in RangeWithStep(0, 6561, 2187)
                           select ushort.Parse($"{a + b + c + d + e + f + g + h}", System.Globalization.NumberStyles.Integer);

        return combinations.ToArray();
    }

    public static IEnumerable<int> RangeWithStep(int start, int count, int step)
    {
        return Enumerable.Range(0, count)
                         .Select(x => start + x * step);
    }

    private char[] _magic = new char[4];
    private uint _size;
    private uint _crc32;
    private byte[]? _data;
    private int[] _frameOffsets;
    private bool[] _layerVisibility;
    private KwzMeta _frameMeta;
    private int _prevDecodedFrame;

    public char[] Magic => _magic;
    public uint Size => _size;
    public uint Crc32 { get => _crc32; set => _crc32 = value; }
    public byte[] Data { get => _data; set => _data = value; }
    public byte[][] Frames { get; set; }

    public static KwzFrameData ReadFrom(BinaryReader reader, KwzHeader header, KwzMeta meta)
    {
        var frameData = new KwzFrameData();

        reader
            .ReadChars(4, ref frameData._magic, Encoding.ASCII)
            .ReadUInt32(ref frameData._size)
            .ReadUInt32(ref frameData._crc32);

        frameData._data = reader.ReadBytes((int)frameData._size - 4);

        frameData._frameOffsets = new int[header.FrameCount];
        int offset = 0;
        for (int i = 0; i < header.FrameCount; i++)
        {
            frameData._frameOffsets[i] = offset;
            offset += meta[i].LayerASize + meta[i].LayerBSize + meta[i].LayerCSize;
        }

        frameData._layerVisibility = [header.LayerAVisible, header.LayerBVisible, header.LayerCVisible];
        frameData._frameMeta = meta;

        return frameData;
    }

    public byte[] DecodeFrame(
        int frameIndex,
        int diffingFlag = 0x07,
        bool isPrevFrame = false)
    {
        if (isPrevFrame)
        {
            diffingFlag &= GetDiffingFlag(frameIndex + 1);
        }

        if (!(_prevDecodedFrame == (frameIndex - 1)) && diffingFlag > 0)
        {
            DecodeFrame(frameIndex - 1, diffingFlag, true);
        }


        int depth = 3;
        int height = 240;
        int width = 40;

        byte[] rawCurrentFrameData = new byte[depth * height * width];
        var currentFrameSpan = rawCurrentFrameData.AsSpan();

        // Assume FrameMeta and FrameOffsets are initialized and populated properly
        var meta = _frameMeta[frameIndex];
        var offset = _frameOffsets[frameIndex];
        Memory<byte> frameSectionData = _data.AsMemory();

        for (int layerIndex = 0; layerIndex < 3; layerIndex++)
        {
            int layerLength = meta.LayerSizes[layerIndex];
            offset += layerLength;

            if (layerLength == 38) continue;
            if ((diffingFlag >> layerIndex & 0x1) == 0) continue;
            if (!_layerVisibility[layerIndex]) continue;

            Memory<byte> compressedLayer = frameSectionData.Slice(offset, layerLength);
            Span<byte> decompressedLayer = currentFrameSpan.Slice(layerIndex * height * width);
            DecompressLayer(compressedLayer, decompressedLayer, offset);
        }

        _prevDecodedFrame = frameIndex;
        return rawCurrentFrameData;
    }

    private int GetDiffingFlag(int frameIndex)
    {
        return (int)~((_frameMeta[frameIndex].Flags >> 4) & 0x07);
    }

    // Methods for decompressing each layer
    public void DecompressLayer(ReadOnlyMemory<byte> compressedLayer, Span<byte> outputLayer, int layerDataOffset)
    {
        int bitIndex = 0;
        ushort bitValue = 0;

        int readBits(int numBits)
        {
            if (bitIndex + numBits > 16)
            {
                ushort nextBits = BinaryPrimitives.ReadUInt16LittleEndian(compressedLayer.Span.Slice(layerDataOffset));
                layerDataOffset += 2;
                bitValue |= (ushort)(nextBits << (16 - bitIndex));
                bitIndex -= 16;
            }
            int result = bitValue & ((1 << numBits) - 1);
            bitValue >>= numBits;
            bitIndex += numBits;
            return result;
        }

        // Helper methods to fill the tile based on the type
        void fillTileWithLine(Span<byte> tile, int lineIndex, int offset)
        {
            var line = lineTable[lineIndex];
            for (int i = 0; i < 8; i++)
            {
                line.CopyTo(tile.Slice(i * 8, 8));
            }
        }

        void fillTileWithAlternatingLines(Span<byte> tile, int index)
        {
            int lineIndexA = commonLineIndexTable[index];
            int lineIndexB = commonShiftedLineIndexTable[index];
            byte[] lineA = lineTable[lineIndexA];
            byte[] lineB = lineTable[lineIndexB];
            byte[] tileArray = [.. lineA, .. lineB, .. lineA, .. lineB, .. lineA, .. lineB, .. lineA, .. lineB];
            tileArray.CopyTo(tile);
        }

        void fillTileWithAlternatingLines2(Span<byte> tile, int lineIndexA, int lineIndexB)
        {
            byte[] lineA = lineTable[lineIndexA];
            byte[] lineB = lineTable[shiftedLineIndexTable[lineIndexB]];
            byte[] tileArray = [.. lineA, .. lineB, .. lineA, .. lineB, .. lineA, .. lineB, .. lineA, .. lineB];
            tileArray.CopyTo(tile);
        }

        void fillTileWithFlags(Span<byte> tile, byte flags)
        {
            for (int i = 0; i < 8; i++)
            {
                int lineIndex;
                if ((flags & (1 << i)) != 0)
                {
                    lineIndex = commonLineIndexTable[readBits(5)];
                }
                else
                {
                    lineIndex = readBits(13);
                }
                byte[] line = lineTable[lineIndex];
                line.CopyTo(tile.Slice(i * 8, 8));
            }
        }

        void fillTileWithPattern(Span<byte> tile, int pattern, bool isCommon)
        {
            int lineIndexA, lineIndexB;
            if (isCommon)
            {
                lineIndexA = commonLineIndexTable[readBits(5)];
                lineIndexB = commonLineIndexTable[readBits(5)];
                pattern = (pattern + 1) % 4;
            }
            else
            {
                lineIndexA = readBits(13);
                lineIndexB = readBits(13);
            }

            byte[] lineA = lineTable[lineIndexA];
            byte[] lineB = lineTable[lineIndexB];
            for (int i = 0; i < 8; i++)
            {
                byte[] line = GetPatternLine(pattern, i, lineA, lineB);
                line.CopyTo(tile.Slice(i * 8, 8));
            }
        }

        for (int largeTileY = 0; largeTileY < 240; largeTileY += 128)
        {
            for (int largeTileX = 0; largeTileX < 320; largeTileX += 128)
            {
                for (int tileY = 0; tileY < 128; tileY += 8)
                {
                    int y = largeTileY + tileY;
                    if (y >= 240)
                        break;

                    for (int tileX = 0; tileX < 128; tileX += 8)
                    {
                        int x = largeTileX + tileX;
                        if (x >= 320)
                            break;

                        int tileType = readBits(3);
                        Span<byte> tile = new byte[8 * 8];

                        switch (tileType)
                        {
                            case 0:
                                int lineIndex0 = commonLineIndexTable[readBits(5)];
                                fillTileWithLine(tile, lineIndex0, 0);
                                break;
                            case 1:
                                int lineIndex1 = readBits(13);
                                fillTileWithLine(tile, lineIndex1, 0);
                                break;
                            case 2:
                                int index2 = readBits(5);
                                fillTileWithAlternatingLines(tile, index2);
                                break;
                            case 3:
                                int lineIndex3A = readBits(13);
                                int lineIndex3B = commonShiftedLineIndexTable[lineIndex3A];
                                fillTileWithAlternatingLines2(tile, lineIndex3A, lineIndex3B);
                                break;
                            case 4:
                                byte flags = (byte)readBits(8);
                                fillTileWithFlags(tile, flags);
                                break;
                            case 5:
                                int skipCount = readBits(5);
                                // Handle tile skipping (adjust position accordingly)
                                tileX += skipCount * 8;
                                break;
                            case 7:
                                int pattern = readBits(2);
                                bool isCommon = readBits(1) == 1;
                                fillTileWithPattern(tile, pattern, isCommon);
                                break;
                        }

                        // Copy tile to the output layer at the correct position
                        CopyTileToOutputLayer(tile, outputLayer, x, y);
                    }
                }
            }
        }
    }

    private byte[] GetPatternLine(int pattern, int i, byte[] lineA, byte[] lineB)
    {
        switch (pattern)
        {
            case 0:
                return (i % 2 == 0) ? lineA : lineB;
            case 1:
                return (i % 3 == 0) ? lineB : lineA;
            case 2:
                return (i % 3 == 2) ? lineB : lineA;
            case 3:
                return (i % 2 == 1) ? lineB : lineA;
            default:
                return lineA;
        }
    }

    private void CopyTileToOutputLayer(Span<byte> tile, Span<byte> outputLayer, int x, int y)
    {
        for (int i = 0; i < 8; i++)
        {
            tile.Slice(i * 8, 8).CopyTo(outputLayer.Slice(((y + i) * 320) + x));
        }
    }
}
