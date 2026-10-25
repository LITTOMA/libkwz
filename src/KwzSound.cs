using System;
using System.Linq;
using System.IO;

namespace LibKwz;

public class KwzSound
{
    public const string MAGIC = "KSN\x01";
    public const int SampleRate = 16364;

    private static readonly short[] ADPCM_STEP_TABLE =
    [
        7,     8,     9,     10,    11,    12,    13,    14,    16,    17,
        19,    21,    23,    25,    28,    31,    34,    37,    41,    45,
        50,    55,    60,    66,    73,    80,    88,    97,    107,   118,
        130,   143,   157,   173,   190,   209,   230,   253,   279,   307,
        337,   371,   408,   449,   494,   544,   598,   658,   724,   796,
        876,   963,   1060,  1166,  1282,  1411,  1552,  1707,  1878,  2066,
        2272,  2499,  2749,  3024,  3327,  3660,  4026,  4428,  4871,  5358,
        5894,  6484,  7132,  7845,  8630,  9493,  10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767, 0
    ];

    private static readonly sbyte[] ADPCM_INDEX_TABLE_2 =
    [
        -1,  2,
        -1,  2,
    ];

    private static readonly short[] ADPCM_SAMPLE_TABLE_2 = BuildAdpcmSampleTable2();

    private static short[] BuildAdpcmSampleTable2()
    {
        short[] table = new short[90 * 4];
        for (int sample = 0; sample < 4; sample++)
        {
            for (int stepIndex = 0; stepIndex < 90; stepIndex++)
            {
                short step = ADPCM_STEP_TABLE[stepIndex];
                short diff = ((short)(step >> 3));
                if ((sample & 1) != 0)
                {
                    diff += step;
                }
                if ((sample & 2) != 0)
                {
                    diff = (short)-diff;
                }
                table[sample + 4 * stepIndex] = diff;
            }
        }
        return table;
    }

    private static readonly sbyte[] ADPCM_INDEX_TABLE_4 =
    [
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8,
    ];
    private static readonly short[] ADPCM_SAMPLE_TABLE_4 = BuildAdpcmSampleTable4();
    private static short[] BuildAdpcmSampleTable4()
    {
        short[] table = new short[90 * 16];
        for (int sample = 0; sample < 16; sample++)
        {
            for (int stepIndex = 0; stepIndex < 90; stepIndex++)
            {
                short step = ADPCM_STEP_TABLE[stepIndex];
                short diff = ((short)(step >> 3));
                if ((sample & 4) != 0)
                {
                    diff += step;
                }
                if ((sample & 2) != 0)
                {
                    diff += (short)(step >> 1);
                }
                if ((sample & 1) != 0)
                {
                    diff += (short)(step >> 2);
                }
                if ((sample & 8) != 0)
                {
                    diff = (short)-diff;
                }
                table[sample + 16 * stepIndex] = diff;
            }
        }
        return table;
    }


    private char[] _magic = [.. MAGIC];
    private uint _size;
    private uint _speed;
    private uint _bgmSize;
    private uint _soundEffectASize;
    private uint _soundEffectXSize;
    private uint _soundEffectYSize;
    private uint _soundEffectUpSize;
    private uint _audioTrackCrc32;
    private byte[][] _audioTrackBuffers = [[], [], [], [], []];
    private byte[] _padding = [];
    public uint Speed { get => _speed; set => _speed = value; }
    public uint Crc32 => _audioTrackCrc32;
    public byte[] GetAudioTrackRaw(int trackIndex) { CheckTrack(trackIndex); return (byte[])_audioTrackBuffers[trackIndex].Clone(); }
    public void SetAudioTrackRaw(int trackIndex, byte[] data)
    {
        CheckTrack(trackIndex); ArgumentNullException.ThrowIfNull(data);
        if(_audioTrackBuffers[trackIndex].AsSpan().SequenceEqual(data)) return;
        _audioTrackBuffers[trackIndex]=(byte[])data.Clone();
        if(_padding.Length<=3) _padding=new byte[(4-_audioTrackBuffers.Sum(t=>t.Length)%4)%4];
    }
    /// <summary>Replace a mono PCM16 track at 16,364 Hz (little endian); ADPCM compression is lossy.</summary>
    public void SetAudioTrack(int trackIndex,byte[] pcm16) { CheckTrack(trackIndex); SetAudioTrackRaw(trackIndex,EncodeAudioTrack(pcm16)); }
    private static void CheckTrack(int index) { if((uint)index>=5) throw new ArgumentOutOfRangeException(nameof(index)); }

    public static KwzSound ReadFrom(BinaryReader reader)
    {
        var kwzSound = new KwzSound();

        reader
            .ReadChars(4, ref kwzSound._magic, Encoding.ASCII)
            .ReadUInt32(ref kwzSound._size)
            .ReadUInt32(ref kwzSound._speed)
            .ReadUInt32(ref kwzSound._bgmSize)
            .ReadUInt32(ref kwzSound._soundEffectASize)
            .ReadUInt32(ref kwzSound._soundEffectXSize)
            .ReadUInt32(ref kwzSound._soundEffectYSize)
            .ReadUInt32(ref kwzSound._soundEffectUpSize)
            .ReadUInt32(ref kwzSound._audioTrackCrc32);

        if (new string(kwzSound._magic) != MAGIC || kwzSound._size < 28 || kwzSound._size > int.MaxValue) throw new InvalidDataException("Invalid KSN section.");
        long trackTotal = (long)kwzSound._bgmSize + kwzSound._soundEffectASize + kwzSound._soundEffectXSize + kwzSound._soundEffectYSize + kwzSound._soundEffectUpSize;
        if (trackTotal > kwzSound._size - 28) throw new InvalidDataException("KSN tracks exceed section.");
        var audioTrackBuffers = new List<byte[]>();
        audioTrackBuffers.Add(reader.ReadExactlyBytes((int)kwzSound._bgmSize));
        audioTrackBuffers.Add(reader.ReadExactlyBytes((int)kwzSound._soundEffectASize));
        audioTrackBuffers.Add(reader.ReadExactlyBytes((int)kwzSound._soundEffectXSize));
        audioTrackBuffers.Add(reader.ReadExactlyBytes((int)kwzSound._soundEffectYSize));
        audioTrackBuffers.Add(reader.ReadExactlyBytes((int)kwzSound._soundEffectUpSize));
        kwzSound._audioTrackBuffers = audioTrackBuffers.ToArray();
        kwzSound._padding = reader.ReadExactlyBytes((int)(kwzSound._size - 28 - trackTotal));

        return kwzSound;
    }

    public void WriteTo(BinaryWriter writer)
    {
        using var data = new MemoryStream();
        foreach (var track in _audioTrackBuffers) data.Write(track);
        data.Write(_padding);
        byte[] payload = data.ToArray();
        _size = checked((uint)payload.Length + 28);
        _audioTrackCrc32 = System.IO.Hashing.Crc32.HashToUInt32(payload);
        writer.WriteValue(_magic, Encoding.ASCII).WriteValue(_size).WriteValue(_speed);
        foreach (var track in _audioTrackBuffers) writer.Write((uint)track.Length);
        writer.Write(_audioTrackCrc32); writer.Write(payload);
    }

    public byte[] GetAudioTrack(int trackIndex)
    {
        CheckTrack(trackIndex);
        return DecodeAudioTrack(_audioTrackBuffers[trackIndex]);
    }

    public byte[] DecodeAudioTrack(byte[] trackData)
    {

        Span<byte> track = trackData;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        // Initial decoder state
        int prevDiff = 0;
        int prevStepIndex = 40;

        for (int i = 0; i < track.Length; i++)
        {
            byte b = track[i];
            int bitPos = 0;

            while (bitPos < 8)
            {
                int sample;
                int diff;
                int stepIndex;

                // Read a 2-bit sample if the previous step index is < 18, or if only 2 bits are left of the byte
                if (prevStepIndex < 18 || bitPos > 4)
                {
                    sample = (b >> bitPos) & 0x3;
                    diff = prevDiff + ADPCM_SAMPLE_TABLE_2[sample + 4 * prevStepIndex];
                    stepIndex = prevStepIndex + ADPCM_INDEX_TABLE_2[sample];
                    bitPos += 2;
                }
                // Else read a 4-bit sample
                else
                {
                    sample = (b >> bitPos) & 0xF;
                    diff = prevDiff + ADPCM_SAMPLE_TABLE_4[sample + 16 * prevStepIndex];
                    stepIndex = prevStepIndex + ADPCM_INDEX_TABLE_4[sample];
                    bitPos += 4;
                }

                // Clamp step index and diff
                stepIndex = Math.Clamp(stepIndex, 0, 79);
                diff = Math.Clamp(diff, -2048, 2047);

                // Add result to output buffer
                writer.Write((short)(diff * 16));

                // Set previous decoder state
                prevStepIndex = stepIndex;
                prevDiff = diff;
            }
        }

        // Return the filled portion of the output buffer
        return output.ToArray();
    }

    /// <summary>Encode signed little-endian PCM16. A partial final byte is padded with zero codes.</summary>
    public byte[] EncodeAudioTrack(byte[] inputSamples)
    {
        ArgumentNullException.ThrowIfNull(inputSamples);
        if ((inputSamples.Length & 1) != 0) throw new ArgumentException("PCM16 input must have an even byte count.");
        using var output = new MemoryStream();
        int predictor = 0, stepIndex = 40, bit = 0, packed = 0;
        for (int i = 0; i < inputSamples.Length; i += 2)
        {
            int target = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(inputSamples.AsSpan(i)) / 16;
            int bits = stepIndex < 18 || bit > 4 ? 2 : 4;
            int best = 0, error = int.MaxValue, bestPredictor = predictor;
            for (int code = 0; code < (1 << bits); code++)
            {
                int delta = bits == 2 ? ADPCM_SAMPLE_TABLE_2[stepIndex * 4 + code] : ADPCM_SAMPLE_TABLE_4[stepIndex * 16 + code];
                int candidate = Math.Clamp(predictor + delta, -2048, 2047);
                int distance = Math.Abs(target - candidate);
                if (distance < error) { best = code; error = distance; bestPredictor = candidate; }
            }
            packed |= best << bit; bit += bits;
            predictor = bestPredictor;
            stepIndex = Math.Clamp(stepIndex + (bits == 2 ? ADPCM_INDEX_TABLE_2[best] : ADPCM_INDEX_TABLE_4[best]), 0, 79);
            if (bit == 8) { output.WriteByte((byte)packed); bit = 0; packed = 0; }
        }
        if (bit != 0) output.WriteByte((byte)packed);
        return output.ToArray();
    }
}
