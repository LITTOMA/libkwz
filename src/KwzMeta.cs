using SixLabors.ImageSharp;

namespace LibKwz;

public class KwzMeta
{
    public const string MAGIC = "KMI\x05";
    
    public static readonly Color[] FramePalette = new Color[]
    {
            Color.FromRgba(0xFF, 0xFF, 0xFF, 0xFF),
            Color.FromRgba(0x14, 0x14, 0x14, 0xFF),
            Color.FromRgba(0xFF, 0x17, 0x17, 0xFF),
            Color.FromRgba(0xFF, 0xE6, 0x00, 0xFF),
            Color.FromRgba(0x00, 0x82, 0x32, 0xFF),
            Color.FromRgba(0x06, 0xAE, 0xFF, 0xFF),
            Color.FromRgba(0x00, 0x00, 0x00, 0x00),
    };

    private char[] _magic = new char[4];
    private uint _size;
    private byte[]? _data;

    public char[] Magic => _magic;
    public uint Size => _size;
    public byte[] Data { get => _data; set => _data = value; }
    public FrameMeta[] Entries { get; private set; } = Array.Empty<FrameMeta>();

    // Index shortcut
    public FrameMeta? this[int index] => Entries == null || Entries.Length <= index ? null : Entries[index];

    public static KwzMeta ReadFrom(BinaryReader reader, KwzHeader header)
    {
        var meta = new KwzMeta();
        reader
            .ReadChars(4, ref meta._magic, Encoding.ASCII)
            .ReadUInt32(ref meta._size);

        meta._data = reader.ReadBytes((int)meta._size);

        var entries = new List<FrameMeta>();
        using var ms = new MemoryStream(meta._data);
        using var br = new BinaryReader(ms);
        for (int i = 0; i < header.FrameCount; i++)
        {
            entries.Add(FrameMeta.ReadFrom(br));
        }
        meta.Entries = entries.ToArray();

        return meta;
    }

    public void WriteTo(BinaryWriter writer)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        foreach (var entry in Entries)
        {
            entry.WriteTo(bw);
        }
        _data = ms.ToArray();
        _size = (uint)_data.Length;

        writer
            .WriteValue([.. MAGIC], Encoding.ASCII)
            .WriteValue(_size)
            .WriteValue(_data);
    }


    public class FrameMeta
    {

        private uint _flags;
        private ushort _layerASize;
        private ushort _layerBSize;
        private ushort _layerCSize;
        private byte[] _frameAuthorId = new byte[10];
        private byte _layerADepth;
        private byte _layerBDepth;
        private byte _layerCDepth;
        private byte _soundEffectFlags;
        private ushort _unknown;
        private ushort _cameraFlags;

        public uint Flags { get => _flags; set => _flags = value; }
        public ushort LayerASize { get => _layerASize; set => _layerASize = value; }
        public ushort LayerBSize { get => _layerBSize; set => _layerBSize = value; }
        public ushort LayerCSize { get => _layerCSize; set => _layerCSize = value; }
        public ushort[] LayerSizes => [_layerASize, _layerBSize, _layerCSize];
        public byte[] FrameAuthorId => _frameAuthorId;
        public byte LayerADepth { get => _layerADepth; set => _layerADepth = value; }
        public byte LayerBDepth { get => _layerBDepth; set => _layerBDepth = value; }
        public byte LayerCDepth { get => _layerCDepth; set => _layerCDepth = value; }
        public byte[] LayerDepths => [_layerADepth, _layerBDepth, _layerCDepth];
        public byte SoundEffectFlags { get => _soundEffectFlags; set => _soundEffectFlags = value; }
        public ushort Unknown { get => _unknown; set => _unknown = value; }
        public ushort CameraFlags { get => _cameraFlags; set => _cameraFlags = value; }

        public Color PaperColor => FramePalette[PaperColorIndex];
        public Color LayerAFirstColor => FramePalette[LayerAFirstColorIndex];
        public Color LayerASecondColor => FramePalette[LayerASecondColorIndex];
        public Color LayerBFirstColor => FramePalette[LayerBFirstColorIndex];
        public Color LayerBSecondColor => FramePalette[LayerBSecondColorIndex];
        public Color LayerCFirstColor => FramePalette[LayerCFirstColorIndex];
        public Color LayerCSecondColor => FramePalette[LayerCSecondColorIndex];

        #region Flags
        public byte PaperColorIndex
        {
            get => (byte)(_flags & 0xF);
            set => _flags = (uint)(((uint)(_flags & 0xFFFFFFF0)) | (byte)(value & 0xF));
        }
        public bool LayerADiffingFlag
        {
            get => ((_flags >> 4) & 0x1) == 1;
            set => _flags = (uint)(((uint)(_flags & 0xFFFFFFEF) | (byte)((byte)(value ? 1 : 0) << 4)));
        }
        public bool LayerBDiffingFlag
        {
            get => ((_flags >> 5) & 0x1) == 1;
            set => _flags = (uint)(((uint)(_flags & 0xFFFFFFDF) | (byte)((byte)(value ? 1 : 0) << 5)));
        }
        public bool LayerCDiffingFlag
        {
            get => ((_flags >> 6) & 0x1) == 1;
            set => _flags = (uint)(((uint)(_flags & 0xFFFFFFBF) | (byte)((byte)(value ? 1 : 0) << 6)));
        }
        public bool IsFrameBasedOnPrevFrame
        {
            get => ((_flags >> 7) & 0x1) == 1;
            set => _flags = (uint)(((uint)(_flags & 0xFFFFFF7F) | (byte)((byte)(value ? 1 : 0) << 7)));
        }
        public byte LayerAFirstColorIndex
        {
            get => (byte)((_flags >> 8) & 0xF);
            set => _flags = (uint)(((uint)(_flags & 0xFFFFF0FF) | (byte)((byte)(value & 0xF) << 8)));
        }
        public byte LayerASecondColorIndex
        {
            get => (byte)((_flags >> 12) & 0xF);
            set => _flags = (uint)(((uint)(_flags & 0xFFFF0FFF) | (byte)((byte)(value & 0xF) << 12)));
        }
        public byte LayerBFirstColorIndex
        {
            get => (byte)((_flags >> 16) & 0xF);
            set => _flags = (uint)(((uint)(_flags & 0xFFF0FFFF) | (byte)((byte)(value & 0xF) << 16)));
        }
        public byte LayerBSecondColorIndex
        {
            get => (byte)((_flags >> 20) & 0xF);
            set => _flags = (uint)(((uint)(_flags & 0xFF0FFFFF) | (byte)((byte)(value & 0xF) << 20)));
        }
        public byte LayerCFirstColorIndex
        {
            get => (byte)((_flags >> 24) & 0xF);
            set => _flags = (uint)(((uint)(_flags & 0xF0FFFFFF) | (byte)((byte)(value & 0xF) << 24)));
        }
        public byte LayerCSecondColorIndex
        {
            get => (byte)((_flags >> 28) & 0xF);
            set => _flags = (uint)(((uint)(_flags & 0x0FFFFFFF) | (byte)((byte)(value & 0xF) << 28)));
        }
        #endregion


        public static FrameMeta ReadFrom(BinaryReader reader)
        {
            var meta = new FrameMeta();
            reader
                .ReadUInt32(ref meta._flags)
                .ReadUInt16(ref meta._layerASize)
                .ReadUInt16(ref meta._layerBSize)
                .ReadUInt16(ref meta._layerCSize)
                .ReadBytes(10, ref meta._frameAuthorId)
                .ReadByte(ref meta._layerADepth)
                .ReadByte(ref meta._layerBDepth)
                .ReadByte(ref meta._layerCDepth)
                .ReadByte(ref meta._soundEffectFlags)
                .ReadUInt16(ref meta._unknown)
                .ReadUInt16(ref meta._cameraFlags);

            return meta;
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer
                .WriteValue(_flags)
                .WriteValue(_layerASize)
                .WriteValue(_layerBSize)
                .WriteValue(_layerCSize)
                .WriteValue(_frameAuthorId)
                .WriteValue(_layerADepth)
                .WriteValue(_layerBDepth)
                .WriteValue(_layerCDepth)
                .WriteValue(_soundEffectFlags)
                .WriteValue(_unknown)
                .WriteValue(_cameraFlags);
        }

        public override string ToString()
        {
            return $"Flags: {_flags}, Layer A Size: {_layerASize}, Layer B Size: {_layerBSize}, Layer C Size: {_layerCSize}, Frame Author ID: {Encoding.ASCII.GetString(_frameAuthorId)}, Layer A Depth: {_layerADepth}, Layer B Depth: {_layerBDepth}, Layer C Depth: {_layerCDepth}, Sound Effect Flags: {_soundEffectFlags}, Unknown: {_unknown}, Camera Flags: {_cameraFlags}";
        }

    }
}