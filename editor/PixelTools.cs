using LibKwz;
using SkiaSharp;

namespace KwzStudio;

public enum DrawingTool { Pencil,Eraser,Fill,Rectangle,Text,Select,Ellipse }

public static class PixelTools
{
    public static void Line(byte[] pixels,int layer,int x0,int y0,int x1,int y1,int radius,byte ink)
    {
        int dx=Math.Abs(x1-x0),dy=-Math.Abs(y1-y0),sx=x0<x1?1:-1,sy=y0<y1?1:-1,error=dx+dy;
        while(true)
        {
            for(int y=y0-radius;y<=y0+radius;y++) for(int x=x0-radius;x<=x0+radius;x++)
                if((uint)x<320 && (uint)y<240 && (x-x0)*(x-x0)+(y-y0)*(y-y0)<=radius*radius+1)
                    pixels[layer*KwzLayerCodec.PixelCount+y*320+x]=ink;
            if(x0==x1 && y0==y1) break;
            int twice=2*error;
            if(twice>=dy) { error+=dy;x0+=sx; }
            if(twice<=dx) { error+=dx;y0+=sy; }
        }
    }
    public static void Fill(byte[] pixels,int layer,int x,int y,byte ink)
    {
        if((uint)x>=320 || (uint)y>=240) return;
        int offset=layer*KwzLayerCodec.PixelCount,start=y*320+x;
        byte old=pixels[offset+start]; if(old==ink) return;
        var queue=new Queue<int>(); queue.Enqueue(start); pixels[offset+start]=ink;
        void Add(int p) { if(pixels[offset+p]==old) { pixels[offset+p]=ink; queue.Enqueue(p); } }
        while(queue.TryDequeue(out int p))
        {
            int px=p%320,py=p/320;
            if(px>0) Add(p-1); if(px<319) Add(p+1); if(py>0) Add(p-320); if(py<239) Add(p+320);
        }
    }
    public static void Rectangle(byte[] pixels,int layer,int x0,int y0,int x1,int y1,byte ink)
    {
        for(int y=Math.Max(0,Math.Min(y0,y1));y<=Math.Min(239,Math.Max(y0,y1));y++)
        for(int x=Math.Max(0,Math.Min(x0,x1));x<=Math.Min(319,Math.Max(x0,x1));x++)
            pixels[layer*KwzLayerCodec.PixelCount+y*320+x]=ink;
    }
    public static void Text(byte[] pixels,int layer,int x,int y,string text,int size,byte ink,string font="Microsoft YaHei")
    {
        using var bitmap=new SKBitmap(320,240);
        using var canvas=new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
        using var typeface=SKTypeface.FromFamilyName(font);
        using var paint=new SKPaint { Typeface=typeface,TextSize=size,Color=SKColors.White,IsAntialias=false };
        float baseline=y-paint.FontMetrics.Ascent;
        foreach(string line in text.Replace("\r","").Split('\n'))
        { canvas.DrawText(line,x,baseline,paint); baseline+=size*1.25f; }
        for(int py=0;py<240;py++) for(int px=0;px<320;px++)
            if(bitmap.GetPixel(px,py).Alpha>=128) pixels[layer*KwzLayerCodec.PixelCount+py*320+px]=ink;
    }
}
