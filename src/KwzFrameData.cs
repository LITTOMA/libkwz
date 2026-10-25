using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
namespace LibKwz;

public class KwzFrameData : IDisposable
{
    public const string MAGIC = "KMC\x02";
    public const int FramePixelCount = KwzLayerCodec.PixelCount * 3;
    private char[] _magic = [.. MAGIC];
    private byte[] _data = [];
    private int[] _frameOffsets = [];
    private KwzMeta _frameMeta = null!;
    private KwzHeader _header = null!;
    private readonly byte[] _frameDecodeBuffer = new byte[FramePixelCount];
    private int _prevDecodedFrame = -1;
    private List<(Image LayerA, Image LayerB, Image LayerC)>? _decodedFrames;
    private List<Image>? _compositeFrames;
    private ulong[]? _renderSnapshot;
    public char[] Magic => _magic;
    public uint Size => (uint)_data.Length + 4;
    public uint Crc32 { get; set; }
    public byte[] Data { get => _data; set { _data = value; Reindex(); Invalidate(); } }
    public int FrameCount => _frameMeta.Entries.Length;
    // Compatibility views are lazy. Prefer FrameToImage for bounded-memory processing.
    public List<(Image LayerA, Image LayerB, Image LayerC)> DecodedFrames { get { EnsureImages(); return _decodedFrames!; } }
    public List<Image> CompositeFrames { get { EnsureImages(); return _compositeFrames!; } }
    public static byte[][] BuildLineTable() => KwzLayerCodec.BuildLineTable();
    public static ushort[] BuildShiftedLineIndexTable() => KwzLayerCodec.Shift.Select(i => (ushort)i).ToArray();
    public static IEnumerable<int> RangeWithStep(int start, int end, int step)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        for (int i = start; i < end; i += step) yield return i;
    }
    internal static KwzFrameData CreateBlank(KwzHeader header, KwzMeta meta)
    {
        var entry = new KwzMeta.FrameMeta { LayerAFirstColorIndex=1, LayerASecondColorIndex=2,
            LayerBFirstColorIndex=1, LayerBSecondColorIndex=5, LayerCFirstColorIndex=1, LayerCSecondColorIndex=4 };
        byte[] layer = KwzLayerCodec.Encode(new byte[KwzLayerCodec.PixelCount]);
        entry.LayerASize = entry.LayerBSize = entry.LayerCSize = checked((ushort)layer.Length);
        entry.LayerADiffingFlag = entry.LayerBDiffingFlag = entry.LayerCDiffingFlag = true;
        meta.ReplaceEntries([entry]);
        var result = new KwzFrameData { _header=header, _frameMeta=meta, _data=[..layer,..layer,..layer] };
        result.Reindex();
        return result;
    }

    /// <summary>Reorder, duplicate, or remove frames. -1 inserts a blank frame. Pixel dependencies are rebuilt in bounded memory.</summary>
    public void RemapFrames(IReadOnlyList<int> sourceFrames)
    {
        ArgumentNullException.ThrowIfNull(sourceFrames);
        if (sourceFrames.Count is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(sourceFrames));
        if (sourceFrames.Any(i => i < -1 || i >= FrameCount)) throw new ArgumentOutOfRangeException(nameof(sourceFrames));
        if(sourceFrames.SequenceEqual(Enumerable.Range(0,FrameCount))) return;
        using var output = new MemoryStream();
        var entries = new KwzMeta.FrameMeta[sourceFrames.Count];
        byte[] previous = new byte[FramePixelCount];
        for (int frame = 0; frame < sourceFrames.Count; frame++)
        {
            int source = sourceFrames[frame];
            byte[] pixels = source < 0 ? new byte[FramePixelCount] : DecodeFrame(source);
            var entry = _frameMeta.Entries[Math.Max(0, source)].Clone();
            if (source < 0) { entry.SoundEffectFlags = 0; entry.CameraFlags = 0; }
            ushort[] sizes = new ushort[3];
            bool[] independent = new bool[3];
            for (int layer = 0; layer < 3; layer++)
            {
                var target = pixels.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount);
                byte[] encoded = KwzLayerCodec.Encode(target, frame == 0 ? default : previous.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount));
                sizes[layer] = checked((ushort)encoded.Length);
                independent[layer] = !KwzLayerEncoding.Read(encoded).UsesPreviousLayer;
                output.Write(encoded);
            }
            entry.LayerASize=sizes[0]; entry.LayerBSize=sizes[1]; entry.LayerCSize=sizes[2];
            entry.LayerADiffingFlag=independent[0]; entry.LayerBDiffingFlag=independent[1]; entry.LayerCDiffingFlag=independent[2];
            entry.IsFrameBasedOnPrevFrame=!independent.All(x=>x);
            entries[frame]=entry; previous=pixels;
        }
        WriteEditedPadding(output);
        int thumb=sourceFrames.ToList().IndexOf(_header.ThumbnailFrameIndex);
        _header.ThumbnailFrameIndex=(ushort)Math.Max(0,thumb);
        _header.FrameCount=(ushort)entries.Length;
        _frameMeta.ReplaceEntries(entries);
        _data=output.ToArray(); Reindex(); Invalidate();
    }

    public byte[] DecodeLayer(int frameIndex,int layerIndex)
    {
        if((uint)layerIndex>=3) throw new ArgumentOutOfRangeException(nameof(layerIndex));
        return DecodeFrame(frameIndex).AsSpan(layerIndex*KwzLayerCodec.PixelCount,KwzLayerCodec.PixelCount).ToArray();
    }
    public void SetLayerPixels(int frameIndex,int layerIndex,ReadOnlySpan<byte> pixels)
    {
        if((uint)layerIndex>=3) throw new ArgumentOutOfRangeException(nameof(layerIndex));
        KwzLayerCodec.ValidatePixels(pixels);
        byte[] frame=DecodeFrame(frameIndex);
        pixels.CopyTo(frame.AsSpan(layerIndex*KwzLayerCodec.PixelCount,KwzLayerCodec.PixelCount));
        SetFramePixels(frameIndex,frame);
    }
    public void InsertBlankFrame(int index)
    {
        if((uint)index>FrameCount) throw new ArgumentOutOfRangeException(nameof(index));
        var map=Enumerable.Range(0,FrameCount).ToList(); map.Insert(index,-1); RemapFrames(map);
    }
    /// <summary>Insert a duplicate at the given destination index, before the frame previously at that index.</summary>
    public void DuplicateFrame(int sourceIndex,int destinationIndex)
    {
        CheckFrame(sourceIndex);
        if((uint)destinationIndex>FrameCount) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        var map=Enumerable.Range(0,FrameCount).ToList(); map.Insert(destinationIndex,sourceIndex); RemapFrames(map);
    }
    public void DeleteFrame(int index)
    {
        CheckFrame(index);
        if(FrameCount==1) throw new InvalidOperationException("An animation must retain at least one frame.");
        RemapFrames(Enumerable.Range(0,FrameCount).Where(i=>i!=index).ToArray());
    }
    /// <summary>Move one frame to its final zero-based destination index.</summary>
    public void MoveFrame(int sourceIndex,int destinationIndex)
    {
        CheckFrame(sourceIndex); CheckFrame(destinationIndex);
        var map=Enumerable.Range(0,FrameCount).ToList(); map.RemoveAt(sourceIndex); map.Insert(destinationIndex,sourceIndex); RemapFrames(map);
    }
    public static KwzFrameData ReadFrom(BinaryReader reader, KwzHeader header, KwzMeta meta)
    {
        var result = new KwzFrameData { _header = header, _frameMeta = meta };
        reader.ReadChars(4, ref result._magic, Encoding.ASCII);
        uint size = reader.ReadUInt32();
        if (new string(result._magic) != MAGIC || size < 4 || size > int.MaxValue) throw new InvalidDataException("Invalid KMC section.");
        result.Crc32 = reader.ReadUInt32();
        result._data = reader.ReadExactlyBytes((int)size - 4);
        if (meta.Entries.Length != header.FrameCount) throw new InvalidDataException("Frame count does not match KMI.");
        result.Reindex();
        return result;
    }
    private void Reindex()
    {
        _frameOffsets = new int[FrameCount + 1];
        for (int i = 0; i < FrameCount; i++)
        {
            var sizes = _frameMeta.Entries[i].LayerSizes;
            if (sizes.Any(s => s < 2 || (s & 1) != 0)) throw new InvalidDataException($"Invalid layer size in frame {i}.");
            _frameOffsets[i + 1] = checked(_frameOffsets[i] + sizes.Sum(s => (int)s));
        }
        if (_frameOffsets[^1] > _data.Length) throw new InvalidDataException("KMI layer sizes exceed KMC data.");
    }
    public ReadOnlyMemory<byte> GetCompressedLayer(int frameIndex, int layerIndex)
    {
        CheckFrame(frameIndex);
        if ((uint)layerIndex >= 3) throw new ArgumentOutOfRangeException(nameof(layerIndex));
        var sizes = _frameMeta.Entries[frameIndex].LayerSizes;
        int offset = _frameOffsets[frameIndex];
        for (int i = 0; i < layerIndex; i++) offset += sizes[i];
        return _data.AsMemory(offset, sizes[layerIndex]);
    }
    private void CheckFrame(int index)
    {
        if ((uint)index >= FrameCount) throw new ArgumentOutOfRangeException(nameof(index));
    }
    /// <summary>Returns an owned copy of all three indexed layers, including hidden layers. Supports arbitrary frame order.</summary>
    public byte[] DecodeFrame(int frameIndex, int diffingFlag = 7, bool isPrevFrame = false)
    {
        CheckFrame(frameIndex);
        // Iterative replay avoids stack overflow and stale data on backwards seeks.
        // Flags are optimization hints; skip instructions determine actual dependencies.
        if (frameIndex < _prevDecodedFrame) { Array.Clear(_frameDecodeBuffer); _prevDecodedFrame = -1; }
        for (int frame = _prevDecodedFrame + 1; frame <= frameIndex; frame++)
            for (int layer = 0; layer < 3; layer++)
                KwzLayerCodec.Decode(GetCompressedLayer(frame, layer), _frameDecodeBuffer.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount));
        _prevDecodedFrame = frameIndex;
        return (byte[])_frameDecodeBuffer.Clone();
    }
    public void DecompressLayer(ReadOnlyMemory<byte> compressedLayer, Span<byte> outputLayer) => KwzLayerCodec.Decode(compressedLayer, outputLayer);
    public byte[] CompressLayer(ReadOnlySpan<byte> pixels, ReadOnlySpan<byte> previous = default) => KwzLayerCodec.Encode(pixels, previous);
    /// <summary>Replace one frame; re-encode the next frame to preserve its appearance despite delta dependencies.</summary>
    public void SetFramePixels(int frameIndex, ReadOnlySpan<byte> pixels)
    {
        CheckFrame(frameIndex);
        if (pixels.Length != FramePixelCount) throw new ArgumentException("Expected three 320x240 indexed layers.");
        for (int layer = 0; layer < 3; layer++) KwzLayerCodec.ValidatePixels(pixels.Slice(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount));
        if(DecodeFrame(frameIndex).AsSpan().SequenceEqual(pixels)) return;
        byte[] previous = frameIndex == 0 ? new byte[FramePixelCount] : DecodeFrame(frameIndex - 1);
        byte[]? next = frameIndex + 1 < FrameCount ? DecodeFrame(frameIndex + 1) : null;
        var replacements = new List<byte[]>();
        var keyframes = new List<bool>();
        for (int f = 0; f < (next == null ? 1 : 2); f++)
        {
            ReadOnlySpan<byte> target = f == 0 ? pixels : next;
            ReadOnlySpan<byte> basis = f == 0 ? previous : pixels;
            for (int layer = 0; layer < 3; layer++)
            {
                var slice = target.Slice(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount);
                var encoded = KwzLayerCodec.Encode(slice, frameIndex == 0 && f == 0 ? default : basis.Slice(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount));
                if (encoded.Length > ushort.MaxValue) throw new InvalidDataException("Compressed layer is too large.");
                replacements.Add(encoded); keyframes.Add(!KwzLayerEncoding.Read(encoded).UsesPreviousLayer);
            }
        }
        int endFrame = frameIndex + (next == null ? 1 : 2);
        using var output = new MemoryStream();
        output.Write(_data.AsSpan(0, _frameOffsets[frameIndex]));
        foreach (var layer in replacements) output.Write(layer);
        output.Write(_data.AsSpan(_frameOffsets[endFrame], _frameOffsets[^1] - _frameOffsets[endFrame]));
        WriteEditedPadding(output);
        for (int f = frameIndex; f < endFrame; f++)
        {
            int p = (f - frameIndex) * 3;
            var meta = _frameMeta.Entries[f];
            meta.LayerASize = (ushort)replacements[p].Length; meta.LayerBSize = (ushort)replacements[p + 1].Length; meta.LayerCSize = (ushort)replacements[p + 2].Length;
            meta.LayerADiffingFlag = keyframes[p]; meta.LayerBDiffingFlag = keyframes[p + 1]; meta.LayerCDiffingFlag = keyframes[p + 2];
            meta.IsFrameBasedOnPrevFrame = !keyframes.Skip(p).Take(3).All(b => b);
        }
        _data = output.ToArray(); Reindex(); Invalidate();
    }

    /// <summary>Discard original compression choices and compress all decoded frames anew.</summary>
    public void RecompressFrames(bool interFrame = true)
    {
        using var output = new MemoryStream();
        byte[] previous = new byte[FramePixelCount];
        var sizes = new ushort[FrameCount, 3];
        var independent = new bool[FrameCount, 3];
        for (int frame = 0; frame < FrameCount; frame++)
        {
            byte[] pixels = DecodeFrame(frame);
            for (int layer = 0; layer < 3; layer++)
            {
                var target = pixels.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount);
                var basis = previous.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount);
                byte[] encoded = KwzLayerCodec.Encode(target, frame == 0 || !interFrame ? default : basis);
                sizes[frame, layer] = checked((ushort)encoded.Length);
                independent[frame, layer] = !KwzLayerEncoding.Read(encoded).UsesPreviousLayer;
                output.Write(encoded);
            }
            previous = pixels;
        }
        WriteEditedPadding(output);
        for (int f = 0; f < FrameCount; f++)
        {
            var meta = _frameMeta.Entries[f];
            meta.LayerASize = sizes[f, 0]; meta.LayerBSize = sizes[f, 1]; meta.LayerCSize = sizes[f, 2];
            meta.LayerADiffingFlag = independent[f, 0]; meta.LayerBDiffingFlag = independent[f, 1]; meta.LayerCDiffingFlag = independent[f, 2];
            meta.IsFrameBasedOnPrevFrame = !(independent[f, 0] && independent[f, 1] && independent[f, 2]);
        }
        _data = output.ToArray(); Reindex(); Invalidate();
    }

    private void WriteEditedPadding(MemoryStream output)
    {
        // Preserve opaque extensions, but regenerate ordinary section alignment after edits.
        if (_data.Length - _frameOffsets[^1] > 3) output.Write(_data.AsSpan(_frameOffsets[^1]));
        while ((output.Length & 3) != 0) output.WriteByte(0);
    }
    /// <summary>Rebuild KMC from decoded pixels, retaining original compression choices and unused bits.</summary>
    public void WriteTo(BinaryWriter writer)
    {
        using var output = new MemoryStream(_data.Length);
        byte[] current = new byte[FramePixelCount], previous = new byte[FramePixelCount];
        for (int frame = 0; frame < FrameCount; frame++)
        {
            current.CopyTo(previous, 0);
            for (int layer = 0; layer < 3; layer++)
            {
                var encoding = KwzLayerEncoding.Read(GetCompressedLayer(frame, layer));
                var target = current.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount);
                encoding.Decode(target);
                output.Write(encoding.EncodePixels(target, previous.AsSpan(layer * KwzLayerCodec.PixelCount, KwzLayerCodec.PixelCount)));
            }
        }
        // KMC alignment bytes are not included in KMI layer lengths.
        output.Write(_data.AsSpan(_frameOffsets[^1]));
        byte[] data = output.ToArray();
        Crc32 = System.IO.Hashing.Crc32.HashToUInt32(data);
        writer.WriteValue(_magic, Encoding.ASCII).WriteValue((uint)data.Length + 4).WriteValue(Crc32).WriteValue(data);
    }
    public (Image<Rgba32>[] layers, Image composite) FrameToImage(int index)
    {
        byte[] pixels = DecodeFrame(index);
        var meta = _frameMeta.Entries[index];
        Color[] colors = [meta.LayerAFirstColor, meta.LayerASecondColor, meta.LayerBFirstColor, meta.LayerBSecondColor, meta.LayerCFirstColor, meta.LayerCSecondColor];
        var layers = new Image<Rgba32>[3];
        for (int layer = 0; layer < 3; layer++)
        {
            layers[layer] = new Image<Rgba32>(320, 240);
            for (int y = 0; y < 240; y++)
            for (int x = 0; x < 320; x++)
            {
                int value = pixels[layer * KwzLayerCodec.PixelCount + y * 320 + x];
                if (value != 0) layers[layer][x, y] = colors[layer * 2 + value - 1];
            }
        }
        var composite = new Image<Rgba32>(320, 240, meta.PaperColor);
        bool[] visible = [_header.LayerAVisible, _header.LayerBVisible, _header.LayerCVisible];
        foreach (int layer in new[] { 2, 1, 0 }.OrderByDescending(i => meta.LayerDepths[i]))
            if (visible[layer]) composite.Mutate(ctx => ctx.DrawImage(layers[layer], 1));
        return (layers, composite);
    }
    private void EnsureImages()
    {
        // Public metadata remains mutable for compatibility; detect palette/depth/visibility changes.
        ulong[] state=_frameMeta.Entries.Select(m=>(ulong)m.Flags | ((ulong)m.LayerADepth<<32) |
            ((ulong)m.LayerBDepth<<40) | ((ulong)m.LayerCDepth<<48) | ((ulong)_header.LayerVisibilityFlags<<56)).ToArray();
        if (_decodedFrames != null && _renderSnapshot!=null && state.AsSpan().SequenceEqual(_renderSnapshot)) return;
        Dispose();
        _decodedFrames = []; _compositeFrames = [];
        for (int i = 0; i < FrameCount; i++)
        {
            var (layers, composite) = FrameToImage(i);
            _decodedFrames.Add((layers[0], layers[1], layers[2])); _compositeFrames.Add(composite);
        }
        _renderSnapshot=state;
    }
    private void Invalidate() { Dispose(); Array.Clear(_frameDecodeBuffer); _prevDecodedFrame = -1; }
    public void Dispose()
    {
        if (_decodedFrames != null) foreach (var (a,b,c) in _decodedFrames) { a.Dispose(); b.Dispose(); c.Dispose(); }
        if (_compositeFrames != null) foreach (var image in _compositeFrames) image.Dispose();
        _decodedFrames = null; _compositeFrames = null; _renderSnapshot=null;
    }
}
