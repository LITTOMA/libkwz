using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KwzStudio;
using LibKwz;
using System.Security.Cryptography;
using System.Text.Json;

string root=Path.GetFullPath(args.Length>0?args[0]:"../../../..");
string sample=Path.GetFullPath(Path.Combine(root,"test/LibKwz.Test/sound.kwz"));
string scratch=Path.GetFullPath(Environment.GetEnvironmentVariable("KWZ_STUDIO_TEST_DIR")??Path.Combine(root,".validation/editor-smoke"));
Directory.CreateDirectory(scratch);
var checks=new List<string>();
void Check(bool passed,string name)
{
    if(!passed) throw new InvalidDataException(name);
    checks.Add(name);Console.WriteLine("PASS "+name);
}
string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
string originalHash=Hash(File.ReadAllBytes(sample));
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }).SetupWithoutStarting();
L.Initialize(Path.Combine(scratch,"settings.json"),"en-US");
L.SetLanguage("en",persist:false);
// Regression: launching from a ROM workspace must not discover or open any files.
string previousDirectory=Environment.CurrentDirectory;
string startupFixture=Path.Combine(scratch,"startup-workspace");
Directory.CreateDirectory(Path.Combine(startupFixture,"rom"));
Directory.CreateDirectory(Path.Combine(startupFixture,"tools","libkwz"));
Directory.CreateDirectory(Path.Combine(startupFixture,"unpack"));
string unsolicitedSample=Path.Combine(startupFixture,"unpack","unrequested.kwz");
File.Copy(sample,unsolicitedSample,overwrite:true);
MainWindow? blankWindow=null;
try
{
    Environment.CurrentDirectory=startupFixture;
    blankWindow=new MainWindow();blankWindow.Show();Dispatcher.UIThread.RunJobs();
    blankWindow.UpdateLayout();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
    Check(blankWindow.Document.Path==null && blankWindow.Document.Note.Header.FrameCount==24 && blankWindow.Document.Pixels.All(p=>p==0),"Default startup stays blank even inside a ROM workspace with KWZ files");
    Check(blankWindow.GetVisualDescendants().OfType<ListBox>().Count()==2 && !blankWindow.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("ROMFS")==true),"Independent startup shows objects and history without a ROM media panel");
    using var capture=blankWindow.CaptureRenderedFrame();capture!.Save(Path.Combine(scratch,"startup.png"));
}
finally
{
    Environment.CurrentDirectory=previousDirectory;
    if(blankWindow!=null) { blankWindow.Document.Open(sample);blankWindow.Close(); }
    File.Delete(unsolicitedSample);
}
var window=new MainWindow(sample);
void Pump()
{
    Dispatcher.UIThread.RunJobs();window.UpdateLayout();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
}
void PressKey(Key key,RawInputModifiers modifiers=RawInputModifiers.None)
{
    if(!Enum.TryParse<PhysicalKey>(key.ToString(),out var physical))Enum.TryParse("Arrow"+key,out physical);
    window.KeyPress(key,modifiers,physical,null);window.KeyRelease(key,modifiers,physical,null);Pump();
}
Point At(double x,double y)
{
    var rect=window.Canvas.ImageRect;
    return window.Canvas.TranslatePoint(new Point(rect.X+x*rect.Width/320,rect.Y+y*rect.Height/240),window)!.Value;
}
void Drag(double x,double y,double toX,double toY)
{
    window.MouseDown(At(x,y),MouseButton.Left);window.MouseMove(At(toX,toY),RawInputModifiers.LeftMouseButton);window.MouseUp(At(toX,toY),MouseButton.Left);Pump();
}
void Click(Point point)
{
    window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);Pump();
}
ColorSwatchPicker Picker(string name)=>window.GetVisualDescendants().OfType<ColorSwatchPicker>().Single(p=>p.Name==name);
Avalonia.Controls.Primitives.ToggleButton Swatch(string name,int index)=>Picker(name).GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single(b=>b.Name=="Swatch"+index);
void Pick(string name,int index)
{
    var button=Swatch(name,index);button.BringIntoView();Pump();
    Click(button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),window)!.Value);
}
try
{
    window.Show();Pump();
    Check(window.Document.Path==sample && !window.Document.IsDirty,"Open KWZ in themed editor");
    Check(window.Canvas.Bounds.Width>320 && window.Canvas.Bounds.Height>240,"Preview has usable layout");
    var doc=window.Document;
    int count=doc.Note.Header.FrameCount;
    string first=Hash(doc.Pixels),next=Hash(doc.Note.FrameData.DecodeFrame(1));
    Point middle=window.Canvas.TranslatePoint(new Point(window.Canvas.Bounds.Width/2,window.Canvas.Bounds.Height/2),window)!.Value;
    window.Canvas.Ink=(byte)(doc.Pixels[120*320+160]==1?2:1);
    Click(middle);
    Check(doc.IsDirty && doc.CanUndo && Hash(doc.Pixels)!=first,"Mouse drawing changes current frame");
    Check(Hash(doc.Note.FrameData.DecodeFrame(1))==next,"Drawing preserves following delta frame");
    PressKey(Key.Z,RawInputModifiers.Control);
    Check(!doc.IsDirty && Hash(doc.Pixels)==first,"Keyboard undo restores original bytes");
    PressKey(Key.Y,RawInputModifiers.Control);
    Check(doc.IsDirty && Hash(doc.Pixels)!=first,"Keyboard redo restores drawing");
    PressKey(Key.D,RawInputModifiers.Control);
    Check(doc.Note.Header.FrameCount==count+1 && doc.Frame==1,"Keyboard duplicate inserts selected frame");
    PressKey(Key.Delete);
    Check(doc.Note.Header.FrameCount==count,"Keyboard delete removes frame");
    Point track=window.Timeline.TranslatePoint(new Point(window.Timeline.FrameWidth*2+5,85),window)!.Value;
    Click(track);
    Check(doc.Frame==2 && window.Canvas.Layer==1,"Timeline click selects frame and layer");
    doc.New(2);window.Canvas.Layer=0;window.Canvas.Ink=1;
    doc.Edit("Palette fixture",n=>
    {
        var pixels=n.FrameData.DecodeFrame(0);pixels[10*320+10]=1;pixels[10*320+11]=2;n.FrameData.SetFramePixels(0,pixels);
        n.Meta.Entries[0].LayerAFirstColorIndex=1;n.Meta.Entries[0].LayerASecondColorIndex=2;
    });Pump();
    uint originalFlags=doc.Note.Meta.Entries[0].Flags,followingFlags=doc.Note.Meta.Entries[1].Flags;
    string indexedPixels=Hash(doc.Pixels);
    Pick("FirstInkPalette",5);
    var colored=PreviewCanvas.Composite(doc,doc.Pixels);int coloredOffset=(10*320+10)*4;
    Check(doc.Note.Meta.Entries[0].LayerAFirstColorIndex==5 && colored[coloredOffset]==206 && colored[coloredOffset+1]==56 && colored[coloredOffset+2]==0,"Clicking a palette swatch changes the actual rendered ink color");
    Check(Hash(doc.Pixels)==indexedPixels && (doc.Note.Meta.Entries[0].Flags&~0xf00u)==(originalFlags&~0xf00u) && doc.Note.Meta.Entries[1].Flags==followingFlags,"Changing a color preserves indexed pixels, other inks, layers and frames");
    Check(Avalonia.Automation.AutomationProperties.GetName(Swatch("DrawingInk",0))?.Contains("#0038CE")==true,"Drawing ink buttons show the current frame's actual palette colors");
    int beforeRepeat=doc.Revision;Pick("FirstInkPalette",5);
    Check(Swatch("FirstInkPalette",5).IsChecked==true && doc.Revision==beforeRepeat,"Clicking the selected swatch keeps it selected without adding an undo step");
    window.Canvas.Focus();PressKey(Key.Z,RawInputModifiers.Control);
    Check(Picker("FirstInkPalette").SelectedIndex==1 && Swatch("FirstInkPalette",1).IsChecked==true,"Undo restores both palette data and the selected color swatch");
    PressKey(Key.Y,RawInputModifiers.Control);
    Swatch("FirstInkPalette",5).Focus();PressKey(Key.Left);
    Check(doc.Frame==0 && doc.Note.Meta.Entries[0].LayerAFirstColorIndex==4,"Keyboard arrows select palette colors without seeking the timeline");
    doc.Undo();Pump();
    int beforeBrush=doc.Revision;Pick("DrawingInk",1);
    Check(window.Canvas.Ink==2 && doc.Revision==beforeBrush,"Choosing a brush ink without an object does not modify the document");
    doc.AddObject(new CanvasObject { Kind=ObjectKind.Rectangle,Frame=0,Layer=0,X=25,Y=25,Width=20,Height=20,Ink=1 });Pump();
    int revisionBeforeInk=doc.Revision;Pick("DrawingInk",1);
    Check(window.Canvas.Ink==2 && doc.SelectedObject?.Ink==2 && doc.Revision>revisionBeforeInk,"Choosing the second ink recolors a selected object without changing its layer palette");
    Pick("SecondInkPalette",3);Pick("PaperPalette",6);
    Check(doc.Note.Meta.Entries[0].LayerASecondColorIndex==3 && doc.Note.Meta.Entries[0].PaperColorIndex==6,"Paper transparency and each layer ink are independently selectable");
    string coloredProject=Path.Combine(scratch,"colors.kwzproj"),coloredExport=Path.Combine(scratch,"colors.kwz");
    uint savedPalette=doc.Note.Meta.Entries[0].Flags;string coloredComposite=Hash(PreviewCanvas.Composite(doc,doc.Pixels));
    doc.SaveProject(coloredProject);doc.Open(coloredProject);Pump();doc.ExportKwz(coloredExport);
    using(var exported=new EditorDocument())
    {
        exported.Open(coloredExport);
        Check(doc.Note.Meta.Entries[0].Flags==savedPalette && Hash(PreviewCanvas.Composite(exported,exported.Pixels))==coloredComposite && exported.Note.VerifySignature(),"Project reopen and signed KWZ export preserve selected colors and transparent paper");
    }
    doc.Seek(1);Pump();
    Check(Picker("FirstInkPalette").SelectedIndex==doc.Note.Meta.Entries[1].LayerAFirstColorIndex,"Seeking updates the palette to the new frame");
    doc.Seek(0);window.Canvas.Layer=1;doc.Seek(0);Pump();
    Check(Picker("FirstInkPalette").SelectedIndex==doc.Note.Meta.Entries[0].LayerBFirstColorIndex,"Changing layers refreshes the palette without recoloring other layers");
    doc.SelectObject(doc.Objects[0].Id);Pick("DrawingInk",2);
    Check(doc.SelectedObject?.Ink==0 && window.Canvas.Ink==0,"Transparent object ink clears pixels instead of introducing a seventh opaque color");
    doc.Undo();Pump();
    using(var screenshot=window.CaptureRenderedFrame())screenshot!.Save(Path.Combine(root,"editor/colors-preview.png"));
    doc.New(4);Pump();
    window.Canvas.Layer=0;window.Canvas.Ink=1;window.Canvas.StampText="中文汉化";window.Canvas.TextSize=24;
    window.Canvas.Focus();PressKey(Key.T);Click(middle);
    Check(doc.Pixels.Count(p=>p!=0)>100,"Chinese text tool renders glyphs into layer pixels");
    Check(doc.Objects.Count==1 && doc.BasePixels.All(p=>p==0),"Text is an editable object, not burned into the base layer");
    Check(doc.Objects[0].FontFamily==StudioFonts.DefaultFamily,"New text objects default to bundled Fusion Pixel");
    using(var face=StudioFonts.CreateTypeface(StudioFonts.DefaultFamily))
        Check(face.FamilyName==StudioFonts.DefaultFamily && face.GetGlyphs("动画记事本 ABC").All(g=>g!=0),"Bundled Fusion Pixel resolves directly and contains Chinese and Latin glyphs");
    using(var legacy=new EditorDocument())
    {
        legacy.New();
        legacy.AddObject(new CanvasObject { Text="旧工程 ABC",FontFamily="Microsoft YaHei" }.MeasureText());
        string legacyPixels=Hash(legacy.Pixels),legacyPath=Path.Combine(scratch,"legacy-font.kwzproj");
        legacy.SaveProject(legacyPath);legacy.Open(legacyPath);
        Check(legacy.Objects.Single().FontFamily=="Microsoft YaHei" && Hash(legacy.Pixels)==legacyPixels,"Reopening an existing project preserves its saved font and pixels");
    }
    var text=doc.Objects[0];
    Drag(text.X+8,text.Y+8,text.X-112,text.Y-77);
    var moved=doc.Objects.Single();
    Check(moved.X==text.X-120 && moved.Y==text.Y-85,$"Mouse dragging moves the selected text object ({text.X},{text.Y} -> {moved.X},{moved.Y})");
    Check(doc.Pixels.AsSpan(120*320,320*60).ToArray().All(p=>p==0),"Moving text restores pixels at its old position");
    PressKey(Key.Z,RawInputModifiers.Control);Check(doc.Objects.Single().X==text.X,"Text movement is undoable");
    PressKey(Key.Y,RawInputModifiers.Control);Check(doc.Objects.Single().X==moved.X,"Text movement is redoable");
    text=doc.Objects.Single();
    Drag(text.X+text.Width,text.Y+text.Height,text.X+text.Width+24,text.Y+text.Height+16);
    Check(doc.Objects.Single().FontSize>text.FontSize,"Text resize handle changes font size");
    var textBox=window.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="ObjectText");
    textBox.Text="自由移动";Pump();
    Check(doc.Objects.Single().Text=="自由移动","Inspector edits existing text content");
    text=doc.Objects.Single();window.Canvas.Focus();
    window.MouseDown(At(text.X+5,text.Y+5),MouseButton.Left);window.MouseMove(At(text.X+35,text.Y+35),RawInputModifiers.LeftMouseButton);
    PressKey(Key.Escape);window.MouseUp(At(text.X+35,text.Y+35),MouseButton.Left);Pump();
    Check(doc.Objects.Single()==text,"Escape cancels a drag without changing the object");
    PressKey(Key.R);Drag(25,150,95,200);
    var shape=doc.Objects.Single(o=>o.Kind==ObjectKind.Rectangle);
    Check(shape.Width==71 && shape.Height==51,"Rectangle drag creates a separate shape object");
    Drag(35,160,85,110);
    shape=doc.Objects.Single(o=>o.Kind==ObjectKind.Rectangle);
    Check(shape.X==75 && shape.Y==100 && doc.BasePixels.All(p=>p==0),"Shape movement preserves the base image");
    Drag(shape.X+shape.Width,shape.Y+shape.Height,shape.X+shape.Width+20,shape.Y+shape.Height+10);
    shape=doc.Objects.Single(o=>o.Kind==ObjectKind.Rectangle);
    Check(shape.Width==91 && shape.Height==61,"Shape resize handle adjusts width and height");
    int frameBeforeNudge=doc.Frame;
    PressKey(Key.Right,RawInputModifiers.Shift);
    Check(doc.SelectedObject?.X==shape.X+10 && doc.Frame==frameBeforeNudge,"Shift-arrow nudges an object without changing frames");
    shape=doc.SelectedObject!;
    doc.UpdateObject(shape with { Filled=false,StrokeWidth=2 });Pump();
    Check(doc.Pixels[(shape.Y+shape.Height/2)*320+shape.X+shape.Width/2]==0,"Outline shape keeps its center transparent");
    PressKey(Key.Delete);Check(doc.Objects.Count==1 && doc.Note.Header.FrameCount==4,"Delete removes selected object instead of deleting a frame");
    PressKey(Key.Z,RawInputModifiers.Control);Check(doc.Objects.Count==2,"Undo restores deleted shape");
    shape=doc.Objects.Single(o=>o.Kind==ObjectKind.Rectangle);doc.SelectObject(shape.Id);
    doc.UpdateObject(shape with { Filled=true });
    int covered=(shape.Y+10)*320+shape.X+10;
    doc.Edit("Base pixel under object",n=>{var pixels=n.FrameData.DecodeFrame(0);pixels[covered]=2;n.FrameData.SetFramePixels(0,pixels);});
    Check(doc.Pixels[covered]==shape.Ink,"Object overlays existing base pixels");
    doc.UpdateObject(doc.SelectedObject! with { X=220,Y=170 });
    Check(doc.Pixels[covered]==2,"Moving a shape reveals the exact underlying pixel");
    doc.SelectObject(null);window.Canvas.Focus();PressKey(Key.O);Drag(210,95,280,145);
    Check(doc.Objects.Any(o=>o.Kind==ObjectKind.Ellipse),"Ellipse is also an editable shape object");
    string editedHash=Hash(doc.Pixels),baseHash=Hash(doc.BasePixels);
    string project=Path.Combine(scratch,"editable.kwzproj"),saved=Path.Combine(scratch,"signed-edit.kwz");
    doc.SaveProject(project);
    Check(doc.IsProject && !doc.IsDirty,"Saving a project preserves editable state and clears dirty flag");
    var savedObjects=doc.Objects.ToArray();string projectHash=Hash(File.ReadAllBytes(project));
    doc.ExportKwz(saved);
    using(var reread=new KwzFile(saved,true))
    {
        Check(reread.VerifySignature(),"KWZ export signs with embedded resource");
        Check(Hash(reread.FrameData.DecodeFrame(doc.Frame))==editedHash,"KWZ export includes text and shapes with identical pixels");
        Check(reread.FrameData.DecodeFrame(1).All(p=>p==0),"Object export preserves following delta frame");
    }
    Check(doc.Objects.SequenceEqual(savedObjects) && Hash(doc.BasePixels)==baseHash && Hash(File.ReadAllBytes(project))==projectHash,"Export leaves the project and editable base unchanged");
    doc.Undo();Check(doc.IsDirty,"Undo after project save marks document modified");
    doc.Redo();Check(!doc.IsDirty,"Redo to saved project clears modified state");
    string isolated=Path.Combine(scratch,"isolated.kwzproj");File.Copy(project,isolated,true);File.Delete(project);
    doc.New();doc.Open(isolated);Pump();
    Check(doc.Objects.SequenceEqual(savedObjects) && Hash(doc.Pixels)==editedHash && Hash(doc.BasePixels)==baseHash,"Self-contained project reopens with objects and hidden pixels intact");
    doc.SelectObject(doc.Objects[0].Id);window.Canvas.Focus();PressKey(Key.V);
    int oldX=doc.SelectedObject!.X;PressKey(Key.Right);
    Check(doc.SelectedObject!.X==oldX+1 && doc.IsDirty,"Reopened project objects remain movable");
    doc.ExportKwz(saved);Check(doc.IsDirty,"Export does not falsely mark unsaved project edits as saved");
    string failedOriginal=Hash(File.ReadAllBytes(isolated));
    using(var locked=new FileStream(isolated,FileMode.Open,FileAccess.Read,FileShare.Read))
    {
        bool failed=false;try{doc.SaveProject(isolated);}catch(Exception error) when(error is IOException or UnauthorizedAccessException){failed=true;}
        Check(failed && doc.IsDirty,"Failed project save retains unsaved state");
    }
    Check(Hash(File.ReadAllBytes(isolated))==failedOriginal && !Directory.EnumerateFiles(scratch,"*.tmp").Any(),"Failed project save preserves destination and removes temporary file");
    bool blockedExport=false;try{doc.ExportKwz(isolated);}catch(ArgumentException){blockedExport=true;}
    Check(blockedExport && Hash(File.ReadAllBytes(isolated))==failedOriginal,"KWZ export cannot overwrite a project file");
    string bad=Path.Combine(scratch,"future-version.kwzproj");
    using(var zip=System.IO.Compression.ZipFile.Open(bad,System.IO.Compression.ZipArchiveMode.Create))
    {
        using var entry=zip.CreateEntry("project.json").Open();
        JsonSerializer.Serialize(entry,new { Format="KWZStudioProject",Version=99,CurrentFrame=0,Objects=Array.Empty<CanvasObject>() });
    }
    string validState=Hash(doc.Pixels);var validObjects=doc.Objects.ToArray();bool rejected=false;
    try{doc.Open(bad);}catch(InvalidDataException){rejected=true;}
    Check(rejected && Hash(doc.Pixels)==validState && doc.Objects.SequenceEqual(validObjects),"Unknown project version is rejected without losing current edits");
    var originalObjects=doc.Objects.ToArray();
    doc.RemapFrames("Duplicate frame with objects",[0,0,1,2,3],1);
    Check(doc.Objects.Count==originalObjects.Length*2 && doc.Objects.Select(o=>o.Id).Distinct().Count()==doc.Objects.Count,"Duplicated frames receive independent object identities");
    Check(Hash(doc.RenderFrame(0))==Hash(doc.RenderFrame(1)),"Frame duplication carries text and shapes with the image");
    doc.Undo();Check(doc.Objects.SequenceEqual(originalObjects),"Undo restores frame and object mappings together");
    doc.SelectObject(doc.Objects.First(o=>o.Kind==ObjectKind.Text).Id);
    string beforeFlatten=Hash(doc.Pixels),baseBeforeFlatten=Hash(doc.BasePixels);int objectCount=doc.Objects.Count;
    doc.FlattenSelectedObject();
    Check(Hash(doc.Pixels)==beforeFlatten && doc.Objects.Count==objectCount-1 && Hash(doc.BasePixels)!=baseBeforeFlatten,"Flattening an object preserves the visible image");
    doc.Undo();Check(doc.Objects.Count==objectCount && Hash(doc.BasePixels)==baseBeforeFlatten,"Undo flattening restores editable object and underlying base");
    doc.SelectObject(doc.Objects.First(o=>o.Kind==ObjectKind.Text).Id);window.Canvas.Tool=DrawingTool.Select;Pump();
    using(var screenshot=window.CaptureRenderedFrame())screenshot!.Save(Path.Combine(root,"editor/objects-preview.png"));
    byte[] before=doc.Note.ToArray();
    try { doc.Edit("Failed edit",n=>{ n.Header.CurrentAuthorName="bad";throw new InvalidDataException("test rollback"); }); }
    catch(InvalidDataException) { }
    Check(before.AsSpan().SequenceEqual(doc.Note.ToArray()),"Failed edit rolls back without changing history");
    var pcm=new byte[KwzSound.SampleRate*2];
    for(int i=0;i<pcm.Length/2;i++)System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*2),(short)(Math.Sin(i*.15)*12000));
    string wav=Path.Combine(scratch,"audio.wav");File.WriteAllBytes(wav,AudioPreview.Wave(pcm));
    byte[] decoded=AudioPreview.ReadWav(wav);
    Check(decoded.Length==pcm.Length && decoded.Any(b=>b!=0),"WAV export and import preserve duration");
    doc.Edit("Import audio",n=>n.Sound!.SetAudioTrack(0,decoded));
    Check(AudioPreview.Mix(doc.Note).Any(b=>b!=0),"Imported audio mixes for playback");
    if(args.Length>1)
    {
        using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"verification-report.json")));
        string corpusRoot=Path.GetFullPath(Path.Combine(root,"../.."));
        int audioFiles=0;
        foreach(var entry in report.RootElement.GetProperty("reports").EnumerateArray())
        {
            string path=Path.Combine(corpusRoot,entry.GetProperty("path").GetString()!);
            using var note=new KwzFile(path);
            var start=new System.Diagnostics.ProcessStartInfo("node") { RedirectStandardInput=true,RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true };
            start.ArgumentList.Add(Path.Combine(root,"test/KwzEditor.Smoke/audio-oracle.cjs"));start.ArgumentList.Add(Path.GetFullPath(args[1]));
            using var process=System.Diagnostics.Process.Start(start)!;
            var output=process.StandardOutput.ReadToEndAsync();
            process.StandardInput.BaseStream.Write(File.ReadAllBytes(path));process.StandardInput.Close();
            if(!process.WaitForExit(30000)) { process.Kill();throw new TimeoutException("Audio reference timed out."); }
            string expected=output.GetAwaiter().GetResult();
            if(process.ExitCode!=0 || Hash(AudioPreview.Mix(note))!=expected) throw new InvalidDataException("Mixed audio differs: "+path);
            audioFiles++;
        }
        Check(audioFiles==34,"Mixed playback audio matches independent decoder for all 34 samples");
    }
    doc.New(2);
    doc.AddObject(new CanvasObject{Kind=ObjectKind.Text,Text="SD",X=20,Y=30,FontSize=24});
    byte[] sdProjectBefore=doc.Note.ToArray();
    var sdObjectsBefore=doc.Objects.ToArray();
    string[] sdHistoryBefore=doc.History;
    string sdFolder=Path.Combine(scratch,"sd","private","Nintendo 3DS","app","JKZJ","000");
    Directory.CreateDirectory(sdFolder);
    using(var flattened=doc.CreateFlattenedNote())
    {
        var imported=KwzSdCard.Import(sdFolder,flattened);
        using var readback=new KwzFile(imported.AnimationPath,verifyChecksums:true);
        Check(readback.VerifySignature() && readback.FrameData.DecodeFrame(0).AsSpan().SequenceEqual(doc.RenderFrame(0)),"SD import contains flattened text objects and a valid game signature");
        Check(KwzSdList.Read(File.ReadAllBytes(imported.ListPath)).Filenames.Single()==Path.GetFileName(imported.AnimationPath),"SD import registers the exported animation in the game catalog");
        File.Delete(imported.AnimationPath);File.Delete(imported.ListPath);
    }
    Check(doc.Note.ToArray().AsSpan().SequenceEqual(sdProjectBefore) && doc.Objects.SequenceEqual(sdObjectsBefore) && doc.History.SequenceEqual(sdHistoryBefore),"SD import preserves the editable document and undo history");
    Check(window.GetVisualDescendants().OfType<Button>().Any(b=>b.Name=="ImportToSdCard"),"SD import is available in the editor toolbar");
    doc.Open(sample);doc.Seek(Math.Min(12,count-1));Pump();
    using(var screenshot=window.CaptureRenderedFrame())
    {
        Check(screenshot!=null && screenshot.PixelSize.Width>=1400,"Classic theme renders through real Skia backend");
        screenshot!.Save(Path.Combine(root,"editor/preview.png"));
    }
    window.Width=1080;window.Height=760;Pump();
    using(var screenshot=window.CaptureRenderedFrame()) screenshot!.Save(Path.Combine(scratch,"compact.png"));
    Check(window.Canvas.Bounds.Width>=320 && window.Canvas.Bounds.Height>=180,"Minimum window size keeps preview usable");
    SdPickerChecks.Run(window,root,scratch,Check,Pump);
    LocalizationChecks.Run(window,root,scratch,Check,Pump);
    Check(Hash(File.ReadAllBytes(sample))==originalHash,"Original sample remains untouched");
    File.WriteAllText(Path.Combine(root,"editor/smoke-report.json"),JsonSerializer.Serialize(new { passed=checks.Count,checks,originalSha256=originalHash,theme="Classic.Avalonia.Theme 11.3.0.3",renderer="Avalonia.Headless + Skia" },new JsonSerializerOptions { WriteIndented=true }));
}
catch
{
    using(var screenshot=window.CaptureRenderedFrame())screenshot?.Save(Path.Combine(scratch,"failure.png"));
    throw;
}
finally
{
    window.Document.Open(sample);window.Close();
    foreach(string file in Directory.EnumerateFiles(scratch).Where(p=>Path.GetExtension(p) is ".kwz" or ".wav" or ".kwzproj"))File.Delete(file);
}
