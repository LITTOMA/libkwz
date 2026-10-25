using System.Text;

namespace LibKwz;

public static class Utils
{
    public static void SetStringValueStrict(ref char[] field, string value, int maxLength)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException("Value is too long", nameof(value));
        }
        var span = field.AsSpan();
        // Fill the field with null characters
        span.Clear();
        value.AsSpan().CopyTo(span);
    }

    public static void SetBytesValueStrict(ref byte[] field, byte[] value, int maxLength)
    {
        SetBytesValueStrict(field.AsSpan(), value, maxLength);
    }

    public static void SetBytesValueStrict(Span<byte> field, ReadOnlySpan<byte> value, int maxLength)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException("Value is too long", nameof(value));
        }
        // Fill the field with null characters
        field.Clear();
        value.CopyTo(field);
    }

    public static string ToStringTrimed(this char[] chars)
    {
        var span = chars.AsSpan();
        int end = span.IndexOf('\0');
        return new string(end < 0 ? span : span[..end]);
    }

    public static byte[] KwzBase32Decode(this byte[] data)
    {
        return Base32Codec.Decode(data);
    }

    public static byte[] KwzBase32Decode(this string data)
    {
        return Base32Codec.Decode(data);
    }

    public static byte[] KwzBase32Encode(this byte[] data)
    {
        return Encoding.ASCII.GetBytes(Base32Codec.Encode(data));
    }

    public static void WriteWavFile(string filePath, byte[] audioData, int sampleRate, int channels = 1)
    {
        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fileStream))
        {
            // WAV header fields
            int byteRate = sampleRate * channels * sizeof(short);
            int blockAlign = channels * sizeof(short);
            int subChunk2Size = audioData.Length;
            int chunkSize = 36 + subChunk2Size;

            // Write RIFF header
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(chunkSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            // Write fmt sub-chunk
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // SubChunk1Size (16 for PCM)
            writer.Write((short)1); // AudioFormat (1 for PCM)
            writer.Write((short)channels); // NumChannels
            writer.Write(sampleRate); // SampleRate
            writer.Write(byteRate); // ByteRate
            writer.Write((short)blockAlign); // BlockAlign
            writer.Write((short)(sizeof(short) * 8)); // BitsPerSample

            // Write data sub-chunk
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(subChunk2Size);

            // Write audio data
            writer.Write(audioData);
        }
    }

    public static class Base32Codec
    {
        private const string CustomAlphabet = "cwmfjordvegbalksnthpyxquiz012345";
        private static readonly Dictionary<char, byte> CharMap;

        static Base32Codec()
        {
            CharMap = [];
            for (byte i = 0; i < CustomAlphabet.Length; i++)
            {
                CharMap[CustomAlphabet[i]] = i;
            }
        }

        public static byte[] Decode(byte[] data)
        {
            return Decode(Encoding.ASCII.GetString(data));
        }

        public static byte[] Decode(string input)
        {
            ArgumentNullException.ThrowIfNull(input);

            int byteCount = input.Length * 5 / 8;
            byte[] result = new byte[byteCount];

            byte currentByte = 0;
            int bitsRemaining = 8;
            int arrayIndex = 0;

            foreach (char c in input)
            {
                if (!CharMap.TryGetValue(c, out byte value))
                    throw new ArgumentException($"Character '{c}' is not valid in Base32 encoding.");

                int mask;
                if (bitsRemaining > 5)
                {
                    mask = value << (bitsRemaining - 5);
                    currentByte = (byte)(currentByte | mask);
                    bitsRemaining -= 5;
                }
                else
                {
                    mask = value >> (5 - bitsRemaining);
                    currentByte = (byte)(currentByte | mask);
                    result[arrayIndex++] = currentByte;
                    currentByte = (byte)(value << (3 + bitsRemaining));
                    bitsRemaining += 3;
                }
            }

            if (arrayIndex != result.Length)
            {
                result[arrayIndex] = currentByte;
            }

            return result;
        }

        public static string Encode(string data)
        {
            return Encode(Encoding.ASCII.GetBytes(data));
        }

        public static string Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentNullException(nameof(data));

            int charCount = (int)Math.Ceiling(data.Length * 8 / 5.0);
            char[] result = new char[charCount];

            int buffer = data[0];
            int nextByte = 1;
            int bitsLeft = 8;
            int index = 0;

            while (bitsLeft > 0 || nextByte < data.Length)
            {
                if (bitsLeft < 5)
                {
                    if (nextByte < data.Length)
                    {
                        buffer <<= 8;
                        buffer |= data[nextByte++] & 0xFF;
                        bitsLeft += 8;
                    }
                    else
                    {
                        int pad = 5 - bitsLeft;
                        buffer <<= pad;
                        bitsLeft += pad;
                    }
                }

                int indexInAlphabet = (buffer >> (bitsLeft - 5)) & 0x1F;
                bitsLeft -= 5;
                result[index++] = CustomAlphabet[indexInAlphabet];
            }

            return new string(result);
        }
    }

}
