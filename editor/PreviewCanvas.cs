using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LibKwz;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.PixelFormats;

namespace KwzStudio;

public sealed class PreviewCanvas : Control,IDisposable
{
    public EditorDocument Document { get; }
    public int Layer { get; set; }
    public byte Ink { get; set; }=1;
    public int BrushRadius { get; set; }=1;
    private DrawingTool _tool;
    public DrawingTool Tool { get=>_tool; set { CancelInteraction();_tool=value;if(value is DrawingTool.Pencil or DrawingTool.Eraser or DrawingTool.Fill)Document.SelectObject(null);InvalidateVisual(); } }
    public bool Onion { get; set; }
    public bool GridLines { get; set; }
    public double Zoom { get; set; }=0;
    public string StampText { get; set; }=L.T("Text");
    public int TextSize { get; set; }=24;
    public bool ShapeFilled { get; set; }=true;
    public int ShapeStrokeWidth { get; set; }=1;
    public Rect ImageRect=>_imageRect;
    public event Action<string>? Status;
    private readonly WriteableBitmap _bitmap=new(new PixelSize(320,240),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
    private byte[]? _stroke;
    private Point _start,_previous;
    private Rect _imageRect;
    private CanvasObject? _objectOriginal,_objectPreview;
    private bool _creating,_resizing;
    private int _gestureFrame,_gestureRevision;
    private IPointer? _pointer;
    private bool ObjectTool=>Tool is DrawingTool.Select or DrawingTool.Text or DrawingTool.Rectangle or DrawingTool.Ellipse;
    public PreviewCanvas(EditorDocument document)
    {
        Document=document; Focusable=true; ClipToBounds=true;
        RenderOptions.SetBitmapInterpolationMode(this,BitmapInterpolationMode.None);
        Cursor=new Cursor(StandardCursorType.Cross);
        Document.Changed+=DocumentChanged;
    }
    private void DocumentChanged()
    {
        if((_stroke!=null || _objectPreview!=null) && (_gestureFrame!=Document.Frame || _gestureRevision!=Document.Revision))CancelInteraction();
        InvalidateVisual();
    }
    public static byte[] Composite(EditorDocument doc,byte[] pixels)
    {
        var note=doc.Note; var meta=note.Meta.Entries[doc.Frame];
        Rgba32[] palette=KwzMeta.FramePalette.Select(c=>c.ToPixel<Rgba32>()).ToArray();
        byte[] inks=[meta.LayerAFirstColorIndex,meta.LayerASecondColorIndex,meta.LayerBFirstColorIndex,meta.LayerBSecondColorIndex,meta.LayerCFirstColorIndex,meta.LayerCSecondColorIndex];
        int[] order=new[]{2,1,0}.OrderByDescending(i=>meta.LayerDepths[i]).ToArray();
        bool[] visible=[note.Header.LayerAVisible,note.Header.LayerBVisible,note.Header.LayerCVisible];
        byte[] bgra=new byte[320*240*4];
        for(int p=0;p<320*240;p++)
        {
            Rgba32 color=palette[meta.PaperColorIndex];
            foreach(int layer in order)
            {
                byte ink=pixels[layer*KwzLayerCodec.PixelCount+p];
                if(visible[layer] && ink!=0)
                {
                    var candidate=palette[inks[layer*2+ink-1]];
                    if(candidate.A>0) color=candidate;
                }
            }
            if(color.A==0) color=(p%320/8+p/320/8)%2==0?new Rgba32(230,230,230):new Rgba32(255,255,255);
            bgra[p*4]=color.B;bgra[p*4+1]=color.G;bgra[p*4+2]=color.R;bgra[p*4+3]=255;
        }
        return bgra;
    }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse("#55585C")),new Rect(Bounds.Size));
        double fit=Math.Min((Bounds.Width-44)/320,(Bounds.Height-44)/240);
        double scale=Zoom<=0?Math.Max(.25,fit):Zoom;
        _imageRect=new Rect((Bounds.Width-320*scale)/2,(Bounds.Height-240*scale)/2,320*scale,240*scale);
        byte[] pixels=_stroke!=null || _objectPreview!=null?Document.RenderFrame(Document.Frame,_objectPreview,_stroke??Document.BasePixels):Document.Pixels;
        byte[] data=Composite(Document,pixels);
        if(Onion && Document.Frame>0)
        {
            byte[] previous=Document.RenderFrame(Document.Frame-1);
            for(int i=0;i<320*240;i++) if(previous[Layer*320*240+i]!=0 && pixels[Layer*320*240+i]==0)
            { data[i*4]=(byte)((data[i*4]+240)/2);data[i*4+1]=(byte)((data[i*4+1]+150)/2);data[i*4+2]=(byte)((data[i*4+2]+60)/2); }
        }
        using(var locked=_bitmap.Lock()) Marshal.Copy(data,0,locked.Address,data.Length);
        context.FillRectangle(Brushes.Black,_imageRect.Inflate(1));
        context.DrawImage(_bitmap,new Rect(0,0,320,240),_imageRect);
        if(GridLines && scale>=2)
        {
            var pen=new Pen(new SolidColorBrush(Color.FromArgb(55,0,0,0)),.5);
            for(int x=0;x<=320;x+=8) context.DrawLine(pen,new Point(_imageRect.X+x*scale,_imageRect.Y),new Point(_imageRect.X+x*scale,_imageRect.Bottom));
            for(int y=0;y<=240;y+=8) context.DrawLine(pen,new Point(_imageRect.X,_imageRect.Y+y*scale),new Point(_imageRect.Right,_imageRect.Y+y*scale));
        }
        var selected=_objectPreview??Document.SelectedObject;
        if(selected!=null && ObjectTool)
        {
            Rect rect=ScreenRect(selected);
            context.DrawRectangle(null,new Pen(Brushes.White,3),rect);
            context.DrawRectangle(null,new Pen(Brushes.DodgerBlue,1),rect);
            foreach(Point corner in new[]{rect.TopLeft,rect.TopRight,rect.BottomLeft,rect.BottomRight})
                context.DrawRectangle(Brushes.White,new Pen(Brushes.DodgerBlue,1),new Rect(corner.X-3,corner.Y-3,6,6));
            context.DrawRectangle(Brushes.DodgerBlue,new Pen(Brushes.White,1),ResizeHandle(selected));
        }
    }
    private Rect ScreenRect(CanvasObject item)=>new(_imageRect.X+item.X*_imageRect.Width/320,_imageRect.Y+item.Y*_imageRect.Height/240,item.Width*_imageRect.Width/320,item.Height*_imageRect.Height/240);
    private Rect ResizeHandle(CanvasObject item) { var r=ScreenRect(item);return new Rect(r.Right-5,r.Bottom-5,10,10); }
    private Point Pixel(Point point)=>new((int)Math.Floor((point.X-_imageRect.X)/_imageRect.Width*320+1e-7),(int)Math.Floor((point.Y-_imageRect.Y)/_imageRect.Height*240+1e-7));
    private CanvasObject? HitObject(Point pixel)
    {
        var meta=Document.Note.Meta.Entries[Document.Frame];
        bool[] visible=[Document.Note.Header.LayerAVisible,Document.Note.Header.LayerBVisible,Document.Note.Header.LayerCVisible];
        return Document.Objects.Select((item,index)=>(item,index)).Where(o=>o.item.Frame==Document.Frame && visible[o.item.Layer])
            .OrderBy(o=>meta.LayerDepths[o.item.Layer]).ThenBy(o=>o.item.Layer).ThenByDescending(o=>o.index)
            .Select(o=>o.item).FirstOrDefault(o=>o.Bounds.Inflate(1).Contains(pixel));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Point position=e.GetPosition(this);
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        bool resize=ObjectTool && Document.SelectedObject is { } selected && ResizeHandle(selected).Inflate(3).Contains(position);
        if(!resize && !_imageRect.Contains(position))return;
        Focus();CancelInteraction();_start=_previous=Pixel(position);
        _gestureFrame=Document.Frame;_gestureRevision=Document.Revision;
        if(ObjectTool)
        {
            var hit=resize?Document.SelectedObject:HitObject(_start);
            if(hit!=null) { Document.SelectObject(hit.Id);Layer=hit.Layer;_objectOriginal=_objectPreview=hit;_resizing=resize; }
            else
            {
                Document.SelectObject(null);
                if(Tool==DrawingTool.Select) { e.Handled=true;return; }
                _creating=true;
                _objectPreview=new CanvasObject { Frame=Document.Frame,Layer=Layer,X=(int)_start.X,Y=(int)_start.Y,Ink=Ink,
                    Kind=Tool==DrawingTool.Text?ObjectKind.Text:Tool==DrawingTool.Ellipse?ObjectKind.Ellipse:ObjectKind.Rectangle,
                    Text=StampText,FontSize=TextSize,Width=1,Height=1,Filled=ShapeFilled,StrokeWidth=ShapeStrokeWidth }.MeasureText();
            }
        }
        else { Document.SelectObject(null);_stroke=(byte[])Document.BasePixels.Clone();Apply(_previous,_previous); }
        _pointer=e.Pointer;e.Pointer.Capture(this);InvalidateVisual();e.Handled=true;
    }
    private void Apply(Point from,Point to)
    {
        if(_stroke==null) return;
        switch(Tool)
        {
            case DrawingTool.Pencil: case DrawingTool.Eraser:
                PixelTools.Line(_stroke,Layer,(int)from.X,(int)from.Y,(int)to.X,(int)to.Y,BrushRadius,Tool==DrawingTool.Eraser?(byte)0:Ink);break;
            case DrawingTool.Fill: PixelTools.Fill(_stroke,Layer,(int)to.X,(int)to.Y,Ink);break;
        }
    }
    private void UpdateObjectPreview(Point p)
    {
        if(_objectPreview==null)return;
        if(_creating && _objectPreview.Kind!=ObjectKind.Text)
            _objectPreview=_objectPreview with { X=(int)Math.Min(_start.X,p.X),Y=(int)Math.Min(_start.Y,p.Y),Width=Math.Clamp((int)Math.Abs(p.X-_start.X)+1,1,16384),Height=Math.Clamp((int)Math.Abs(p.Y-_start.Y)+1,1,16384) };
        else if(_objectOriginal is { } item)
        {
            if(_resizing)
            {
                if(item.Kind==ObjectKind.Text)
                    _objectPreview=(item with { FontSize=Math.Clamp((int)Math.Round(item.FontSize*Math.Max(1,p.Y-item.Y)/item.Height),6,256) }).MeasureText();
                else _objectPreview=item with { Width=Math.Clamp((int)p.X-item.X,1,16384),Height=Math.Clamp((int)p.Y-item.Y,1,16384) };
            }
            else _objectPreview=item with { X=Math.Clamp(item.X+(int)(p.X-_start.X),1-item.Width,319),Y=Math.Clamp(item.Y+(int)(p.Y-_start.Y),1-item.Height,239) };
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p=Pixel(e.GetPosition(this)); Status?.Invoke(L.F("X {0} Y {1} · Layer {2}",p.X.ToString("000"),p.Y.ToString("000"),(char)('A'+Layer)));
        if(_stroke==null && _objectPreview==null)
        {
            Cursor=new Cursor(ObjectTool && Document.SelectedObject is { } selected && ResizeHandle(selected).Inflate(3).Contains(e.GetPosition(this))?StandardCursorType.BottomRightCorner:
                ObjectTool && HitObject(p)!=null?StandardCursorType.SizeAll:Tool==DrawingTool.Select?StandardCursorType.Arrow:StandardCursorType.Cross);
            return;
        }
        if(_stroke!=null && Tool is DrawingTool.Pencil or DrawingTool.Eraser)Apply(_previous,p);
        UpdateObjectPreview(p);
        _previous=p; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if(_stroke==null && _objectPreview==null)return;
        UpdateObjectPreview(Pixel(e.GetPosition(this)));
        var pixels=_stroke;var item=_objectPreview;bool create=_creating;bool resize=_resizing;int frame=_gestureFrame;
        CancelInteraction();
        try
        {
            if(item!=null) { if(create)Document.AddObject(item);else Document.UpdateObject(item,resize?"Resize object":"Move object"); }
            else if(pixels!=null)Document.Edit(Tool==DrawingTool.Eraser?"Erase pixels":Tool==DrawingTool.Fill?"Fill area":"Draw pixels",n=>n.FrameData.SetFramePixels(frame,pixels));
        }
        catch(Exception error) { Status?.Invoke(error.Message); }
        InvalidateVisual();e.Handled=true;
    }
    public void CancelInteraction()
    {
        _stroke=null;_objectPreview=_objectOriginal=null;_creating=_resizing=false;
        var pointer=_pointer;_pointer=null;pointer?.Capture(null);InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { CancelInteraction();base.OnPointerCaptureLost(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Escape) { CancelInteraction();Document.SelectObject(null);e.Handled=true;return; }
        if(Document.SelectedObject is { } item && ObjectTool)
        {
            int step=e.KeyModifiers.HasFlag(KeyModifiers.Shift)?10:1;
            if(e.Key==Key.Delete)Document.DeleteSelectedObject();
            else if(e.Key==Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control))Document.DuplicateSelectedObject();
            else if(e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
                Document.UpdateObject(item with { X=Math.Clamp(item.X+(e.Key==Key.Left?-step:e.Key==Key.Right?step:0),-16384,16384),Y=Math.Clamp(item.Y+(e.Key==Key.Up?-step:e.Key==Key.Down?step:0),-16384,16384) },"Nudge object");
            else { base.OnKeyDown(e);return; }
            e.Handled=true;return;
        }
        base.OnKeyDown(e);
    }
    public void Dispose() { Document.Changed-=DocumentChanged;_bitmap.Dispose(); }
}
