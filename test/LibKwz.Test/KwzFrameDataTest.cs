namespace LibKwz.Test;
public class KwzFrameDataTest
{
    [Fact]
    public void ShiftedTableIsAnActualOnePixelRotation()
    {
        var lines = KwzFrameData.BuildLineTable(); var shifted = KwzFrameData.BuildShiftedLineIndexTable();
        Assert.Equal(6561, lines.Length);
        for (int i = 0; i < 6561; i++) for (int x = 0; x < 8; x++) Assert.Equal(lines[i][(x + 1) % 8], lines[shifted[i]][x]);
    }
    [Fact]
    public void DeltaRunsCrossRowsAndLargeTiles()
    {
        var previous = new byte[KwzLayerCodec.PixelCount];
        new Random(42).NextBytes(previous);
        for (int i = 0; i < previous.Length; i++) previous[i] %= 3;
        byte[] changed = (byte[])previous.Clone(); changed[128 * 320 + 128] = (byte)((changed[128 * 320 + 128] + 1) % 3);
        byte[] compressed = KwzLayerCodec.Encode(changed, previous);
        Assert.True(compressed.Length < 100);
        KwzLayerCodec.Decode(compressed, previous);
        Assert.Equal(changed, previous);
    }
    [Fact]
    public void RandomLayersAndUnchangedLayersRoundtrip()
    {
        var random = new Random(7); var pixels = new byte[KwzLayerCodec.PixelCount];
        for (int round = 0; round < 3; round++)
        {
            random.NextBytes(pixels); for (int i = 0; i < pixels.Length; i++) pixels[i] %= 3;
            var compressed = KwzLayerCodec.Encode(pixels); var decoded = new byte[pixels.Length]; KwzLayerCodec.Decode(compressed, decoded);
            Assert.Equal(pixels, decoded);
            var skipped = KwzLayerCodec.Encode(pixels, pixels); Assert.Equal(38, skipped.Length);
            KwzLayerCodec.Decode(skipped, decoded); Assert.Equal(pixels, decoded);
        }
    }
    [Fact]
    public void InvalidBitstreamsAndPixelsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => KwzLayerEncoding.Read(new byte[] { 6, 0 }));
        Assert.Throws<InvalidDataException>(() => KwzLayerEncoding.Read(new byte[] { 1, 0xFF }));
        Assert.Throws<InvalidDataException>(() => KwzLayerEncoding.Read(Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(() => KwzLayerEncoding.Read(new byte[] { 0 }));
        Assert.Throws<ArgumentException>(() => KwzLayerCodec.Encode(new byte[10]));
        var invalid = new byte[KwzLayerCodec.PixelCount]; invalid[1] = 3;
        Assert.Throws<ArgumentException>(() => KwzLayerCodec.Encode(invalid));
    }
    [Fact]
    public void PaletteSettersRetainHighBits()
    {
        var m = new KwzMeta.FrameMeta { Flags = 0x12345678 };
        m.LayerAFirstColorIndex = 5; Assert.Equal(0x12345578u, m.Flags);
        m.LayerASecondColorIndex = 4; m.LayerBFirstColorIndex = 3; m.LayerBSecondColorIndex = 2;
        m.LayerCFirstColorIndex = 1; m.LayerCSecondColorIndex = 5;
        Assert.Equal(0x51234578u, m.Flags);
    }
}
