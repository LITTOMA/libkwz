namespace LibKwz.Test;
public class KwzSoundTest
{
    [Fact]
    public void SectionAndSignedPcmEncodingRoundtrip()
    {
        using var file = new KwzFile(Path.Combine(AppContext.BaseDirectory, "sound.kwz"));
        Assert.NotNull(file.Sound);
        var sound = file.Sound;
        var pcm = new byte[400];
        for (int i = 0; i < 200; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(Math.Sin(i * 0.1) * 12000));
        var decoded = sound.DecodeAudioTrack(sound.EncodeAudioTrack(pcm));
        Assert.InRange(decoded.Length, pcm.Length, pcm.Length + 6);
        long error = 0;
        for (int i = 0; i < pcm.Length; i += 2)
            error += Math.Abs(System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i)) - System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(decoded.AsSpan(i)));
        Assert.True(error / 200 < 2000);
        using var ms = new MemoryStream(); sound.WriteTo(new BinaryWriter(ms)); ms.Position = 0;
        var reread = KwzSound.ReadFrom(new BinaryReader(ms));
        for (int i = 0; i < 5; i++) Assert.Equal(sound.GetAudioTrackRaw(i), reread.GetAudioTrackRaw(i));
    }
}
