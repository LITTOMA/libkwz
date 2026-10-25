using Avalonia;
using LibKwz;
using SkiaSharp;

namespace KwzStudio;

public enum ObjectKind { Text,Rectangle,Ellipse }

/// <summary>Immutable editable overlay in the original 320 × 240 image coordinates.</summary>
public sealed record CanvasObject
{
    public Guid Id { get; init; }=Guid.NewGuid();
    public int Frame { get; init; }
    public int Layer { get; init; }
    public ObjectKind Kind { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }=80;
    public int Height { get; init; }=40;
    public byte Ink { get; init; }=1;
    public string Text { get; init; }="中文汉化";
    public int FontSize { get; init; }=24;
    public string FontFamily { get; init; }="Microsoft YaHei";
    public bool Filled { get; init; }=true;
    public int StrokeWidth { get; init; }=1;
    [System.Text.Json.Serialization.JsonIgnore] public Rect Bounds=>new(X,Y,Width,Height);
    [System.Text.Json.Serialization.JsonIgnore] public string Label=>$"{(char)('A'+Layer)} · {(Kind==ObjectKind.Text?Text.Replace('\n',' '):Kind==ObjectKind.Rectangle?"矩形":"椭圆")}";
    public CanvasObject MeasureText()
    {
        if(Kind!=ObjectKind.Text)return this;
        using var face=SKTypeface.FromFamilyName(FontFamily);
        using var paint=new SKPaint { Typeface=face,TextSize=FontSize };
        string[] lines=Text.Replace("\r","").Split('\n');
        return this with { Width=Math.Max(1,(int)Math.Ceiling(lines.Max(line=>paint.MeasureText(line)))),
            Height=Math.Max(1,(int)Math.Ceiling(-paint.FontMetrics.Ascent+paint.FontMetrics.Descent+(lines.Length-1)*FontSize*1.25f)) };
    }
    public void Draw(byte[] pixels)
    {
        using var bitmap=new SKBitmap(320,240);
        using var canvas=new SKCanvas(bitmap);
        using var paint=new SKPaint { Color=SKColors.White,IsAntialias=false,StrokeWidth=StrokeWidth,Style=Filled?SKPaintStyle.Fill:SKPaintStyle.Stroke };
        canvas.Clear(SKColors.Transparent);
        if(Kind==ObjectKind.Text)
        {
            using var face=SKTypeface.FromFamilyName(FontFamily);
            paint.Typeface=face;paint.TextSize=FontSize;paint.Style=SKPaintStyle.Fill;
            float baseline=Y-paint.FontMetrics.Ascent;
            foreach(string line in Text.Replace("\r","").Split('\n')) { canvas.DrawText(line,X,baseline,paint);baseline+=FontSize*1.25f; }
        }
        else
        {
            float inset=Filled?0:Math.Min(StrokeWidth/2f,Math.Min(Width,Height)/2f);
            var rect=new SKRect(X+inset,Y+inset,X+Width-inset,Y+Height-inset);
            if(Kind==ObjectKind.Rectangle)canvas.DrawRect(rect,paint);else canvas.DrawOval(rect,paint);
        }
        var mask=bitmap.Pixels;int offset=Layer*KwzLayerCodec.PixelCount;
        for(int p=0;p<mask.Length;p++)if(mask[p].Alpha>=128)pixels[offset+p]=Ink;
    }
    public void Validate(int frames)
    {
        if(Id==Guid.Empty || Frame<0 || Frame>=frames || Layer is <0 or >2 || !Enum.IsDefined(Kind) ||
            X is <-16384 or >16384 || Y is <-16384 or >16384 || Width is <1 or >16384 || Height is <1 or >16384 ||
            Ink>2 || FontSize is <6 or >256 || StrokeWidth is <1 or >64 || Text==null || Text.Length>10000 ||
            string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length>256)
            throw new InvalidDataException("文字或形状对象的数据无效。");
    }
}
