using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Globalization;

namespace KwzStudio;

public sealed class TimelineControl : Control
{
    public EditorDocument Document { get; }
    public double FrameWidth { get; set; }=28;
    public int Layer { get; set; }
    public event Action<int>? LayerSelected;
    public event Action? ScrubStarted;
    private bool _drag;
    private int _revision=-1;
    private float[] _wave=[];
    private double _audioSeconds;
    private static readonly IBrush[] Tracks=[new SolidColorBrush(Color.Parse("#C0D4ED")),new SolidColorBrush(Color.Parse("#C7DECA")),new SolidColorBrush(Color.Parse("#D4CEE6"))];
    public TimelineControl(EditorDocument document) { Document=document;Height=210;Focusable=true;HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left; }
    public void Refresh()
    {
        Width=Math.Max(600,Document.Note.Header.FrameCount*FrameWidth+12);
        if(_revision!=Document.Revision)
        {
            _revision=Document.Revision;
            byte[] pcm=Document.Note.Sound?.GetAudioTrack(0)??[];
            uint speed=Document.Note.Sound?.Speed??Document.Note.Header.FrameSpeed;
            double recorded=speed<=10?new double[]{.2,.5,1,2,4,6,8,12,20,24,30}[speed]:Document.Note.Header.FramesPerSecond;
            _audioSeconds=pcm.Length/2d/LibKwz.KwzSound.SampleRate*recorded/Document.Note.Header.FramesPerSecond;
            _wave=new float[Math.Min(1600,pcm.Length/2)];
            for(int i=0;i<_wave.Length;i++)
            {
                int from=i*(pcm.Length/2)/_wave.Length,to=(i+1)*(pcm.Length/2)/_wave.Length;
                int peak=0; for(int p=from;p<to;p++) peak=Math.Max(peak,Math.Abs((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(p*2))));
                _wave[i]=peak/32768f;
            }
        }
        InvalidateVisual();
    }
    private static void Label(DrawingContext c,string text,double x,double y,IBrush? brush=null,double size=11)
        =>c.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,brush??Brushes.Black),new Point(x,y));
    public override void Render(DrawingContext c)
    {
        c.FillRectangle(new SolidColorBrush(Color.Parse("#E8E6E0")),new Rect(Bounds.Size));
        int frames=Document.Note.Header.FrameCount;
        for(int i=0;i<frames;i++)
        {
            double x=i*FrameWidth;
            if(i%Math.Max(1,(int)(45/FrameWidth))==0) { Label(c,(i+1).ToString(),x+4,5); c.DrawLine(new Pen(Brushes.Gray),new Point(x,21),new Point(x,28)); }
            var m=Document.Note.Meta.Entries[i];
            for(int layer=0;layer<3;layer++)
            {
                var rect=new Rect(x+1,29+layer*40,FrameWidth-2,36);
                c.FillRectangle(i==Document.Frame && layer==Layer?new SolidColorBrush(Color.Parse("#7196C3")):Tracks[layer],rect);
                c.DrawRectangle(null,new Pen(new SolidColorBrush(Color.Parse("#9199A0")),.6),rect);
                bool independent=layer switch {0=>m.LayerADiffingFlag,1=>m.LayerBDiffingFlag,_=>m.LayerCDiffingFlag};
                if(FrameWidth>=20) Label(c,independent?"◆":"·",x+FrameWidth/2-5,39+layer*40,Brushes.DimGray);
            }
            if(m.SoundEffectFlags!=0) Label(c,"♪",x+2,185,new SolidColorBrush(Color.Parse("#704A1B")));
        }
        c.FillRectangle(new SolidColorBrush(Color.Parse("#DCE5D2")),new Rect(0,150,frames*FrameWidth,32));
        // The waveform is an overview of the BGM; timing uses the recording frame-rate ratio.
        if(_wave.Length>0)
        {
            double width=_audioSeconds*Document.Note.Header.FramesPerSecond*FrameWidth;
            var pen=new Pen(new SolidColorBrush(Color.Parse("#577545")),1);
            for(int i=0;i<_wave.Length;i++) { double x=i*width/_wave.Length;if(x>frames*FrameWidth)break;c.DrawLine(pen,new Point(x,166-_wave[i]*13),new Point(x,166+_wave[i]*13)); }
        }
        double playhead=Document.Frame*FrameWidth;
        c.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#B71C1C")),2),new Point(playhead+1,0),new Point(playhead+1,210));
        c.FillRectangle(new SolidColorBrush(Color.Parse("#B71C1C")),new Rect(playhead-4,0,10,4));
    }
    private void Seek(PointerEventArgs e)
    {
        var p=e.GetPosition(this); Document.Seek((int)(p.X/FrameWidth));
        if(p.Y>=29 && p.Y<149) { Layer=Math.Clamp((int)((p.Y-29)/40),0,2);LayerSelected?.Invoke(Layer); }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        ScrubStarted?.Invoke(); Focus();_drag=true;e.Pointer.Capture(this);Seek(e);e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if(_drag) Seek(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag=false;e.Pointer.Capture(null); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { _drag=false; }
}
