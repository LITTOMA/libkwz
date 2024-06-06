using System;
using System.IO;

namespace LibKwz;

public class KwzFile
{
    public KwzHeader Header { get; }

    public KwzFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);
        Header = KwzHeader.ReadFrom(reader);
    }
}
