using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LibKwz;
using SixLabors.ImageSharp.PixelFormats;

namespace KwzStudio;

/// <summary>Palette choices rendered inside the existing Classic theme's toggle buttons.</summary>
public sealed class ColorSwatchPicker : UserControl
{
    private static readonly string[] ColorNames=["白","黑","红","黄","绿","蓝","透明"];
    private readonly ToggleButton[] _buttons;
    private readonly ColorChip[] _chips;
    private readonly string[]? _labels;
    private int _selectedIndex=-1;
    public event EventHandler? SelectionChanged;
    public int SelectedIndex
    {
        get=>_selectedIndex;
        set
        {
            // Preserve an existing unusual palette index when loading a note; only the visible choices are editable.
            if(_selectedIndex==value)return;
            _selectedIndex=value;RefreshSelection();SelectionChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public ColorSwatchPicker(int[] colors,string[]? labels=null)
    {
        if(colors.Length==0 || (labels!=null && labels.Length!=colors.Length))throw new ArgumentException("Invalid color choices.");
        _labels=labels;_buttons=new ToggleButton[colors.Length];_chips=new ColorChip[colors.Length];
        var row=new StackPanel { Orientation=Orientation.Horizontal,Spacing=2 };
        for(int i=0;i<colors.Length;i++)
        {
            int index=i;
            var chip=new ColorChip { Width=16,Height=18 };
            var button=new ToggleButton { Name="Swatch"+i,Padding=new Thickness(3),MinWidth=0,MinHeight=0,Height=28 };
            if(labels==null) { button.Width=24;button.Content=chip; }
            else
            {
                button.Width=74;
                var content=new StackPanel { Orientation=Orientation.Horizontal,Spacing=3,VerticalAlignment=VerticalAlignment.Center };
                content.Children.Add(chip);content.Children.Add(new TextBlock { Text=labels[i],VerticalAlignment=VerticalAlignment.Center });button.Content=content;
            }
            button.Click+=(_,_)=>{SelectedIndex=index;RefreshSelection();};
            button.KeyDown+=(_,e)=>
            {
                int next=e.Key switch { Key.Left or Key.Up=>(index+_buttons.Length-1)%_buttons.Length,Key.Right or Key.Down=>(index+1)%_buttons.Length,Key.Home=>0,Key.End=>_buttons.Length-1,_=>-1 };
                if(next<0)return;
                SelectedIndex=next;_buttons[next].Focus();e.Handled=true;
            };
            _buttons[i]=button;_chips[i]=chip;row.Children.Add(button);
        }
        Content=row;SetColors(colors);
    }
    public void SetColors(params int[] colors)
    {
        if(colors.Length!=_chips.Length)throw new ArgumentException("Color choice count changed.");
        for(int i=0;i<colors.Length;i++)
        {
            int index=colors[i];if((uint)index>=KwzMeta.FramePalette.Length)throw new ArgumentOutOfRangeException(nameof(colors));
            Rgba32 rgba=KwzMeta.FramePalette[index].ToPixel<Rgba32>();
            _chips[i].Color=Color.FromArgb(rgba.A,rgba.R,rgba.G,rgba.B);_chips[i].InvalidateVisual();
            string description=ColorNames[index]+(rgba.A==0?"":$"  #{rgba.R:X2}{rgba.G:X2}{rgba.B:X2}");
            string tip=_labels==null?description:$"{_labels[i]} · {description}";
            AutomationProperties.SetName(_buttons[i],tip);ToolTip.SetTip(_buttons[i],tip);
        }
    }
    private void RefreshSelection()
    {
        for(int i=0;i<_buttons.Length;i++)
        {
            bool selected=i==_selectedIndex;_buttons[i].IsChecked=selected;
            _chips[i].Selected=selected;_chips[i].InvalidateVisual();
        }
    }
    private sealed class ColorChip : Control
    {
        public Color Color { get; set; }
        public bool Selected { get; set; }
        public override void Render(DrawingContext context)
        {
            var rect=new Rect(Bounds.Size);
            if(Color.A==0)
            {
                context.FillRectangle(Brushes.White,rect);
                for(int y=0;y<Bounds.Height;y+=4)for(int x=0;x<Bounds.Width;x+=4)
                    if((x/4+y/4)%2==0)context.FillRectangle(Brushes.LightGray,new Rect(x,y,Math.Min(4,Bounds.Width-x),Math.Min(4,Bounds.Height-y)));
            }
            else context.FillRectangle(new SolidColorBrush(Color),rect);
            context.DrawRectangle(new Pen(Brushes.Gray,1),rect.Deflate(.5));
            if(Selected)
            {
                var ink=Color.A>0 && (Color.R*299+Color.G*587+Color.B*114)<128000?Brushes.White:Brushes.Black;
                var pen=new Pen(ink,2);double y=Bounds.Height/2;
                context.DrawLine(pen,new Point(3,y),new Point(6,y+3));context.DrawLine(pen,new Point(6,y+3),new Point(13,y-4));
            }
        }
    }
}
