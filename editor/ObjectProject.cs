using System.IO.Compression;
using System.Text.Json;

namespace KwzStudio;

/// <summary>Versioned, self-contained ZIP project: base pixels plus editable overlays.</summary>
internal static class ObjectProject
{
    internal sealed record Manifest(string Format,int Version,int CurrentFrame,CanvasObject[] Objects);
    public static byte[] Encode(byte[] baseKwz,CanvasObject[] objects,int frame)
    {
        using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            using(var entry=zip.CreateEntry("base.kwz",CompressionLevel.Optimal).Open())entry.Write(baseKwz);
            using(var entry=zip.CreateEntry("project.json",CompressionLevel.Optimal).Open())
                JsonSerializer.Serialize(entry,new Manifest("KWZStudioProject",1,frame,objects));
        }
        return output.ToArray();
    }
    public static (byte[] Base,CanvasObject[] Objects,int Frame) Decode(string path)
    {
        using var input=File.OpenRead(path);
        if(input.Length>64*1024*1024)throw new InvalidDataException("工程文件过大。");
        using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        byte[] Read(string name,int limit)
        {
            var entry=zip.GetEntry(name)??throw new InvalidDataException("工程文件缺少 "+name);
            if(entry.Length>limit)throw new InvalidDataException("工程文件区段过大。");
            using var stream=entry.Open();byte[] bytes=new byte[(int)entry.Length];stream.ReadExactly(bytes);
            if(stream.ReadByte()!=-1)throw new InvalidDataException("工程文件长度不符。");
            return bytes;
        }
        var manifest=JsonSerializer.Deserialize<Manifest>(Read("project.json",4*1024*1024))??throw new InvalidDataException("工程信息无效。");
        if(manifest.Format!="KWZStudioProject" || manifest.Version!=1 || manifest.Objects==null || manifest.Objects.Length>10000)
            throw new InvalidDataException("不支持此工程格式、版本或对象数量。");
        return (Read("base.kwz",256*1024*1024),manifest.Objects,manifest.CurrentFrame);
    }
    public static void Save(string path,byte[] data)
    {
        string target=System.IO.Path.GetFullPath(path),temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(data);stream.Flush(true); }
            File.Move(temp,target,true);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
}
