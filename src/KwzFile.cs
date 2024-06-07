using System;
using System.IO;

namespace LibKwz;

public class KwzFile
{
    public KwzHeader Header { get; }
    public KwzThumbnail Thumbnail { get; }
    public KwzFrameData FrameData { get; }

    public KwzFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);
        Header = KwzHeader.ReadFrom(reader);

        byte[]? frameDataSection = null;
        KwzMeta? meta = null;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            reader.PeekChars(4, out var magic, Encoding.ASCII);
            switch (new string(magic))
            {
                case KwzThumbnail.MAGIC:
                    Thumbnail = KwzThumbnail.ReadFrom(reader);
                    break;
                case KwzFrameData.MAGIC:
                    reader.BaseStream.Seek(4, SeekOrigin.Current);
                    var frameDataSize = reader.ReadUInt32();
                    reader.BaseStream.Seek(-8, SeekOrigin.Current);
                    frameDataSection = reader.ReadBytes((int)frameDataSize + 8);
                    break;
                case KwzMeta.MAGIC:
                    meta = KwzMeta.ReadFrom(reader, Header);
                    break;
                default:
                    // exit loop
                    reader.BaseStream.Seek(0, SeekOrigin.End);
                    break;
            }
        }

        if (frameDataSection != null && meta != null)
        {
            using var ms = new MemoryStream(frameDataSection);
            using var br = new BinaryReader(ms);
            FrameData = KwzFrameData.ReadFrom(br, Header, meta);
        }
    }
}
