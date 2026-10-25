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

public sealed class MainWindow : ClassicWindow
{
    public EditorDocument Document { get; }=new();
    public PreviewCanvas Canvas { get; }
    public TimelineControl Timeline { get; }
    private readonly TextBlock _status=new(),_details=new(),_time=new(),_signature=new(),_fileLabel=new();
    private readonly ListBox _assets=new(),_history=new(),_objectList=new();
    private readonly StackPanel _objectTransform=new(){Spacing=4},_textOptions=new(){Spacing=4},_shapeOptions=new(){Spacing=4};
    private readonly TextBlock _selectionTitle=new(){Text="未选中对象",FontWeight=FontWeight.Bold};
    private readonly NumericUpDown _objectX=Number(0,-16384,16384),_objectY=Number(0,-16384,16384),_objectWidth=Number(80,1,16384),_objectHeight=Number(40,1,16384),_shapeStroke=Number(1,1,64);
    private readonly CheckBox _shapeFilled=new(){Content="实心形状",IsChecked=true};
    private readonly ComboBox _layer=new(){ItemsSource=new[]{"图层 A","图层 B","图层 C"},SelectedIndex=0};
    private readonly ColorSwatchPicker _first=new([0,1,2,3,4,5]){Name="FirstInkPalette"},_second=new([0,1,2,3,4,5]){Name="SecondInkPalette"},_paper=new([0,1,2,3,4,5,6]){Name="PaperPalette"};
    private readonly ComboBox _fps=new(){ItemsSource=new[]{"0.2","0.5","1","2","4","6","8","12","20","24","30"},SelectedIndex=7};
    private readonly ColorSwatchPicker _ink=new([1,2,6],["墨色 1","墨色 2","透明"]){Name="DrawingInk",SelectedIndex=0};
    private readonly NumericUpDown _frame=Number(1,1,65535),_depth=Number(0,0,255),_brush=Number(3,1,31),_textSize=Number(24,6,256);
    private readonly TextBox _text=new(){Name="ObjectText",Text="中文汉化",MaxLength=10000,AcceptsReturn=true,Height=62,Watermark="输入中文，然后点击画面放置"};
    private readonly TextBox _author=new(){MaxLength=11};
    private readonly CheckBox[] _visible=[new(){Content="A"},new(){Content="B"},new(){Content="C"}];
    private readonly CheckBox[] _effects=[new(){Content="SE 1"},new(){Content="SE 2"},new(){Content="SE 3"},new(){Content="SE 4"}];
    private readonly CheckBox _loop=new(){Content="循环"},_onion=new(){Content="洋葱皮"},_grid=new(){Content="8 px 网格"};
    private readonly Button _play=new(){Content="▶ 播放",MinWidth=75},_undo=new(){Content="↶ 撤销"},_redo=new(){Content="↷ 重做"};
    private readonly Dictionary<DrawingTool,ToggleButton> _tools=[];
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(10)};
    private readonly Stopwatch _clock=new();
    private readonly List<string> _assetPaths=[];
    private readonly AudioPreview _audio=new();
    private readonly string? _root;
    private int _startFrame,_revision=-1;
    private bool _sync,_allowClose,_closePending;
    public MainWindow(string? initialPath=null)
    {
        Title="KWZ Studio";Width=1440;Height=960;MinWidth=1080;MinHeight=760;
        FontFamily=new FontFamily("Microsoft YaHei UI, Segoe UI");FontSize=12;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        _root=FindRoot();
        Canvas=new PreviewCanvas(Document);Timeline=new TimelineControl(Document);
        Content=BuildWorkspace();
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
            if(_allowClose || !Document.IsDirty) return;
            e.Cancel=true;
            if(_closePending) return;
            _closePending=true;Stop();
            if(await ConfirmDiscard()) { _allowClose=true;Close(); }
            _closePending=false;
        };
        Closed+=(_,_)=>{ _timer.Stop();_audio.Dispose();Canvas.Dispose();Document.Dispose(); };
        Opened+=async (_,_)=>
        {
            try
            {
                if(_root!=null)
                {
                    string unpack=Path.Combine(_root,"unpack");
                    if(Directory.Exists(unpack))
                        _assetPaths.AddRange(Directory.EnumerateFiles(unpack,"*.kwz",SearchOption.AllDirectories).DistinctBy(Path.GetFileName).OrderBy(Path.GetFileName));
                }
                _assets.ItemsSource=_assetPaths.Select((p,i)=>$"{i+1:00}  {Path.GetFileNameWithoutExtension(p)[..Math.Min(18,Path.GetFileNameWithoutExtension(p).Length)]}…").ToArray();
                string? first=initialPath??_assetPaths.FirstOrDefault();
                if(first!=null && File.Exists(first)) { Document.Open(first); _assets.SelectedIndex=_assetPaths.IndexOf(first); }
                else Document.New();
                Refresh(); _status.Text="就绪 · 拖入 KWZ 开始编辑；空格播放，方向键逐帧。";
            }
            catch(Exception error) { await Error(error); }
        };
    }
    private static string? FindRoot()
    {
        foreach(string start in new[]{AppContext.BaseDirectory,Environment.CurrentDirectory})
            for(var d=new DirectoryInfo(start);d!=null;d=d.Parent)
                if(Directory.Exists(Path.Combine(d.FullName,"rom")) && Directory.Exists(Path.Combine(d.FullName,"tools","libkwz"))) return d.FullName;
        return null;
    }
    private static NumericUpDown Number(decimal value,decimal min,decimal max)=>new(){Value=value,Minimum=min,Maximum=max,Increment=1,FormatString="0",MinWidth=64};
    private static TextBlock Label(string text)=>new(){Text=text,VerticalAlignment=VerticalAlignment.Center};
    private static StackPanel Row(params Control[] children)
    {
        var result=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center };
        foreach(var child in children) result.Children.Add(child);return result;
    }
    private static Control Field(string title,Control control,int labelWidth=74)
    {
        var grid=new Grid { ColumnDefinitions=new ColumnDefinitions($"{labelWidth},*"),Margin=new Thickness(0,3) };
        grid.Children.Add(Label(title));Grid.SetColumn(control,1);grid.Children.Add(control);return grid;
    }
    private static Control Section(string title,Control content)
    {
        var panel=new DockPanel();
        var heading=new Border { Background=new SolidColorBrush(AColor.Parse("#D4D0C8")),Padding=new Thickness(7,5),BorderBrush=Brushes.Gray,BorderThickness=new Thickness(0,0,0,1),Child=new TextBlock { Text=title,FontWeight=FontWeight.Bold } };
        DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);panel.Children.Add(content);
        return new Border { BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),Child=panel };
    }
    private Button Button(string title,Action action,string? tip=null)
    {
        var b=new Button { Content=title,Padding=new Thickness(9,4),MinHeight=27 };
        b.Click+=(_,_)=>Run(action);if(tip!=null)ToolTip.SetTip(b,tip);return b;
    }
    private Button AsyncButton(string title,Func<Task> action)
    { var b=Button(title,()=>{}); b.Click+=async (_,_)=>await Guard(action);return b; }
    private MenuItem Menu(string title,params Control[] children)=>new(){Header=title,ItemsSource=children};
    private MenuItem Item(string title,Action action,string? shortcut=null)
    {
        var item=new MenuItem { Header=title };if(shortcut!=null)item.InputGesture=KeyGesture.Parse(shortcut);
        item.Click+=(_,_)=>Run(action);return item;
    }
    private MenuItem AsyncItem(string title,Func<Task> action,string? shortcut=null)
    {
        var item=new MenuItem { Header=title };if(shortcut!=null)item.InputGesture=KeyGesture.Parse(shortcut);
        item.Click+=async (_,_)=>await Guard(action);return item;
    }
    private Control BuildWorkspace()
    {
        var root=new Grid { RowDefinitions=new RowDefinitions("Auto,Auto,*,5,Auto,24"),Margin=new Thickness(3) };
        var menu=new Avalonia.Controls.Menu { ItemsSource=new[]{
            Menu("_文件",AsyncItem("新建动画",New,"Ctrl+N"),AsyncItem("打开…",()=>Open(),"Ctrl+O"),AsyncItem("保存工程",()=>Save(false),"Ctrl+S"),AsyncItem("工程另存为…",()=>Save(true),"Ctrl+Shift+S"),AsyncItem("导出 KWZ…",ExportKwz,"Ctrl+Shift+E"),new Separator(),Item("退出",Close)),
            Menu("_编辑",Item("撤销",()=>{Stop();Document.Undo();},"Ctrl+Z"),Item("重做",()=>{Stop();Document.Redo();},"Ctrl+Y"),new Separator(),Item("插入空白帧",Insert),Item("复制当前帧",Duplicate,"Ctrl+D"),Item("删除当前帧",Delete,"Delete"),Item("当前帧前移",()=>Move(-1)),Item("当前帧后移",()=>Move(1))),
            Menu("_素材",AsyncItem("导入 PNG 到当前图层…",ImportImage),AsyncItem("导出当前画面 PNG…",ExportImage),new Separator(),AsyncItem("替换背景音乐 WAV…",()=>ImportAudio(0)),AsyncItem("导出背景音乐 WAV…",()=>ExportAudio(0)),AsyncItem("替换音效 1…",()=>ImportAudio(1)),AsyncItem("替换音效 2…",()=>ImportAudio(2)),AsyncItem("替换音效 3…",()=>ImportAudio(3)),AsyncItem("替换音效 4…",()=>ImportAudio(4))),
            Menu("_播放",Item("播放 / 暂停",TogglePlay,"Space"),Item("回到开头",()=>{Stop();Document.Seek(0);},"Home"),Item("跳到结尾",()=>{Stop();Document.Seek(Document.Note.Header.FrameCount-1);},"End")),
            Menu("_帮助",AsyncItem("快捷键与使用说明",()=>Info("使用说明","空格：播放 / 暂停\n← / →：上一帧 / 下一帧\nHome / End：首帧 / 末帧\nCtrl+Z / Y：撤销 / 重做\nCtrl+D / Delete：复制 / 删除选中对象；无选择时操作当前帧\nV / B / E / G / R / O / T：选择 / 画笔 / 橡皮 / 填充 / 矩形 / 椭圆 / 文字\n\n每层只有两种墨色，透明像素显示下层。\n文字与形状是可移动对象。V 选择，右下角缩放，Esc 取消。\n保存 .kwzproj 工程，Ctrl+Shift+E 导出游戏 KWZ。\n\n主题：Classic.Avalonia.Theme 11.3.0.3"))) } };
        root.Children.Add(menu);
        var toolbar=Row(AsyncButton("新建",New),AsyncButton("打开…",()=>Open()),AsyncButton("保存工程",()=>Save(false)),AsyncButton("导出 KWZ",ExportKwz),new Separator { Width=7 },_undo,_redo,new Separator{Width=7});
        foreach(var (tool,title) in new[]{(DrawingTool.Select,"选择 V"),(DrawingTool.Pencil,"画笔 B"),(DrawingTool.Eraser,"橡皮 E"),(DrawingTool.Fill,"填充 G"),(DrawingTool.Rectangle,"矩形 R"),(DrawingTool.Ellipse,"椭圆 O"),(DrawingTool.Text,"文字 T")})
        {
            var button=new ToggleButton { Content=title,Padding=new Thickness(8,4),IsChecked=tool==DrawingTool.Pencil };
            button.Click+=(_,_)=>SelectTool(tool);_tools[tool]=button;toolbar.Children.Add(button);
        }
        toolbar.Children.Add(Label("笔宽"));_brush.Width=70;toolbar.Children.Add(_brush);
        toolbar.Margin=new Thickness(3,6);Grid.SetRow(toolbar,1);root.Children.Add(toolbar);
        var middle=new Grid { ColumnDefinitions=new ColumnDefinitions("218,5,*,5,276") };
        var left=new Grid { RowDefinitions=new RowDefinitions("*,5,150,5,120") };
        _assets.DoubleTapped+=async (_,_)=>{if(_assets.SelectedIndex>=0)await Open(_assetPaths[_assets.SelectedIndex]);};
        ToolTip.SetTip(_assets,"双击打开示例动画；也可以直接拖入 KWZ 文件。");
        left.Children.Add(Section("项目素材 / ROMFS",_assets));
        _objectList.ItemTemplate=new FuncDataTemplate<CanvasObject>((item,_)=>new TextBlock { Text=item?.Label,TextTrimming=TextTrimming.CharacterEllipsis });
        _objectList.SelectionChanged+=(_,_)=>{if(!_sync && _objectList.SelectedItem is CanvasObject item){Document.SelectObject(item.Id);SelectTool(DrawingTool.Select);}};
        var objectSection=Section("本帧对象",_objectList);Grid.SetRow(objectSection,2);left.Children.Add(objectSection);
        var history=Section("操作历史",_history);Grid.SetRow(history,4);left.Children.Add(history);
        middle.Children.Add(left);
        var split=new GridSplitter { Width=5,HorizontalAlignment=HorizontalAlignment.Stretch };Grid.SetColumn(split,1);middle.Children.Add(split);
        var center=new Grid { RowDefinitions=new RowDefinitions("26,*,40") };
        _fileLabel.Margin=new Thickness(7,4);_fileLabel.TextTrimming=TextTrimming.CharacterEllipsis;center.Children.Add(_fileLabel);
        Grid.SetRow(Canvas,1);center.Children.Add(Canvas);
        var zoom=new ComboBox { ItemsSource=new[]{"适合窗口","100%","200%","300%","400%"},SelectedIndex=0,Width=105 };
        zoom.SelectionChanged+=(_,_)=>{Canvas.Zoom=zoom.SelectedIndex;Canvas.InvalidateVisual();};
        _play.Click+=(_,_)=>Run(TogglePlay);
        var playback=Row(Button("|◀",()=>{Stop();Document.Seek(0);}),Button("◀",()=>Step(-1)),_play,Button("▶",()=>Step(1)),Button("▶|",()=>{Stop();Document.Seek(Document.Note.Header.FrameCount-1);}),_loop,_time,zoom);
        playback.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(playback,2);center.Children.Add(playback);Grid.SetColumn(center,2);middle.Children.Add(center);
        var split2=new GridSplitter { Width=5,HorizontalAlignment=HorizontalAlignment.Stretch };Grid.SetColumn(split2,3);middle.Children.Add(split2);
        var properties=new StackPanel { Spacing=5,Margin=new Thickness(8) };
        properties.Children.Add(Field("帧位置",_frame));properties.Children.Add(Field("帧率",_fps));
        properties.Children.Add(new Separator());properties.Children.Add(Field("当前图层",_layer));
        properties.Children.Add(Row(Label("可见"),_visible[0],_visible[1],_visible[2]));
        properties.Children.Add(Label("对象 / 笔色"));properties.Children.Add(_ink);
        properties.Children.Add(Field("墨色 1",_first,48));properties.Children.Add(Field("墨色 2",_second,48));
        properties.Children.Add(Field("纸张",_paper,48));
        properties.Children.Add(new TextBlock { Text="固定六色；每层两种墨色。修改墨色会改变本帧该层所有使用它的内容。",TextWrapping=TextWrapping.Wrap });
        properties.Children.Add(new Separator());properties.Children.Add(_selectionTitle);
        _objectTransform.Children.Add(Field("位置 X",_objectX));_objectTransform.Children.Add(Field("位置 Y",_objectY));
        _objectTransform.Children.Add(Field("宽度",_objectWidth));_objectTransform.Children.Add(Field("高度",_objectHeight));
        _objectTransform.Children.Add(Row(Button("复制对象",Document.DuplicateSelectedObject),Button("删除对象",Document.DeleteSelectedObject)));
        _objectTransform.Children.Add(Button("合并到像素",Document.FlattenSelectedObject,"合并后可用画笔修改；可以撤销恢复对象。"));
        _textOptions.Children.Add(_text);_textOptions.Children.Add(Field("文字大小",_textSize));properties.Children.Add(_textOptions);
        _shapeOptions.Children.Add(_shapeFilled);_shapeOptions.Children.Add(Field("线条宽度",_shapeStroke));properties.Children.Add(_shapeOptions);
        properties.Children.Add(_objectTransform);
        properties.Children.Add(new TextBlock { Text="V 选择 / 拖动 · 右下角缩放\n方向键微调 · Shift 加速 · Esc 取消",TextWrapping=TextWrapping.Wrap });
        properties.Children.Add(new Separator());properties.Children.Add(Field("立体深度",_depth));
        properties.Children.Add(Row(_onion,_grid));properties.Children.Add(new Separator());
        properties.Children.Add(new Separator());properties.Children.Add(Label("当前帧触发音效"));properties.Children.Add(Row(_effects));
        properties.Children.Add(new Separator());properties.Children.Add(Field("当前作者",_author));properties.Children.Add(Button("应用作者",()=>Edit("修改作者",n=>n.Header.CurrentAuthorName=_author.Text??"")));
        _signature.TextWrapping=TextWrapping.Wrap;properties.Children.Add(_signature);
        var inspector=Section("属性 / 绘制",new ScrollViewer { Content=properties,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });Grid.SetColumn(inspector,4);middle.Children.Add(inspector);
        Grid.SetRow(middle,2);root.Children.Add(middle);
        var horizontal=new GridSplitter { Height=5,ResizeDirection=GridResizeDirection.Rows,HorizontalAlignment=HorizontalAlignment.Stretch };Grid.SetRow(horizontal,3);root.Children.Add(horizontal);
        var timelineArea=new Grid { RowDefinitions=new RowDefinitions("33,228") };
        var timelineZoom=new Slider { Minimum=10,Maximum=72,Value=28,Width=120 };
        timelineZoom.ValueChanged+=(_,_)=>{Timeline.FrameWidth=timelineZoom.Value;Timeline.Refresh();};
        var timelineTools=Row(Label("时间线"),Button("＋ 空白帧",Insert),Button("复制帧",Duplicate),Button("删除帧",Delete),Button("← 前移",()=>Move(-1)),Button("后移 →",()=>Move(1)),Label("缩放"),timelineZoom,_details);
        timelineTools.Margin=new Thickness(5,2);timelineArea.Children.Add(timelineTools);
        var trackGrid=new Grid { ColumnDefinitions=new ColumnDefinitions("100,*") };
        var names=new StackPanel();names.Children.Add(new TextBlock { Text="帧 / 轨道",Height=28,Margin=new Thickness(7,0) });
        foreach(string label in new[]{"V1  图层 A","V2  图层 B","V3  图层 C"}) names.Children.Add(new TextBlock { Text=label,Height=40,Padding=new Thickness(7,10) });
        names.Children.Add(new TextBlock { Text="A1  背景音乐",Height=34,Padding=new Thickness(7,7) });names.Children.Add(new TextBlock { Text="A2  音效",Padding=new Thickness(7,2) });trackGrid.Children.Add(names);
        var scroll=new ScrollViewer { Content=Timeline,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll,1);trackGrid.Children.Add(scroll);Grid.SetRow(trackGrid,1);timelineArea.Children.Add(trackGrid);Grid.SetRow(timelineArea,4);root.Children.Add(timelineArea);
        _status.Margin=new Thickness(7,3);_status.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetRow(_status,5);root.Children.Add(_status);
        _undo.Click+=(_,_)=>{Stop();Document.Undo();};_redo.Click+=(_,_)=>{Stop();Document.Redo();};
        return root;
    }
    private void WireProperties()
    {
        _layer.SelectionChanged+=(_,_)=>{ if(_sync||_layer.SelectedIndex<0)return;Canvas.Layer=Timeline.Layer=_layer.SelectedIndex;if(Document.SelectedObject is {} item)Document.UpdateObject(item with { Layer=Canvas.Layer },"对象图层");else Refresh(); };
        _first.SelectionChanged+=(_,_)=>SetColor(0,_first.SelectedIndex);
        _second.SelectionChanged+=(_,_)=>SetColor(1,_second.SelectedIndex);
        _paper.SelectionChanged+=(_,_)=>{if(!_sync && _paper.SelectedIndex>=0)Edit("纸张颜色",n=>n.Meta.Entries[Document.Frame].PaperColorIndex=(byte)_paper.SelectedIndex);};
        _ink.SelectionChanged+=(_,_)=>{if(_sync)return;Canvas.Ink=(byte)(_ink.SelectedIndex==2?0:_ink.SelectedIndex+1);UpdateSelected(o=>o with { Ink=Canvas.Ink },"对象颜色");};
        _frame.ValueChanged+=(_,_)=>{if(!_sync){Stop();Document.Seek((int)(_frame.Value??1)-1);}};
        _fps.SelectionChanged+=(_,_)=>{if(!_sync&&_fps.SelectedIndex>=0)Edit("修改帧率",n=>n.Header.FrameSpeed=(byte)_fps.SelectedIndex);};
        _depth.ValueChanged+=(_,_)=>{if(!_sync)Edit("图层深度",n=>{var m=n.Meta.Entries[Document.Frame];byte v=(byte)(_depth.Value??0);if(Canvas.Layer==0)m.LayerADepth=v;else if(Canvas.Layer==1)m.LayerBDepth=v;else m.LayerCDepth=v;});};
        _brush.ValueChanged+=(_,_)=>Canvas.BrushRadius=((int)(_brush.Value??1)-1)/2;
        _text.TextChanged+=(_,_)=>{if(_sync)return;Canvas.StampText=_text.Text??"";if(Document.SelectedObject?.Kind==ObjectKind.Text)UpdateSelected(o=>o with { Text=Canvas.StampText },"修改文字");};
        _textSize.ValueChanged+=(_,_)=>{if(_sync)return;Canvas.TextSize=(int)(_textSize.Value??24);if(Document.SelectedObject?.Kind==ObjectKind.Text)UpdateSelected(o=>o with { FontSize=Canvas.TextSize },"文字大小");};
        _objectX.ValueChanged+=(_,_)=>UpdateSelected(o=>o with { X=(int)(_objectX.Value??0) },"对象位置");
        _objectY.ValueChanged+=(_,_)=>UpdateSelected(o=>o with { Y=(int)(_objectY.Value??0) },"对象位置");
        _objectWidth.ValueChanged+=(_,_)=>UpdateSelected(o=>o with { Width=(int)(_objectWidth.Value??1) },"形状尺寸");
        _objectHeight.ValueChanged+=(_,_)=>UpdateSelected(o=>o with { Height=(int)(_objectHeight.Value??1) },"形状尺寸");
        _shapeFilled.Click+=(_,_)=>{if(_sync)return;Canvas.ShapeFilled=_shapeFilled.IsChecked==true;if(Document.SelectedObject?.Kind is ObjectKind.Rectangle or ObjectKind.Ellipse)UpdateSelected(o=>o with { Filled=Canvas.ShapeFilled },"形状填充");};
        _shapeStroke.ValueChanged+=(_,_)=>{if(_sync)return;Canvas.ShapeStrokeWidth=(int)(_shapeStroke.Value??1);if(Document.SelectedObject?.Kind is ObjectKind.Rectangle or ObjectKind.Ellipse)UpdateSelected(o=>o with { StrokeWidth=Canvas.ShapeStrokeWidth },"形状线宽");};
        _onion.Click+=(_,_)=>{Canvas.Onion=_onion.IsChecked==true;Canvas.InvalidateVisual();};
        _grid.Click+=(_,_)=>{Canvas.GridLines=_grid.IsChecked==true;Canvas.InvalidateVisual();};
        _loop.Click+=(_,_)=>{if(!_sync)Edit("循环设置",n=>n.Header.LoopPlayback=_loop.IsChecked==true);};
        for(int i=0;i<3;i++){int layer=i;_visible[i].Click+=(_,_)=>{if(!_sync)Edit("图层可见性",n=>{if(layer==0)n.Header.LayerAVisible=_visible[layer].IsChecked==true;else if(layer==1)n.Header.LayerBVisible=_visible[layer].IsChecked==true;else n.Header.LayerCVisible=_visible[layer].IsChecked==true;});};}
        for(int i=0;i<4;i++){int track=i;_effects[i].Click+=(_,_)=>{if(!_sync)Edit("音效触发",n=>{var m=n.Meta.Entries[Document.Frame];m.SoundEffectFlags=_effects[track].IsChecked==true?(byte)(m.SoundEffectFlags|(1<<track)):(byte)(m.SoundEffectFlags&~(1<<track));});};}
    }
    private void UpdateSelected(Func<CanvasObject,CanvasObject> update,string label)
    { if(!_sync && Document.SelectedObject is {} item)Run(()=>{Stop();Document.UpdateObject(update(item),label);}); }
    private void SetColor(int ink,int value)
    {
        if(_sync||value<0)return;
        Edit("图层墨色",n=>{var m=n.Meta.Entries[Document.Frame];int shift=8+Canvas.Layer*8+ink*4;m.Flags=(m.Flags&~(15u<<shift))|((uint)value<<shift);});
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
            _selectionTitle.Text=selected==null?"文字与形状对象":selected.Kind==ObjectKind.Text?"文字对象":selected.Kind==ObjectKind.Rectangle?"矩形对象":"椭圆对象";
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
            Title=$"KWZ Studio — {(Document.Path==null?"未命名":Path.GetFileName(Document.Path))}{(Document.IsDirty?" *":"")}";
            _fileLabel.Text=$"节目监视器  /  {(Document.Path==null?"新建动画":Path.GetFileName(Document.Path))}";
            _frame.Maximum=note.Header.FrameCount;_frame.Value=Document.Frame+1;_fps.SelectedIndex=note.Header.FrameSpeed;
            _first.SelectedIndex=(int)(m.Flags>>(8+Canvas.Layer*8)&15);_second.SelectedIndex=(int)(m.Flags>>(12+Canvas.Layer*8)&15);
            _ink.SetColors(_first.SelectedIndex,_second.SelectedIndex,6);_ink.SelectedIndex=Canvas.Ink==0?2:Canvas.Ink-1;
            _paper.SelectedIndex=m.PaperColorIndex;_depth.Value=m.LayerDepths[Canvas.Layer];
            _visible[0].IsChecked=note.Header.LayerAVisible;_visible[1].IsChecked=note.Header.LayerBVisible;_visible[2].IsChecked=note.Header.LayerCVisible;
            _loop.IsChecked=note.Header.LoopPlayback;
            for(int i=0;i<4;i++)_effects[i].IsChecked=(m.SoundEffectFlags&(1<<i))!=0;
            _undo.IsEnabled=Document.CanUndo;_redo.IsEnabled=Document.CanRedo;
            _time.Text=$@"{TimeSpan.FromSeconds(Document.Frame/note.Header.FramesPerSecond):mm\:ss\.ff}  ·  {Document.Frame+1:D3} / {note.Header.FrameCount:D3}";
            _details.Text=$"320 × 240  ·  {note.Header.FramesPerSecond:0.#} fps  ·  {note.Header.FrameCount/note.Header.FramesPerSecond:0.00} s";
            if(_revision!=Document.Revision)
            { _revision=Document.Revision;_history.ItemsSource=Document.History;_author.Text=note.Header.CurrentAuthorName; }
            _signature.Text="工程保留对象 · 导出 KWZ 自动签名。";
            Canvas.InvalidateVisual();Timeline.Refresh();
        }
        finally{_sync=false;}
    }
    private void SelectTool(DrawingTool tool){Stop();Canvas.Tool=tool;foreach(var pair in _tools)pair.Value.IsChecked=pair.Key==tool;Refresh();Canvas.Focus();}
    private void Edit(string name,Action<KwzFile> action)=>Run(()=>{Stop();Document.Edit(name,action);_status.Text=name;});
    private void Remap(string label,List<int> map,int frame)=>Run(()=>{Stop();Document.RemapFrames(label,map,frame);});
    private List<int> FrameMap()=>Enumerable.Range(0,Document.Note.Header.FrameCount).ToList();
    private void Insert(){var map=FrameMap();map.Insert(Document.Frame,-1);Remap("插入空白帧",map,Document.Frame);}
    private void Duplicate(){var map=FrameMap();int at=Document.Frame+1;map.Insert(at,Document.Frame);Remap("复制当前帧",map,at);}
    private void Delete(){if(Document.Note.Header.FrameCount==1)throw new InvalidOperationException("至少保留一帧。");var map=FrameMap();map.RemoveAt(Document.Frame);Remap("删除当前帧",map,Math.Min(Document.Frame,map.Count-1));}
    private void Move(int direction){int target=Document.Frame+direction;if(target<0||target>=Document.Note.Header.FrameCount)return;var map=FrameMap();int source=map[Document.Frame];map.RemoveAt(Document.Frame);map.Insert(target,source);Remap("移动当前帧",map,target);}
    private void Step(int delta){Stop();Document.Seek(Document.Frame+delta);}
    private void TogglePlay()
    {
        if(_timer.IsEnabled){Stop();return;}
        if(Document.Frame==Document.Note.Header.FrameCount-1)Document.Seek(0);
        _startFrame=Document.Frame;
        _audio.Start(Document.Note,Document.Frame);
        _clock.Restart();_timer.Start();_play.Content="Ⅱ 暂停";
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
    private void Stop(){_timer.Stop();_clock.Stop();_audio.Stop();_play.Content="▶ 播放";}
    private async void OnKey(object? sender,KeyEventArgs e)
    {
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
            path??=await Pick("打开工程或 KWZ 动画",new FilePickerFileType("KWZ 工程与动画"){Patterns=["*.kwzproj","*.kwz"]});
            if(path==null||!await ConfirmDiscard())return;
            Document.Open(path);_status.Text="已打开 "+path;
        }catch(Exception error){await Error(error);}
    }
    private async Task Save(bool forceAs)
    {
        Stop();string? path=Document.Path;
        if(forceAs||!Document.IsProject||path==null)
        {
            var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="保存编辑工程",DefaultExtension="kwzproj",SuggestedFileName=(path==null?"新建动画":Path.GetFileNameWithoutExtension(path))+".kwzproj",FileTypeChoices=[new("KWZ Studio 工程"){Patterns=["*.kwzproj"]}],ShowOverwritePrompt=true});
            path=file?.TryGetLocalPath();if(path==null)return;
        }
        Document.SaveProject(path);_status.Text="工程已保存，文字和形状仍可编辑："+Path.GetFileName(path);
    }
    private async Task ExportKwz()
    {
        Stop();
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="导出游戏 KWZ",DefaultExtension="kwz",SuggestedFileName=(Document.Path==null?"新建动画":Path.GetFileNameWithoutExtension(Document.Path))+"_zh.kwz",FileTypeChoices=[new("KWZ 动画"){Patterns=["*.kwz"]}],ShowOverwritePrompt=true});
        string? path=file?.TryGetLocalPath();if(path==null)return;
        Document.ExportKwz(path);_status.Text="已导出并签名；工程对象保持可编辑："+Path.GetFileName(path);
    }
    private async Task<bool> ConfirmDiscard()
    {
        if(!Document.IsDirty||(!Document.CanUndo&&Document.Path==null))return true;
        string? answer=await Ask("未保存的修改","保存当前修改后再继续？",["保存","不保存","取消"]);
        if(answer=="不保存")return true;
        if(answer!="保存")return false;
        await Guard(()=>Save(false));return !Document.IsDirty;
    }
    private async Task<string?> Pick(string title,FilePickerFileType filter)
    { var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=title,AllowMultiple=false,FileTypeFilter=[filter]});return files.FirstOrDefault()?.TryGetLocalPath(); }
    private async Task ImportImage()
    {
        Stop();string? path=await Pick("导入到当前图层（按两种墨色量化）",new("图像"){Patterns=["*.png","*.bmp","*.jpg"]});if(path==null)return;
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
        Edit("导入图层图像",n=>n.FrameData.SetLayerPixels(Document.Frame,Canvas.Layer,layer));
    }
    private async Task ExportImage()
    {
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="导出当前画面",SuggestedFileName=$"frame_{Document.Frame+1:D3}.png",DefaultExtension="png",ShowOverwritePrompt=true});
        string? path=file?.TryGetLocalPath();if(path==null)return;
        using var flattened=Document.CreateFlattenedNote();
        var (layers,composite)=flattened.FrameData.FrameToImage(Document.Frame);
        try{await composite.SaveAsPngAsync(path);}finally{composite.Dispose();foreach(var layer in layers)layer.Dispose();}
        _status.Text="画面已导出。";
    }
    private async Task ImportAudio(int track)
    {
        Stop();string? path=await Pick("替换音轨：PCM WAV",new("WAV 音频"){Patterns=["*.wav"]});if(path==null)return;
        byte[] pcm=AudioPreview.ReadWav(path);
        Edit("替换音轨",n=>{if(n.Sound==null)throw new InvalidOperationException("此文件没有声音区段。");n.Sound.SetAudioTrack(track,pcm);if(track==0)n.Sound.Speed=n.Header.FrameSpeed;});
    }
    private async Task ExportAudio(int track)
    {
        if(Document.Note.Sound==null)throw new InvalidOperationException("此文件没有声音区段。");
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="导出音轨",SuggestedFileName="bgm.wav",DefaultExtension="wav",ShowOverwritePrompt=true});
        string? path=file?.TryGetLocalPath();if(path!=null)Utils.WriteWavFile(path,Document.Note.Sound.GetAudioTrack(track),KwzSound.SampleRate);
    }
    private void Run(Action action){try{action();}catch(Exception error){_ = Error(error);}}
    private async Task Guard(Func<Task> action){try{await action();}catch(Exception error){await Error(error);}}
    private Task Error(Exception error){_status.Text=error.Message;return Info("操作未完成",error.Message);}
    private async Task Info(string title,string message)=>await Ask(title,message,["确定"]);
    private async Task<string?> Ask(string title,string message,string[] choices)
    {
        var dialog=new ClassicWindow { Title=title,Width=450,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=FontFamily,FontSize=12 };
        var body=new StackPanel { Margin=new Thickness(18),Spacing=16 };
        body.Children.Add(new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap });
        var buttons=Row();buttons.HorizontalAlignment=HorizontalAlignment.Right;
        foreach(string choice in choices){var button=new Button{Content=choice,MinWidth=72,Padding=new Thickness(10,5)};button.Click+=(_,_)=>dialog.Close(choice);buttons.Children.Add(button);}
        body.Children.Add(buttons);dialog.Content=body;return await dialog.ShowDialog<string?>(this);
    }
}
