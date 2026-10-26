using System.Buffers.Binary;
using System.Runtime.InteropServices;
using LibKwz;

namespace KwzStudio;

public sealed class AudioPreview : IDisposable
{
    private GCHandle _buffer;
    [DllImport("winmm.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool PlaySound(IntPtr sound,IntPtr module,uint flags);
    public void Start(KwzFile note,int frame)
    {
        Stop();
        byte[] pcm=Mix(note);
        int offset=Math.Clamp((int)(frame/note.Header.FramesPerSecond*KwzSound.SampleRate)*2,0,pcm.Length);
        byte[] wave=Wave(pcm.AsSpan(offset));
        _buffer=GCHandle.Alloc(wave,GCHandleType.Pinned);
        if(!PlaySound(_buffer.AddrOfPinnedObject(),IntPtr.Zero,0x0001|0x0004|0x0002))
        {Stop();throw new InvalidOperationException(L.T("Could not open the system audio output device."));}
    }
    public void Stop()
    {
        if(!_buffer.IsAllocated)return;
        PlaySound(IntPtr.Zero,IntPtr.Zero,0);_buffer.Free();
    }
    public static byte[] Mix(KwzFile note)
    {
        double fps=new double[]{.2,.5,1,2,4,6,8,12,20,24,30}[note.Header.FrameSpeed];
        double duration=note.Header.FrameCount/fps;
        if(duration>600)throw new InvalidOperationException(L.T("Synchronized audio preview supports animations up to 10 minutes."));
        var mix=new int[(int)Math.Ceiling(duration*KwzSound.SampleRate)];
        if(note.Sound!=null)
        {
            var sound=note.Sound;
            uint speed=sound.Speed;
            double recorded=speed<=10?new double[]{.2,.5,1,2,4,6,8,12,20,24,30}[speed]:note.Header.FramesPerSecond;
            Add(mix,sound.GetAudioTrack(0),0,fps/recorded);
            byte[][] effects=Enumerable.Range(1,4).Select(sound.GetAudioTrack).ToArray();
            for(int f=0;f<note.Header.FrameCount;f++)
                for(int track=1;track<5;track++)
                    if((note.Meta.Entries[f].SoundEffectFlags&(1<<(track-1)))!=0)
                        Add(mix,effects[track-1],(int)Math.Ceiling(f*(KwzSound.SampleRate/fps)),1);
        }
        byte[] result=new byte[mix.Length*2];
        for(int i=0;i<mix.Length;i++)BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(i*2),(short)Math.Clamp(mix[i],short.MinValue,short.MaxValue));
        return result;
    }
    private static void Add(int[] target,byte[] pcm,int offset,double ratio)
    {
        int count=pcm.Length/2;
        int samples=(int)(count/ratio);
        for(int i=offset;i<target.Length && i-offset<samples;i++)
        {
            double source=(i-offset)*ratio;int index=(int)source;if(index>=count)break;
            int a=BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(index*2));
            int b=index+1<count?BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan((index+1)*2)):0;
            int sample=(int)(a+(b-a)*(source-index));
            target[i]=Math.Clamp(target[i]+sample,short.MinValue,short.MaxValue);
        }
    }
    public static byte[] Wave(ReadOnlySpan<byte> pcm)
    {
        byte[] result=new byte[44+pcm.Length];
        "RIFF"u8.CopyTo(result);BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4),result.Length-8);
        "WAVEfmt "u8.CopyTo(result.AsSpan(8));BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16),16);
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(20),1);BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(22),1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24),KwzSound.SampleRate);BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28),KwzSound.SampleRate*2);
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(32),2);BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(34),16);
        "data"u8.CopyTo(result.AsSpan(36));BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40),pcm.Length);pcm.CopyTo(result.AsSpan(44));return result;
    }
    public static byte[] ReadWav(string path)
    {
        if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException(L.T("WAV files must not exceed 64 MB."));
        byte[] bytes=File.ReadAllBytes(path);
        if(bytes.Length<12||!bytes.AsSpan(0,4).SequenceEqual("RIFF"u8)||!bytes.AsSpan(8,4).SequenceEqual("WAVE"u8))throw new InvalidDataException(L.T("Choose a RIFF/WAVE audio file."));
        int format=0,channels=0,rate=0,bits=0,alignment=0;ReadOnlySpan<byte> data=default;
        for(int p=12;p+8<=bytes.Length;)
        {
            uint length=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+4));
            if(length>int.MaxValue||length>bytes.Length-p-8)throw new InvalidDataException(L.T("The WAV section is truncated."));
            var chunk=bytes.AsSpan(p+8,(int)length);
            if(bytes.AsSpan(p,4).SequenceEqual("fmt "u8)&&length>=16)
            {format=BinaryPrimitives.ReadUInt16LittleEndian(chunk);channels=BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);rate=BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);alignment=BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]);bits=BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);}
            if(bytes.AsSpan(p,4).SequenceEqual("data"u8))data=chunk;
            p=checked(p+8+(int)length+((int)length&1));
        }
        if(channels is <1 or >8||rate is <8000 or >192000||!((format==1&&bits==16)||(format==3&&bits==32))||alignment!=channels*bits/8||data.Length%alignment!=0)
            throw new InvalidDataException(L.T("Supported WAV: PCM16 or Float32, 8–192 kHz, 1–8 channels."));
        int count=data.Length/alignment;
        if(count/(double)rate>600)throw new InvalidDataException(L.T("Audio tracks must not exceed 10 minutes."));
        float[] mono=new float[count];
        for(int i=0;i<count;i++)for(int channel=0;channel<channels;channel++)
        {
            int at=i*alignment+channel*(bits/8);
            float value=bits==16?BinaryPrimitives.ReadInt16LittleEndian(data[at..])/32768f:BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[at..]));
            if(!float.IsFinite(value))throw new InvalidDataException(L.T("The WAV contains invalid samples."));mono[i]+=Math.Clamp(value,-1,1)/channels;
        }
        int output=(int)Math.Round(count*(double)KwzSound.SampleRate/rate);byte[] result=new byte[output*2];
        for(int i=0;i<output;i++)
        {
            double at=i*(double)rate/KwzSound.SampleRate;int index=Math.Min(count-1,(int)at);
            double value=mono[index]+(mono[Math.Min(count-1,index+1)]-mono[index])*(at-index);
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(i*2),(short)Math.Clamp((int)Math.Round(value*32767),short.MinValue,short.MaxValue));
        }
        return result;
    }
    public void Dispose()=>Stop();
}
