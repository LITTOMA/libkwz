using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Classic.Avalonia.Theme;
using LibKwz;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using AColor=Avalonia.Media.Color;

namespace KwzStudio;

public sealed partial class MainWindow : ClassicWindow
{
    public EditorDocument Document { get; }=new();
    public PreviewCanvas Canvas { get; }
    public TimelineControl Timeline { get; }
    private readonly List<Action> _localized = [];
    private readonly Dictionary<string,MenuItem> _languageItems = [];
    private readonly ComboBox _zoom = new();
    private readonly TextBlock _status=new(),_details=new(),_time=new(),_signature=new(),_fileLabel=new();
    private readonly ListBox _history=new(),_objectList=new();
    private readonly StackPanel _objectTransform=new(){Spacing=4},_textOptions=new(){Spacing=4},_shapeOptions=new(){Spacing=4};
    private readonly TextBlock _selectionTitle=new(){Text="No object selected",FontWeight=FontWeight.Bold};
    private readonly NumericUpDown _objectX=Number(0,-16384,16384),_objectY=Number(0,-16384,16384),_objectWidth=Number(80,1,16384),_objectHeight=Number(40,1,16384),_shapeStroke=Number(1,1,64);
    private readonly CheckBox _shapeFilled=new(){Content="Filled shape",IsChecked=true};
    private readonly ComboBox _layer=new(){ItemsSource=new[]{"Layer A","Layer B","Layer C"},SelectedIndex=0};
    private readonly ColorSwatchPicker _first=new([0,1,2,3,4,5]){Name="FirstInkPalette"},_second=new([0,1,2,3,4,5]){Name="SecondInkPalette"},_paper=new([0,1,2,3,4,5,6]){Name="PaperPalette"};
    private readonly ComboBox _fps=new(){ItemsSource=new[]{"0.2","0.5","1","2","4","6","8","12","20","24","30"},SelectedIndex=7};
    private readonly ColorSwatchPicker _ink=new([1,2,6],["Ink 1","Ink 2","Transparent"]){Name="DrawingInk",SelectedIndex=0};
    private readonly NumericUpDown _frame=Number(1,1,65535),_depth=Number(0,0,255),_brush=Number(3,1,31),_textSize=Number(24,6,256);
    private readonly TextBox _text=new(){Name="ObjectText",Text=L.T("Text"),MaxLength=10000,AcceptsReturn=true,Height=62,Watermark="Enter text, then click the canvas to place it"};
    private readonly TextBox _author=new(){Name="CurrentAuthor",MaxLength=11};
    private readonly CheckBox[] _visible=[new(){Content="A"},new(){Content="B"},new(){Content="C"}];
    private readonly CheckBox[] _effects=[new(){Content="SE 1"},new(){Content="SE 2"},new(){Content="SE 3"},new(){Content="SE 4"}];
    private readonly CheckBox _loop=new(){Content="Loop"},_onion=new(){Content="Onion skin"},_grid=new(){Content="8 px grid"};
    private readonly Button _play=new(){Content="▶ Play",MinWidth=75},_undo=new(){Content="↶ Undo"},_redo=new(){Content="↷ Redo"};
    private readonly Dictionary<DrawingTool,ToggleButton> _tools=[];
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(10)};
    private readonly Stopwatch _clock=new();
    private readonly AudioPreview _audio=new();
    private int _startFrame,_revision=-1;
    private bool _sync,_allowClose,_closePending;
    public MainWindow(string? initialPath=null)
    {
        Title="KWZ Studio";Width=1440;Height=960;MinWidth=1080;MinHeight=760;
        FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI, Nirmala UI, Leelawadee UI");FontSize=12;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Canvas=new PreviewCanvas(Document);Timeline=new TimelineControl(Document);
        Content=BuildWorkspace();
        BindContent(_shapeFilled,"Filled shape");BindContent(_loop,"Loop");BindContent(_onion,"Onion skin");BindContent(_grid,"8 px grid");
        BindContent(_undo,"↶ Undo");BindContent(_redo,"↷ Redo");
        Localize(_text,"Enter text, then click the canvas to place it",(c,t)=>c.Watermark=t);
        Canvas.FlowDirection=Timeline.FlowDirection=FlowDirection.LeftToRight;
        L.Changed+=ApplyLanguage;ApplyLanguage();
        Document.Changed+=Refresh;
        Canvas.Status+=message=>_status.Text=message;
        Canvas.PointerPressed+=(_,_)=>Stop();
        Timeline.ScrubStarted+=()=>{Stop();Document.SelectObject(null);};
        Timeline.LayerSelected+=index=>_layer.SelectedIndex=index;
        _timer.Tick+=(_,_)=>Tick();
        WireProperties(); Refresh();
        KeyDown+=OnKey;
        DragDrop.SetAllowDrop(this,true);
        AddHandler(DragDrop.DropEvent,async (_,e)=>
        {
            var path=e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
            if(path!=null && (path.EndsWith(".kwz",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".kwzproj",StringComparison.OrdinalIgnoreCase))) await Open(path);
        });
        Closing+=async (_,e)=>
        {
            if(_sdImporting) { e.Cancel=true;return; }
            if(_allowClose || !Document.IsDirty) return;
            e.Cancel=true;
            if(_closePending) return;
            _closePending=true;Stop();
            if(await ConfirmDiscard()) { _allowClose=true;Close(); }
            _closePending=false;
        };
        Closed+=(_,_)=>{ L.Changed-=ApplyLanguage;_timer.Stop();_audio.Dispose();Canvas.Dispose();Document.Dispose(); };
        Opened+=async (_,_)=>
        {
            try
            {
                if(initialPath!=null) Document.Open(initialPath);
                else Document.New();
                Refresh(); _status.Text=L.T("Ready · Drop a KWZ to edit. Space plays; arrow keys step through frames.");
            }
            catch(Exception error) { await Error(error); }
        };
    }
    private static NumericUpDown Number(decimal value,decimal min,decimal max)=>new(){Value=value,Minimum=min,Maximum=max,Increment=1,FormatString="0",MinWidth=64};
    private T Localize<T>(T control,string key,Action<T,string> assign)
    {
        void Apply()=>assign(control,L.T(key));
        _localized.Add(Apply);Apply();return control;
    }
    private T BindContent<T>(T control,string key) where T:ContentControl => Localize(control,key,(c,t)=>c.Content=t);
    private TextBlock Text(string key)=>Localize(new TextBlock(),key,(c,t)=>c.Text=t);
    private TextBlock Label(string key) { var text=Text(key);text.VerticalAlignment=VerticalAlignment.Center;return text; }
    private void ApplyLanguage()
    {
        _sync=true;
        try
        {
            FlowDirection=L.IsRightToLeft?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
            foreach(var update in _localized)update();
            int layer=Math.Max(0,_layer.SelectedIndex),zoom=Math.Max(0,_zoom.SelectedIndex);
            _layer.ItemsSource=new[]{L.T("Layer A"),L.T("Layer B"),L.T("Layer C")};_layer.SelectedIndex=layer;
            _zoom.ItemsSource=new[]{L.T("Fit to window"),"100%","200%","300%","400%"};_zoom.SelectedIndex=zoom;
            int speed=Math.Max(0,_fps.SelectedIndex);
            _fps.ItemsSource=new double[]{.2,.5,1,2,4,6,8,12,20,24,30}.Select(f=>f.ToString("0.#",L.Culture)).ToArray();_fps.SelectedIndex=speed;
            foreach(var picker in new[]{_first,_second,_paper,_ink})picker.RefreshLanguage();
            foreach(var pair in _languageItems)pair.Value.Icon=pair.Key==L.Preference?new TextBlock{Text="✓"}:null;
            _status.Text=L.T("Ready · Drop a KWZ to edit. Space plays; arrow keys step through frames.");
            _history.ItemsSource=Document.History;
        }
        finally{_sync=false;}
        Refresh();
    }
    private MenuItem LanguageMenu()
    {
        var choices=new List<Control>();
        var system=Item("Follow system language",()=>L.SetLanguage("system"));
        system.Name="Language_system";_languageItems.Add("system",system);choices.Add(system);choices.Add(new Separator());
        foreach(var language in L.Languages)
        {
            var item=new MenuItem{Header=language.NativeName,Name="Language_"+language.Code,FlowDirection=FlowDirection.LeftToRight};
            item.Click+=(_,_)=>Run(()=>L.SetLanguage(language.Code));_languageItems.Add(language.Code,item);choices.Add(item);
        }
        var menu=Menu("Language",choices.ToArray());menu.Name="LanguageMenu";return menu;
    }
    private static WrapPanel Wrap(params Control[] children)
    {
        var result=new WrapPanel();
        foreach(var child in children){child.Margin=new Thickness(3,2);result.Children.Add(child);}return result;
    }
    private static StackPanel Row(params Control[] children)
    {
        var result=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center };
        foreach(var child in children) result.Children.Add(child);return result;
    }
    private Control Field(string title,Control control,int labelWidth=74)
    {
        var grid=new Grid { ColumnDefinitions=new ColumnDefinitions("Auto,*"),Margin=new Thickness(0,3) };
        var label=Label(title);label.MinWidth=labelWidth;label.MaxWidth=120;label.TextWrapping=TextWrapping.Wrap;label.Margin=new Thickness(0,0,6,0);grid.Children.Add(label);Grid.SetColumn(control,1);grid.Children.Add(control);return grid;
    }
    private Control Section(string title,Control content)
    {
        var panel=new DockPanel();
        var heading=new Border { Background=new SolidColorBrush(AColor.Parse("#D4D0C8")),Padding=new Thickness(7,5),BorderBrush=Brushes.Gray,BorderThickness=new Thickness(0,0,0,1),Child=Localize(new TextBlock { FontWeight=FontWeight.Bold,TextWrapping=TextWrapping.Wrap },title,(c,t)=>c.Text=t) };
        DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);panel.Children.Add(content);
        return new Border { BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),Child=panel };
    }
    private Button Button(string title,Action action,string? tip=null)
    {
        var b=BindContent(new Button { Padding=new Thickness(9,4),MinHeight=27 },title);
        b.Click+=(_,_)=>Run(action);if(tip!=null)Localize(b,tip,(c,t)=>ToolTip.SetTip(c,t));return b;
    }
    private Button AsyncButton(string title,Func<Task> action)
    { var b=Button(title,()=>{}); b.Click+=async (_,_)=>await Guard(action);return b; }
    private MenuItem Menu(string title,params Control[] children)=>Localize(new MenuItem{ItemsSource=children},title,(c,t)=>c.Header=t);
    private MenuItem Item(string title,Action action,string? shortcut=null)
    {
        var item=Localize(new MenuItem(),title,(c,t)=>c.Header=t);if(shortcut!=null)item.InputGesture=KeyGesture.Parse(shortcut);
        item.Click+=(_,_)=>Run(action);return item;
    }
    private MenuItem AsyncItem(string title,Func<Task> action,string? shortcut=null)
    {
        var item=Localize(new MenuItem(),title,(c,t)=>c.Header=t);if(shortcut!=null)item.InputGesture=KeyGesture.Parse(shortcut);
        item.Click+=async (_,_)=>await Guard(action);return item;
    }
    private Control BuildWorkspace()
    {
        var root=new Grid { RowDefinitions=new RowDefinitions("Auto,Auto,*,5,Auto,24"),Margin=new Thickness(3) };
        var menu=new Avalonia.Controls.Menu { ItemsSource=new[]{
            Menu("_File",AsyncItem("New animation",New,"Ctrl+N"),AsyncItem("Open…",()=>Open(),"Ctrl+O"),AsyncItem("Save project",()=>Save(false),"Ctrl+S"),AsyncItem("Save project as…",()=>Save(true),"Ctrl+Shift+S"),AsyncItem("Export KWZ…",ExportKwz,"Ctrl+Shift+E"),AsyncItem("Import to SD card",ImportToSdCard),AsyncItem("Choose SD card…",async()=>{await ChooseSdCard();}),new Separator(),Item("Exit",Close)),
            Menu("_Edit",Item("Undo",()=>{Stop();Document.Undo();},"Ctrl+Z"),Item("Redo",()=>{Stop();Document.Redo();},"Ctrl+Y"),new Separator(),Item("Insert blank frame",Insert),Item("Duplicate current frame",Duplicate,"Ctrl+D"),Item("Delete current frame",Delete,"Delete"),Item("Move frame earlier",()=>Move(-1)),Item("Move frame later",()=>Move(1))),
            Menu("_Media",AsyncItem("Import image to current layer…",ImportImage),AsyncItem("Export current frame as PNG…",ExportImage),new Separator(),AsyncItem("Replace background music from WAV…",()=>ImportAudio(0)),AsyncItem("Export background music as WAV…",()=>ExportAudio(0)),AsyncItem("Replace sound effect 1…",()=>ImportAudio(1)),AsyncItem("Replace sound effect 2…",()=>ImportAudio(2)),AsyncItem("Replace sound effect 3…",()=>ImportAudio(3)),AsyncItem("Replace sound effect 4…",()=>ImportAudio(4))),
            Menu("_Playback",Item("Play / pause",TogglePlay,"Space"),Item("Go to first frame",()=>{Stop();Document.Seek(0);},"Home"),Item("Go to last frame",()=>{Stop();Document.Seek(Document.Note.Header.FrameCount-1);},"End")),
            LanguageMenu(),
            Menu("_Help",AsyncItem("Keyboard shortcuts and help",()=>Info("Help","Help text"))) } };
        root.Children.Add(menu);
        var sdImport=AsyncButton("Import to SD card",ImportToSdCard);sdImport.Name="ImportToSdCard";
        var toolbar=Wrap(AsyncButton("New",New),AsyncButton("Open…",()=>Open()),AsyncButton("Save project",()=>Save(false)),AsyncButton("Export KWZ",ExportKwz),sdImport,new Separator { Width=7 },_undo,_redo,new Separator{Width=7});
        foreach(var (tool,title) in new[]{(DrawingTool.Select,"Select V"),(DrawingTool.Pencil,"Pencil B"),(DrawingTool.Eraser,"Eraser E"),(DrawingTool.Fill,"Fill G"),(DrawingTool.Rectangle,"Rectangle R"),(DrawingTool.Ellipse,"Ellipse O"),(DrawingTool.Text,"Text T")})
        {
            var button=BindContent(new ToggleButton { Name="Tool"+tool,Padding=new Thickness(8,4),Margin=new Thickness(3,2),IsChecked=tool==DrawingTool.Pencil },title);
            button.Click+=(_,_)=>SelectTool(tool);_tools[tool]=button;toolbar.Children.Add(button);
        }
        var brushLabel=Label("Brush width");brushLabel.Margin=new Thickness(6,0,6,0);toolbar.Children.Add(brushLabel);_brush.Width=70;toolbar.Children.Add(_brush);
        toolbar.Margin=new Thickness(3,6);Grid.SetRow(toolbar,1);root.Children.Add(toolbar);
        var middle=new Grid { ColumnDefinitions=new ColumnDefinitions("190,5,*,5,330") };
        var left=new Grid { RowDefinitions=new RowDefinitions("*,5,180") };
        _objectList.ItemTemplate=new FuncDataTemplate<CanvasObject>((item,_)=>new TextBlock { Text=item?.Label,TextTrimming=TextTrimming.CharacterEllipsis });
        _objectList.SelectionChanged+=(_,_)=>{if(!_sync && _objectList.SelectedItem is CanvasObject item){Document.SelectObject(item.Id);SelectTool(DrawingTool.Select);}};
        left.Children.Add(Section("Objects in this frame",_objectList));
        var history=Section("History",_history);Grid.SetRow(history,2);left.Children.Add(history);
        middle.Children.Add(left);
        var split=new GridSplitter { Width=5,HorizontalAlignment=HorizontalAlignment.Stretch };Grid.SetColumn(split,1);middle.Children.Add(split);
        var center=new Grid { RowDefinitions=new RowDefinitions("26,*,Auto") };
        _fileLabel.Margin=new Thickness(7,4);_fileLabel.TextTrimming=TextTrimming.CharacterEllipsis;center.Children.Add(_fileLabel);
        Grid.SetRow(Canvas,1);center.Children.Add(Canvas);
        var zoom=_zoom;zoom.ItemsSource=new[]{L.T("Fit to window"),"100%","200%","300%","400%"};zoom.SelectedIndex=0;zoom.MinWidth=105;
        zoom.SelectionChanged+=(_,_)=>{Canvas.Zoom=zoom.SelectedIndex;Canvas.InvalidateVisual();};
        _play.Click+=(_,_)=>Run(TogglePlay);
        var playback=Wrap(Button("|◀",()=>{Stop();Document.Seek(0);}),Button("◀",()=>Step(-1)),_play,Button("▶",()=>Step(1)),Button("▶|",()=>{Stop();Document.Seek(Document.Note.Header.FrameCount-1);}),_loop,_time,zoom);
        playback.FlowDirection=FlowDirection.LeftToRight;playback.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(playback,2);center.Children.Add(playback);Grid.SetColumn(center,2);middle.Children.Add(center);
        var split2=new GridSplitter { Width=5,HorizontalAlignment=HorizontalAlignment.Stretch };Grid.SetColumn(split2,3);middle.Children.Add(split2);
        var properties=new StackPanel { Spacing=5,Margin=new Thickness(8) };
        properties.Children.Add(Field("Frame",_frame));properties.Children.Add(Field("Frame rate",_fps));
        properties.Children.Add(new Separator());properties.Children.Add(Field("Current layer",_layer));
        properties.Children.Add(Row(Label("Visible"),_visible[0],_visible[1],_visible[2]));
        properties.Children.Add(Label("Object / drawing ink"));properties.Children.Add(_ink);
        properties.Children.Add(Field("Ink 1",_first,48));properties.Children.Add(Field("Ink 2",_second,48));
        properties.Children.Add(Field("Paper",_paper,48));
        properties.Children.Add(Localize(new TextBlock {TextWrapping=TextWrapping.Wrap},"Six fixed colors; two inks per layer. Changing an ink affects everything using it in this frame and layer.",(c,t)=>c.Text=t));
        properties.Children.Add(new Separator());properties.Children.Add(_selectionTitle);
        _objectTransform.Children.Add(Field("Position X",_objectX));_objectTransform.Children.Add(Field("Position Y",_objectY));
        _objectTransform.Children.Add(Field("Width",_objectWidth));_objectTransform.Children.Add(Field("Height",_objectHeight));
        _objectTransform.Children.Add(Wrap(Button("Duplicate object",Document.DuplicateSelectedObject),Button("Delete object",Document.DeleteSelectedObject)));
        _objectTransform.Children.Add(Button("Rasterize object",Document.FlattenSelectedObject,"Edit with drawing tools after rasterizing. Undo restores the editable object."));
        _textOptions.Children.Add(_text);_textOptions.Children.Add(Field("Text size",_textSize));properties.Children.Add(_textOptions);
        _shapeOptions.Children.Add(_shapeFilled);_shapeOptions.Children.Add(Field("Stroke width",_shapeStroke));properties.Children.Add(_shapeOptions);
        properties.Children.Add(_objectTransform);
        properties.Children.Add(Localize(new TextBlock {TextWrapping=TextWrapping.Wrap},"Object gesture help",(c,t)=>c.Text=t));
        properties.Children.Add(new Separator());properties.Children.Add(Field("3D depth",_depth));
        properties.Children.Add(Wrap(_onion,_grid));properties.Children.Add(new Separator());
        properties.Children.Add(new Separator());properties.Children.Add(Label("Sound effects on this frame"));properties.Children.Add(Row(_effects));
        properties.Children.Add(new Separator());properties.Children.Add(Field("Current author",_author));properties.Children.Add(Button("Apply author",()=>Edit("Change author",n=>n.Header.CurrentAuthorName=_author.Text??"")));
        _signature.TextWrapping=TextWrapping.Wrap;properties.Children.Add(_signature);
        var inspector=Section("Properties / drawing",new ScrollViewer { Content=properties,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });Grid.SetColumn(inspector,4);middle.Children.Add(inspector);
        Grid.SetRow(middle,2);root.Children.Add(middle);
        var horizontal=new GridSplitter { Height=5,ResizeDirection=GridResizeDirection.Rows,HorizontalAlignment=HorizontalAlignment.Stretch };Grid.SetRow(horizontal,3);root.Children.Add(horizontal);
        var timelineArea=new Grid { RowDefinitions=new RowDefinitions("Auto,228") };
        var timelineZoom=new Slider { Minimum=10,Maximum=72,Value=28,Width=120 };
        timelineZoom.ValueChanged+=(_,_)=>{Timeline.FrameWidth=timelineZoom.Value;Timeline.Refresh();};
        var timelineTools=Wrap(Label("Timeline"),Button("＋ Blank frame",Insert),Button("Duplicate frame",Duplicate),Button("Delete frame",Delete),Button("← Earlier",()=>Move(-1)),Button("Later →",()=>Move(1)),Label("Zoom"),timelineZoom,_details);
        timelineTools.FlowDirection=FlowDirection.LeftToRight;
        timelineTools.Margin=new Thickness(5,2);timelineArea.Children.Add(timelineTools);
        var trackGrid=new Grid { ColumnDefinitions=new ColumnDefinitions("150,*"),FlowDirection=FlowDirection.LeftToRight };
        var names=new StackPanel();names.Children.Add(Localize(new TextBlock { Height=28,Margin=new Thickness(7,0) },"Frame / track",(c,t)=>c.Text=t));
        foreach(string label in new[]{"V1  Layer A","V2  Layer B","V3  Layer C"}) names.Children.Add(Localize(new TextBlock { Height=40,Padding=new Thickness(7,10),TextWrapping=TextWrapping.Wrap },label,(c,t)=>c.Text=t));
        names.Children.Add(Localize(new TextBlock { Height=34,Padding=new Thickness(7,7),TextWrapping=TextWrapping.Wrap },"A1  Music",(c,t)=>c.Text=t));names.Children.Add(Localize(new TextBlock { Padding=new Thickness(7,2),TextWrapping=TextWrapping.Wrap },"A2  Sound effects",(c,t)=>c.Text=t));trackGrid.Children.Add(names);
        var scroll=new ScrollViewer { Content=Timeline,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll,1);trackGrid.Children.Add(scroll);Grid.SetRow(trackGrid,1);timelineArea.Children.Add(trackGrid);Grid.SetRow(timelineArea,4);root.Children.Add(timelineArea);
        _status.Margin=new Thickness(7,3);_status.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetRow(_status,5);root.Children.Add(_status);
        _undo.Click+=(_,_)=>{Stop();Document.Undo();};_redo.Click+=(_,_)=>{Stop();Document.Redo();};
        return root;
    }
    private void WireProperties()
    {
        _layer.SelectionChanged+=(_,_)=>{ if(_sync||_layer.SelectedIndex<0)return;Canvas.Layer=Timeline.Layer=_layer.SelectedIndex;if(Document.SelectedObject is {} item)Document.UpdateObject(item with { Layer=Canvas.Layer },"Object layer");else Refresh(); };
        _first.SelectionChanged+=(_,_)=>SetColor(0,_first.SelectedIndex);
        _second.SelectionChanged+=(_,_)=>SetColor(1,_second.SelectedIndex);
        _paper.SelectionChanged+=(_,_)=>{if(!_sync && _paper.SelectedIndex>=0)Edit("Paper color",n=>n.Meta.Entries[Document.Frame].PaperColorIndex=(byte)_paper.SelectedIndex);};
        _ink.SelectionChanged+=(_,_)=>{if(_sync)return;Canvas.Ink=(byte)(_ink.SelectedIndex==2?0:_ink.SelectedIndex+1);UpdateSelected(o=>o with { Ink=Canvas.Ink },"Object color");};
        _frame.ValueChanged+=(_,_)=>{if(!_sync){Stop();Document.Seek((int)(_frame.Value??1)-1);}};
        _fps.SelectionChanged+=(_,_)=>{if(!_sync&&_fps.SelectedIndex>=0)Edit("Change frame rate",n=>n.Header.FrameSpeed=(byte)_fps.SelectedIndex);};
        _depth.ValueChanged+=(_,_)=>{if(!_sync)Edit("Layer depth",n=>{var m=n.Meta.Entries[Document.Frame];byte v=(byte)(_depth.Value??0);if(Canvas.Layer==0)m.LayerADepth=v;else if(Canvas.Layer==1)m.LayerBDepth=v;else m.LayerCDepth=v;});};
        _brush.ValueChanged+=(_,_)=>Canvas.BrushRadius=((int)(_brush.Value??1)-1)/2;
        _text.TextChanged+=(_,_)=>{if(_sync)return;Canvas.StampText=_text.Text??"";if(Document.SelectedObject?.Kind==ObjectKind.Text)UpdateSelected(o=>o with { Text=Canvas.StampText },"Edit text");};
        _textSize.ValueChanged+=(_,_)=>{if(_sync)return;Canvas.TextSize=(int)(_textSize.Value??24);if(Document.SelectedObject?.Kind==ObjectKind.Text)UpdateSelected(o=>o with { FontSize=Canvas.TextSize },"Text size");};
        _objectX.ValueChanged+=(_,_)=>UpdateSelected(o=>o with { X=(int)(_objectX.Value??0) },"Object position");
        _objectY.ValueChanged+=(_,_)=>UpdateSelected(o=>o with { Y=(int)(_objectY.Value??0) },"Object position");
        _objectWidth.ValueChanged+=(_,_)=>UpdateSelected(o=>o with { Width=(int)(_objectWidth.Value??1) },"Shape size");
        _objectHeight.ValueChanged+=(_,_)=>UpdateSelected(o=>o with { Height=(int)(_objectHeight.Value??1) },"Shape size");
        _shapeFilled.Click+=(_,_)=>{if(_sync)return;Canvas.ShapeFilled=_shapeFilled.IsChecked==true;if(Document.SelectedObject?.Kind is ObjectKind.Rectangle or ObjectKind.Ellipse)UpdateSelected(o=>o with { Filled=Canvas.ShapeFilled },"Shape fill");};
        _shapeStroke.ValueChanged+=(_,_)=>{if(_sync)return;Canvas.ShapeStrokeWidth=(int)(_shapeStroke.Value??1);if(Document.SelectedObject?.Kind is ObjectKind.Rectangle or ObjectKind.Ellipse)UpdateSelected(o=>o with { StrokeWidth=Canvas.ShapeStrokeWidth },"Shape stroke");};
        _onion.Click+=(_,_)=>{Canvas.Onion=_onion.IsChecked==true;Canvas.InvalidateVisual();};
        _grid.Click+=(_,_)=>{Canvas.GridLines=_grid.IsChecked==true;Canvas.InvalidateVisual();};
        _loop.Click+=(_,_)=>{if(!_sync)Edit("Loop setting",n=>n.Header.LoopPlayback=_loop.IsChecked==true);};
        for(int i=0;i<3;i++){int layer=i;_visible[i].Click+=(_,_)=>{if(!_sync)Edit("Layer visibility",n=>{if(layer==0)n.Header.LayerAVisible=_visible[layer].IsChecked==true;else if(layer==1)n.Header.LayerBVisible=_visible[layer].IsChecked==true;else n.Header.LayerCVisible=_visible[layer].IsChecked==true;});};}
        for(int i=0;i<4;i++){int track=i;_effects[i].Click+=(_,_)=>{if(!_sync)Edit("Sound effect trigger",n=>{var m=n.Meta.Entries[Document.Frame];m.SoundEffectFlags=_effects[track].IsChecked==true?(byte)(m.SoundEffectFlags|(1<<track)):(byte)(m.SoundEffectFlags&~(1<<track));});};}
    }
    private void UpdateSelected(Func<CanvasObject,CanvasObject> update,string label)
    { if(!_sync && Document.SelectedObject is {} item)Run(()=>{Stop();Document.UpdateObject(update(item),label);}); }
    private void SetColor(int ink,int value)
    {
        if(_sync||value<0)return;
        Edit("Layer ink",n=>{var m=n.Meta.Entries[Document.Frame];int shift=8+Canvas.Layer*8+ink*4;m.Flags=(m.Flags&~(15u<<shift))|((uint)value<<shift);});
    }
    private void Refresh()
    {
        _sync=true;
        try
        {
            var note=Document.Note;var m=note.Meta.Entries[Document.Frame];
            var selected=Document.SelectedObject;
            if(selected!=null){Canvas.Layer=Timeline.Layer=selected.Layer;_layer.SelectedIndex=selected.Layer;Canvas.Ink=selected.Ink;_ink.SelectedIndex=selected.Ink==0?2:selected.Ink-1;}
            _objectTransform.IsVisible=selected!=null;
            _selectionTitle.Text=L.T(selected==null?"Text and shape objects":selected.Kind==ObjectKind.Text?"Text object":selected.Kind==ObjectKind.Rectangle?"Rectangle object":"Ellipse object");
            _textOptions.IsVisible=selected?.Kind==ObjectKind.Text || (selected==null && Canvas.Tool==DrawingTool.Text);
            _shapeOptions.IsVisible=selected?.Kind is ObjectKind.Rectangle or ObjectKind.Ellipse || (selected==null && Canvas.Tool is DrawingTool.Rectangle or DrawingTool.Ellipse);
            if(selected!=null)
            {
                _objectX.Value=selected.X;_objectY.Value=selected.Y;_objectWidth.Value=selected.Width;_objectHeight.Value=selected.Height;
                _objectWidth.IsEnabled=_objectHeight.IsEnabled=selected.Kind!=ObjectKind.Text;
                if(selected.Kind==ObjectKind.Text){_text.Text=Canvas.StampText=selected.Text;_textSize.Value=Canvas.TextSize=selected.FontSize;}
                else{_shapeFilled.IsChecked=Canvas.ShapeFilled=selected.Filled;_shapeStroke.Value=Canvas.ShapeStrokeWidth=selected.StrokeWidth;}
            }
            _objectList.ItemsSource=Document.Objects.Where(o=>o.Frame==Document.Frame).Reverse().ToArray();_objectList.SelectedItem=selected;
            Title=$"KWZ Studio — {(Document.Path==null?L.T("Untitled"):Path.GetFileName(Document.Path))}{(Document.IsDirty?" *":"")}";
            _fileLabel.Text=L.F("Preview / {0}",Document.Path==null?L.T("New animation"):Path.GetFileName(Document.Path));
            _frame.Maximum=note.Header.FrameCount;_frame.Value=Document.Frame+1;_fps.SelectedIndex=note.Header.FrameSpeed;
            _first.SelectedIndex=(int)(m.Flags>>(8+Canvas.Layer*8)&15);_second.SelectedIndex=(int)(m.Flags>>(12+Canvas.Layer*8)&15);
            _ink.SetColors(_first.SelectedIndex,_second.SelectedIndex,6);_ink.SelectedIndex=Canvas.Ink==0?2:Canvas.Ink-1;
            _paper.SelectedIndex=m.PaperColorIndex;_depth.Value=m.LayerDepths[Canvas.Layer];
            _visible[0].IsChecked=note.Header.LayerAVisible;_visible[1].IsChecked=note.Header.LayerBVisible;_visible[2].IsChecked=note.Header.LayerCVisible;
            _loop.IsChecked=note.Header.LoopPlayback;
            for(int i=0;i<4;i++)_effects[i].IsChecked=(m.SoundEffectFlags&(1<<i))!=0;
            _undo.IsEnabled=Document.CanUndo;_redo.IsEnabled=Document.CanRedo;
            _time.Text=$@"{TimeSpan.FromSeconds(Document.Frame/note.Header.FramesPerSecond):mm\:ss\.ff}  ·  {Document.Frame+1:D3} / {note.Header.FrameCount:D3}";
            _details.Text=L.F("320 × 240 · {0:0.#} fps · {1:0.00} s",note.Header.FramesPerSecond,note.Header.FrameCount/note.Header.FramesPerSecond);
            _play.Content=L.T(_timer.IsEnabled?"Ⅱ Pause":"▶ Play");
            if(_revision!=Document.Revision)
            { _revision=Document.Revision;_history.ItemsSource=Document.History;_author.Text=note.Header.CurrentAuthorName; }
            _signature.Text=L.T("Projects keep objects editable. KWZ exports are signed automatically.");
            Canvas.InvalidateVisual();Timeline.Refresh();
        }
        finally{_sync=false;}
    }
    private void SelectTool(DrawingTool tool){Stop();Canvas.Tool=tool;foreach(var pair in _tools)pair.Value.IsChecked=pair.Key==tool;Refresh();Canvas.Focus();}
    private void Edit(string name,Action<KwzFile> action)=>Run(()=>{Stop();Document.Edit(name,action);_status.Text=L.T(name);});
    private void Remap(string label,List<int> map,int frame)=>Run(()=>{Stop();Document.RemapFrames(label,map,frame);});
    private List<int> FrameMap()=>Enumerable.Range(0,Document.Note.Header.FrameCount).ToList();
    private void Insert(){var map=FrameMap();map.Insert(Document.Frame,-1);Remap("Insert blank frame",map,Document.Frame);}
    private void Duplicate(){var map=FrameMap();int at=Document.Frame+1;map.Insert(at,Document.Frame);Remap("Duplicate current frame",map,at);}
    private void Delete(){if(Document.Note.Header.FrameCount==1)throw new InvalidOperationException(L.T("Keep at least one frame."));var map=FrameMap();map.RemoveAt(Document.Frame);Remap("Delete current frame",map,Math.Min(Document.Frame,map.Count-1));}
    private void Move(int direction){int target=Document.Frame+direction;if(target<0||target>=Document.Note.Header.FrameCount)return;var map=FrameMap();int source=map[Document.Frame];map.RemoveAt(Document.Frame);map.Insert(target,source);Remap("Move current frame",map,target);}
    private void Step(int delta){Stop();Document.Seek(Document.Frame+delta);}
    private void TogglePlay()
    {
        if(_timer.IsEnabled){Stop();return;}
        if(Document.Frame==Document.Note.Header.FrameCount-1)Document.Seek(0);
        _startFrame=Document.Frame;
        _audio.Start(Document.Note,Document.Frame);
        _clock.Restart();_timer.Start();_play.Content=L.T("Ⅱ Pause");
    }
    private void Tick()
    {
        int frame=_startFrame+(int)(_clock.Elapsed.TotalSeconds*Document.Note.Header.FramesPerSecond);
        if(frame>=Document.Note.Header.FrameCount)
        {
            if(Document.Note.Header.LoopPlayback){_startFrame=0;frame=0;_audio.Start(Document.Note,0);_clock.Restart();}
            else{Stop();Document.Seek(Document.Note.Header.FrameCount-1);return;}
        }
        if(frame!=Document.Frame)Document.Seek(frame);
    }
    private void Stop(){_timer.Stop();_clock.Stop();_audio.Stop();_play.Content=L.T("▶ Play");}
    private async void OnKey(object? sender,KeyEventArgs e)
    {
        if(_sdImporting) { e.Handled=true;return; }
        bool ctrl=e.KeyModifiers.HasFlag(KeyModifiers.Control),shift=e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if(ctrl && shift && e.Key==Key.E){e.Handled=true;await Guard(ExportKwz);return;}
        if(ctrl && e.Key is Key.O or Key.S or Key.N) {e.Handled=true;await Guard(()=>e.Key==Key.O?Open():e.Key==Key.N?New():Save(shift));return;}
        if(e.Source is TextBox or NumericUpDown || (e.Source is Control c && c.FindAncestorOfType<TextBox>()!=null))return;
        if(ctrl){if(e.Key==Key.Z){Stop();Document.Undo();}else if(e.Key==Key.Y){Stop();Document.Redo();}else if(e.Key==Key.D){if(Document.SelectedObject!=null)Document.DuplicateSelectedObject();else Duplicate();}else return;e.Handled=true;return;}
        Run(()=>{switch(e.Key){case Key.Space:TogglePlay();break;case Key.Left:Step(-1);break;case Key.Right:Step(1);break;case Key.Home:Stop();Document.Seek(0);break;case Key.End:Stop();Document.Seek(Document.Note.Header.FrameCount-1);break;case Key.Delete:if(Document.SelectedObject!=null)Document.DeleteSelectedObject();else Delete();break;case Key.V:SelectTool(DrawingTool.Select);break;case Key.O:SelectTool(DrawingTool.Ellipse);break;case Key.Escape:Canvas.CancelInteraction();Document.SelectObject(null);break;case Key.B:SelectTool(DrawingTool.Pencil);break;case Key.E:SelectTool(DrawingTool.Eraser);break;case Key.G:SelectTool(DrawingTool.Fill);break;case Key.R:SelectTool(DrawingTool.Rectangle);break;case Key.T:SelectTool(DrawingTool.Text);break;default:return;}e.Handled=true;});
    }
    private async Task New(){Stop();if(!await ConfirmDiscard())return;Document.New();}
    public async Task Open(string? path=null)
    {
        Stop();
        try
        {
            path??=await Pick("Open project or KWZ animation",new FilePickerFileType(L.T("KWZ projects and animations")){Patterns=["*.kwzproj","*.kwz"]});
            if(path==null||!await ConfirmDiscard())return;
            Document.Open(path);_status.Text=L.F("Opened {0}",path);
        }catch(Exception error){await Error(error);}
    }
    private async Task Save(bool forceAs)
    {
        Stop();string? path=Document.Path;
        if(forceAs||!Document.IsProject||path==null)
        {
            var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=L.T("Save editable project"),DefaultExtension="kwzproj",SuggestedFileName=(path==null?L.T("New animation"):Path.GetFileNameWithoutExtension(path))+".kwzproj",FileTypeChoices=[new(L.T("KWZ Studio project")){Patterns=["*.kwzproj"]}],ShowOverwritePrompt=true});
            path=file?.TryGetLocalPath();if(path==null)return;
        }
        Document.SaveProject(path);_status.Text=L.F("Project saved; text and shapes remain editable: {0}",Path.GetFileName(path));
    }
    private async Task ExportKwz()
    {
        Stop();
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=L.T("Export game KWZ"),DefaultExtension="kwz",SuggestedFileName=(Document.Path==null?L.T("New animation"):Path.GetFileNameWithoutExtension(Document.Path))+"_edited.kwz",FileTypeChoices=[new(L.T("KWZ animation")){Patterns=["*.kwz"]}],ShowOverwritePrompt=true});
        string? path=file?.TryGetLocalPath();if(path==null)return;
        Document.ExportKwz(path);_status.Text=L.F("Signed KWZ exported; project objects remain editable: {0}",Path.GetFileName(path));
    }
    private async Task<bool> ConfirmDiscard()
    {
        if(!Document.IsDirty||(!Document.CanUndo&&Document.Path==null))return true;
        string? answer=await Ask("Unsaved changes","Save your changes before continuing?",["Save","Don't save","Cancel"]);
        if(answer=="Don't save")return true;
        if(answer!="Save")return false;
        await Guard(()=>Save(false));return !Document.IsDirty;
    }
    private async Task<string?> Pick(string title,FilePickerFileType filter)
    { var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=L.T(title),AllowMultiple=false,FileTypeFilter=[filter]});return files.FirstOrDefault()?.TryGetLocalPath(); }
    private async Task ImportImage()
    {
        Stop();string? path=await Pick("Import to current layer (quantize to two inks)",new(L.T("Images")){Patterns=["*.png","*.bmp","*.jpg"]});if(path==null)return;
        using var image=SixLabors.ImageSharp.Image.Load<Rgba32>(path);image.Mutate(c=>c.Resize(320,240));
        var meta=Document.Note.Meta.Entries[Document.Frame];
        Rgba32[] colors=[meta.PaperColor.ToPixel<Rgba32>(),KwzMeta.FramePalette[_first.SelectedIndex].ToPixel<Rgba32>(),KwzMeta.FramePalette[_second.SelectedIndex].ToPixel<Rgba32>()];
        byte[] layer=new byte[320*240];
        for(int y=0;y<240;y++)for(int x=0;x<320;x++)
        {
            var pixel=image[x,y];if(pixel.A<128)continue;
            int best=0,distance=int.MaxValue;
            for(int i=0;i<3;i++){int dr=pixel.R-colors[i].R,dg=pixel.G-colors[i].G,db=pixel.B-colors[i].B;int d=dr*dr+dg*dg+db*db;if(d<distance){best=i;distance=d;}}
            layer[y*320+x]=(byte)best;
        }
        Edit("Import layer image",n=>n.FrameData.SetLayerPixels(Document.Frame,Canvas.Layer,layer));
    }
    private async Task ExportImage()
    {
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=L.T("Export current frame"),SuggestedFileName=$"frame_{Document.Frame+1:D3}.png",DefaultExtension="png",ShowOverwritePrompt=true});
        string? path=file?.TryGetLocalPath();if(path==null)return;
        using var flattened=Document.CreateFlattenedNote();
        var (layers,composite)=flattened.FrameData.FrameToImage(Document.Frame);
        try{await composite.SaveAsPngAsync(path);}finally{composite.Dispose();foreach(var layer in layers)layer.Dispose();}
        _status.Text=L.T("Frame exported.");
    }
    private async Task ImportAudio(int track)
    {
        Stop();string? path=await Pick("Replace audio track: PCM WAV",new(L.T("WAV audio")){Patterns=["*.wav"]});if(path==null)return;
        byte[] pcm=AudioPreview.ReadWav(path);
        Edit("Replace audio track",n=>{if(n.Sound==null)throw new InvalidOperationException(L.T("This file has no sound section."));n.Sound.SetAudioTrack(track,pcm);if(track==0)n.Sound.Speed=n.Header.FrameSpeed;});
    }
    private async Task ExportAudio(int track)
    {
        if(Document.Note.Sound==null)throw new InvalidOperationException(L.T("This file has no sound section."));
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=L.T("Export audio track"),SuggestedFileName="bgm.wav",DefaultExtension="wav",ShowOverwritePrompt=true});
        string? path=file?.TryGetLocalPath();if(path!=null)Utils.WriteWavFile(path,Document.Note.Sound.GetAudioTrack(track),KwzSound.SampleRate);
    }
    private void Run(Action action){try{action();}catch(Exception error){_ = Error(error);}}
    private async Task Guard(Func<Task> action){try{await action();}catch(Exception error){await Error(error);}}
    private Task Error(Exception error){_status.Text=error.Message;return Info("Operation incomplete",error.Message);}
    private async Task Info(string title,string message)=>await Ask(title,message,["OK"]);
    private async Task<string?> Ask(string title,string message,string[] choices)
    {
        var dialog=new ClassicWindow { Title=L.T(title),FlowDirection=FlowDirection,Width=520,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=FontFamily,FontSize=12 };
        var body=new StackPanel { Margin=new Thickness(18),Spacing=16 };
        body.Children.Add(new TextBlock { Text=L.T(message),TextWrapping=TextWrapping.Wrap });
        var buttons=Wrap();buttons.HorizontalAlignment=HorizontalAlignment.Right;
        foreach(string choice in choices){var button=new Button{Content=L.T(choice),MinWidth=72,Padding=new Thickness(10,5)};button.Click+=(_,_)=>dialog.Close(choice);buttons.Children.Add(button);}
        body.Children.Add(buttons);dialog.Content=body;return await dialog.ShowDialog<string?>(this);
    }
}
