using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LibKwz;

if (args.Length < 1) { Console.Error.WriteLine("Usage: LibKwz.Verify <Note directory> [flipnote KwzParser.cjs] [report.json]"); return 2; }
string root = Path.GetFullPath(args[0]);
string? oracle = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
string script = Path.Combine(root, "tools/libkwz/test/LibKwz.Verify/oracle.cjs");
string publicKeyResource=Path.Combine(root,"tools/libkwz/src/Resources/KwzVerificationKey.der");
using RSA key=KwzSigning.CreateSigningKey();
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
OracleResult Reference(byte[] bytes)
{
    var start = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(script); start.ArgumentList.Add(oracle!);
    start.ArgumentList.Add(publicKeyResource);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    process.StandardInput.BaseStream.Write(bytes); process.StandardInput.Close();
    if (!process.WaitForExit(60000)) { process.Kill(); throw new TimeoutException("Independent decoder exceeded 60 seconds."); }
    string result = stdout.GetAwaiter().GetResult(); string error = stderr.GetAwaiter().GetResult();
    Require(process.ExitCode == 0, error);
    return JsonSerializer.Deserialize<OracleResult>(result)!;
}
var files = Directory.EnumerateFiles(Path.Combine(root, "unpack"), "*.kwz", SearchOption.AllDirectories)
    .Concat(Directory.EnumerateFiles(root, "*.kwz"))
    .Concat(Directory.EnumerateFiles(Path.Combine(root, "tools/kwz-parser"), "*.kwz", SearchOption.AllDirectories))
    .Concat(Directory.EnumerateFiles(Path.Combine(root, "tools/libkwz/test/LibKwz.Test"), "*.kwz"))
    .OrderBy(p => p).ToArray();
var reports = new List<object>();
int totalFrames = 0; long totalBytes = 0; var types = new SortedSet<int>();
var timer = Stopwatch.StartNew();
foreach (string path in files)
{
    byte[] original = File.ReadAllBytes(path);
    using var file = new KwzFile(new MemoryStream(original),verifyChecksums:true);
    byte[] rebuilt = file.ToArray();
    Require(original.AsSpan().SequenceEqual(rebuilt), $"Binary mismatch: {path}");
    Require(file.SignatureMatchesOriginalBody, "Original signature body changed.");
    OracleResult? reference = oracle == null ? null : Reference(original);
    bool originalSignatureValid=KwzSigning.Verify(original,key);
    byte[] resigned=file.ToArray(key);
    Require(KwzSigning.Verify(resigned,key),"Generated signature invalid.");
    if(originalSignatureValid) Require(original.AsSpan().SequenceEqual(resigned),"Original signature was not reproduced.");
    var frames = new List<string>();
    for (int i = 0; i < file.Header.FrameCount; i++)
    {
        string digest = Hash(file.FrameData.DecodeFrame(i)); frames.Add(digest);
        if (reference != null) Require(reference.frames[i] == digest, $"Reference pixels differ: {path}, frame {i}");
        for (int layer = 0; layer < 3; layer++) types.UnionWith(KwzLayerEncoding.Read(file.FrameData.GetCompressedLayer(i, layer)).TileTypes);
    }
    var audio = new List<string>();
    if (file.Sound != null)
        for (int track = 0; track < 5; track++)
        {
            string digest = Hash(file.Sound.GetAudioTrack(track)); audio.Add(digest);
            if (reference != null) Require(reference.audio[track] == digest, $"Reference audio differs: {path}, track {track}");
        }
    file.FrameData.RecompressFrames();
    byte[] reencoded = file.ToArray();
    using var reread = new KwzFile(new MemoryStream(reencoded));
    for (int i = 0; i < frames.Count; i++) Require(Hash(reread.FrameData.DecodeFrame(i)) == frames[i], $"Recompressed pixels differ: {path}, frame {i}");
    if (oracle != null)
    {
        var recompressedReference = Reference(reencoded);
        Require(frames.SequenceEqual(recompressedReference.frames), $"Independent recompression mismatch: {path}");
        Require(audio.SequenceEqual(recompressedReference.audio), $"Independent recompression audio mismatch: {path}");
    }
    // Exercise the editor as well: a replacement must not leak through subsequent skip runs.
    int editFrame = frames.Count / 2;
    byte[] edited = file.FrameData.DecodeFrame(editFrame);
    for (int layer = 0; layer < 3; layer++)
        for (int y = 96; y < 112; y++)
        for (int x = 128; x < 144; x++)
        {
            int p = layer * KwzLayerCodec.PixelCount + y * 320 + x;
            edited[p] = (byte)((edited[p] + 1) % 3);
        }
    file.FrameData.SetFramePixels(editFrame, edited);
    byte[] editedFile = file.ToArray(key);
    Require(!file.SignatureMatchesOriginalBody, "Edited body was not marked as changed.");
    if (oracle != null)
    {
        var editedReference = Reference(editedFile);
        Require(editedReference.signatureValid==true,$"Independent signature check failed: {path}");
        for (int i = 0; i < frames.Count; i++)
            Require(editedReference.frames[i] == (i == editFrame ? Hash(edited) : frames[i]), $"Independent edit mismatch: {path}, frame {i}");
    }
    // Nonsequential remapping tests first-frame dependencies, duplication, and a new blank frame.
    int[] map=[frames.Count-1,0,editFrame,editFrame,-1];
    string[] remappedHashes=map.Select(i=>i<0?Hash(new byte[KwzFrameData.FramePixelCount]):i==editFrame?Hash(edited):frames[i]).ToArray();
    file.FrameData.RemapFrames(map);
    file.UpdateThumbnail(0);
    byte[] remapped=file.ToArray(key);
    using(var remappedNote=new KwzFile(new MemoryStream(remapped),verifyChecksums:true))
    {
        Require(remappedNote.VerifySignature(key),"Remapped signature invalid.");
        for(int i=0;i<map.Length;i++) Require(Hash(remappedNote.FrameData.DecodeFrame(i))==remappedHashes[i],"Remapped pixels differ.");
    }
    if(oracle!=null)
    {
        var remappedReference=Reference(remapped);
        Require(remappedReference.signatureValid==true,"Independent remapped signature invalid.");
        Require(remappedHashes.SequenceEqual(remappedReference.frames),"Independent remapped pixels differ.");
        Require(audio.SequenceEqual(remappedReference.audio),"Remapping changed audio tracks.");
    }
    totalFrames += frames.Count; totalBytes += original.Length;
    reports.Add(new { path = Path.GetRelativePath(root, path), frames = frames.Count, bytes = original.Length, sha256 = Hash(original), rebuiltSha256 = Hash(rebuilt), recompressedBytes = reencoded.Length, binaryIdentical = true, originalSignatureValid, validOriginalSignatureReproduced = originalSignatureValid, editedSignatureVerified=true, remappedPixelsAndSignatureVerified=true, independentSigningAndRemapping=reference!=null, independentPixelsAndAudio = reference != null, recompressedIndependentPixelsAndAudio = reference != null, editedFrameAndSuccessorsVerified = reference != null, frameHashes = frames, audioHashes = audio });
    Console.WriteLine($"PASS {Path.GetRelativePath(root, path)} ({frames.Count} frames, {original.Length} bytes)");
}
using(var blank=KwzFile.CreateBlank(3,24))
{
    blank.UpdateThumbnail(0);
    var data=blank.ToArray(key);
    Require(KwzSigning.Verify(data,key),"New animation signature invalid.");
    if(oracle!=null)
    {
        var result=Reference(data);
        Require(result.frames.Length==3 && result.frames.All(h=>h==Hash(new byte[KwzFrameData.FramePixelCount])),"Independent new-animation pixels differ.");
        Require(result.signatureValid==true,"Independent new-animation signature invalid.");
    }
}
var summary = new { files = files.Length, frames = totalFrames, layers = totalFrames * 3, bytes = totalBytes, seconds = Math.Round(timer.Elapsed.TotalSeconds, 2), tileTypes = types.ToArray(), independentDecoder = oracle == null ? null : "flipnote.js 6.3.0", newAnimationVerified=true, reports };
if (args.Length > 2) File.WriteAllText(args[2], JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS all {files.Length} files, {totalFrames} frames, {totalFrames * 3} layers, {totalBytes} bytes; tile types: {string.Join(',', types)}; {timer.Elapsed.TotalSeconds:F1}s");
return 0;
record OracleResult(string[] frames, string[] audio,bool? signatureValid);
