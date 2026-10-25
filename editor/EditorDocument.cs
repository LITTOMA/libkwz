using System.Security.Cryptography;
using System.Text.Json;
using LibKwz;

namespace KwzStudio;

public sealed class EditorDocument : IDisposable
{
    private sealed record State(string Name,byte[] Bytes,CanvasObject[] Objects,Guid? Selection,int Frame,byte[] Hash);
    private readonly List<State> _history=[];
    private readonly List<CanvasObject> _objects=[];
    private int _position;
    private byte[] _savedHash=[];
    public KwzFile Note { get; private set; }=KwzFile.CreateBlank();
    public string? Path { get; private set; }
    public bool IsProject=>Path?.EndsWith(".kwzproj",StringComparison.OrdinalIgnoreCase)==true;
    public int Frame { get; private set; }
    public int Revision { get; private set; }
    public byte[] BasePixels { get; private set; }=[];
    public byte[] Pixels { get; private set; }=[];
    public IReadOnlyList<CanvasObject> Objects=>_objects.AsReadOnly();
    public Guid? SelectedId { get; private set; }
    public CanvasObject? SelectedObject=>_objects.FirstOrDefault(o=>o.Id==SelectedId && o.Frame==Frame);
    public event Action? Changed;
    public bool IsDirty=>!_savedHash.AsSpan().SequenceEqual(_history[_position].Hash);
    public bool CanUndo=>_position>0;
    public bool CanRedo=>_position+1<_history.Count;
    public string[] History=>_history.Take(_position+1).Select(s=>s.Name).Reverse().ToArray();
    public EditorDocument()=>Reset("新建动画",false);
    public void Open(string path)
    {
        bool isProject=path.EndsWith(".kwzproj",StringComparison.OrdinalIgnoreCase);
        var project=isProject?ObjectProject.Decode(path):(File.ReadAllBytes(path),Array.Empty<CanvasObject>(),0);
        var next=new KwzFile(new MemoryStream(project.Item1),verifyChecksums:true);
        try
        {
            foreach(var item in project.Item2) { if(item==null)throw new InvalidDataException("工程对象为空。");item.Validate(next.Header.FrameCount); }
            if(project.Item2.Select(o=>o.Id).Distinct().Count()!=project.Item2.Length)throw new InvalidDataException("工程对象编号重复。");
        }
        catch { next.Dispose();throw; }
        Note.Dispose();Note=next;_objects.Clear();_objects.AddRange(project.Item2);SelectedId=null;
        Path=System.IO.Path.GetFullPath(path);Frame=Math.Clamp(project.Item3,0,Note.Header.FrameCount-1);Reset("打开动画",true);
    }
    public void New(int frames=24)
    {
        var next=KwzFile.CreateBlank(frames,12);
        Note.Dispose();Note=next;Path=null;Frame=0;SelectedId=null;_objects.Clear();Reset("新建动画",false);
    }
    private State Snapshot(string label)
    {
        byte[] bytes=Note.ToArray();CanvasObject[] objects=_objects.ToArray();
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(bytes);hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(objects));
        return new(label,bytes,objects,SelectedId,Frame,hash.GetHashAndReset());
    }
    private void Reset(string label,bool saved)
    {
        Revision++;_history.Clear();_position=0;
        var state=Snapshot(label);_history.Add(state);_savedHash=saved?state.Hash:[];Refresh();
    }
    public void Seek(int frame) { Frame=Math.Clamp(frame,0,Note.Header.FrameCount-1);Refresh(); }
    public void SelectObject(Guid? id)
    {
        Guid? selection=_objects.FirstOrDefault(o=>o.Id==id && o.Frame==Frame)?.Id;
        if(SelectedId==selection)return;SelectedId=selection;Changed?.Invoke();
    }
    public byte[] RenderFrame(int frame,CanvasObject? preview=null,byte[]? basePixels=null)
    {
        byte[] pixels=basePixels==null?Note.FrameData.DecodeFrame(frame):(byte[])basePixels.Clone();
        foreach(var item in _objects.Where(o=>o.Frame==frame)) (item.Id==preview?.Id?preview:item).Draw(pixels);
        if(preview!=null && !_objects.Any(o=>o.Id==preview.Id))preview.Draw(pixels);
        return pixels;
    }
    private void Refresh()
    {
        Frame=Math.Clamp(Frame,0,Note.Header.FrameCount-1);
        if(SelectedObject==null)SelectedId=null;
        BasePixels=Note.FrameData.DecodeFrame(Frame);Pixels=RenderFrame(Frame,basePixels:BasePixels);Changed?.Invoke();
    }
    public void Edit(string label,Action<KwzFile> edit)=>EditState(label,()=>edit(Note));
    private void EditState(string label,Action edit)
    {
        State before=_history[_position];Guid? selection=SelectedId;int frame=Frame;
        try
        {
            edit();var after=Snapshot(label);
            if(!before.Hash.AsSpan().SequenceEqual(after.Hash))
            {
                if(CanRedo)_history.RemoveRange(_position+1,_history.Count-_position-1);
                _history.Add(after);_position++;Revision++;
                long size=_history.Sum(s=>(long)s.Bytes.Length+JsonSerializer.SerializeToUtf8Bytes(s.Objects).Length);
                while(_history.Count>2 && (_history.Count>100 || size>32*1024*1024))
                { size-=_history[0].Bytes.Length+JsonSerializer.SerializeToUtf8Bytes(_history[0].Objects).Length;_history.RemoveAt(0);_position--; }
            }
        }
        catch { LoadSnapshot(before);SelectedId=selection;Frame=frame;throw; }
        finally { Refresh(); }
    }
    private void LoadSnapshot(State state)
    {
        var next=new KwzFile(new MemoryStream(state.Bytes));Note.Dispose();Note=next;
        _objects.Clear();_objects.AddRange(state.Objects);SelectedId=state.Selection;Frame=state.Frame;
    }
    public void Undo() { if(CanUndo) { LoadSnapshot(_history[--_position]);Revision++;Refresh(); } }
    public void Redo() { if(CanRedo) { LoadSnapshot(_history[++_position]);Revision++;Refresh(); } }
    public void AddObject(CanvasObject item)=>EditState(item.Kind==ObjectKind.Text?"添加文字对象":"添加形状对象",()=>
    {
        item=item.MeasureText();item.Validate(Note.Header.FrameCount);
        if(_objects.Any(o=>o.Id==item.Id))throw new ArgumentException("对象编号重复。");
        _objects.Add(item);SelectedId=item.Id;
    });
    public void UpdateObject(CanvasObject item,string label="修改对象")=>EditState(label,()=>
    {
        item=item.MeasureText();item.Validate(Note.Header.FrameCount);
        int index=_objects.FindIndex(o=>o.Id==item.Id);if(index<0)throw new InvalidOperationException("对象不存在。");
        _objects[index]=item;SelectedId=item.Id;
    });
    public void DeleteSelectedObject()
    {
        if(SelectedObject is not { } item)return;
        EditState("删除对象",()=>{_objects.RemoveAll(o=>o.Id==item.Id);SelectedId=null;});
    }
    public void DuplicateSelectedObject() { if(SelectedObject is { } item)AddObject(item with { Id=Guid.NewGuid(),X=Math.Min(16384,item.X+8),Y=Math.Min(16384,item.Y+8) }); }
    public void FlattenSelectedObject()
    {
        if(SelectedObject is not { } item)return;
        EditState("对象合并到像素",()=>
        {
            var stack=_objects.TakeWhile(o=>o.Id!=item.Id).Append(item).Where(o=>o.Frame==Frame && o.Layer==item.Layer).ToArray();
            byte[] pixels=Note.FrameData.DecodeFrame(Frame);foreach(var o in stack)o.Draw(pixels);
            Note.FrameData.SetFramePixels(Frame,pixels);_objects.RemoveAll(o=>stack.Contains(o));SelectedId=null;
        });
    }
    public void RemapFrames(string label,IReadOnlyList<int> map,int selectedFrame)=>EditState(label,()=>
    {
        Note.FrameData.RemapFrames(map);
        var remapped=new List<CanvasObject>();var used=new HashSet<Guid>();
        for(int f=0;f<map.Count;f++)foreach(var item in _objects.Where(o=>o.Frame==map[f]))
            remapped.Add(item with { Frame=f,Id=used.Add(item.Id)?item.Id:Guid.NewGuid() });
        _objects.Clear();_objects.AddRange(remapped);Frame=selectedFrame;SelectedId=null;
    });
    public KwzFile CreateFlattenedNote()
    {
        var output=new KwzFile(new MemoryStream(Note.ToArray()));
        try { foreach(int frame in _objects.Select(o=>o.Frame).Distinct().Order())output.FrameData.SetFramePixels(frame,RenderFrame(frame));return output; }
        catch { output.Dispose();throw; }
    }
    public void SaveProject(string path)
    {
        if(!path.EndsWith(".kwzproj",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("工程请保存为 .kwzproj 文件。");
        var state=_history[_position];
        ObjectProject.Save(path,ObjectProject.Encode(state.Bytes,state.Objects,Frame));
        Path=System.IO.Path.GetFullPath(path);_savedHash=state.Hash;Revision++;Refresh();
    }
    public void ExportKwz(string path)
    {
        if(!path.EndsWith(".kwz",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("请导出为 .kwz 文件，工程仍保留为 .kwzproj。");
        using var output=CreateFlattenedNote();
        if(_objects.Count>0 || !output.VerifySignature())output.UpdateThumbnail(Math.Min(output.Header.ThumbnailFrameIndex,output.Header.FrameCount-1));
        output.SaveSigned(path);
    }
    public void Dispose()=>Note.Dispose();
}
