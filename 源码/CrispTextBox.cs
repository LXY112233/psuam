using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using I=System.Windows.Input;
namespace QingMove
{
    // WPF preserves editing, IME, selection, undo and accessibility, and supports OpenType.
    public class CrispTextBox:UserControl
    {
        readonly ElementHost host;
        readonly C.TextBox input;
        bool multiline;
        bool released;
        ScrollBars bars;
        public CrispTextBox()
        {
            TabStop=false;BackColor=Theme.Field;
            input=new C.TextBox(){BorderThickness=new W.Thickness(0),Padding=new W.Thickness(0),Margin=new W.Thickness(0),VerticalContentAlignment=W.VerticalAlignment.Center,Background=Brush(Theme.Field),Foreground=Brush(Theme.Ink),SelectionBrush=Brush(Theme.Blue)};
            M.TextOptions.SetTextFormattingMode(input,M.TextFormattingMode.Display);
            M.TextOptions.SetTextRenderingMode(input,M.TextRenderingMode.ClearType);
            input.UseLayoutRounding=true;input.SnapsToDevicePixels=true;
            host=new ElementHost(){Dock=DockStyle.Fill,Child=input,TabStop=true};Controls.Add(host);
            input.TextChanged+=delegate{OnTextChanged(EventArgs.Empty);};
            input.GotKeyboardFocus+=delegate{OnGotFocus(EventArgs.Empty);};input.LostKeyboardFocus+=delegate{OnLostFocus(EventArgs.Empty);};
            input.PreviewKeyDown+=delegate(object sender,I.KeyEventArgs e)
            {
                var key=e.Key==I.Key.System?e.SystemKey:e.Key;
                Keys keys=(Keys)I.KeyInterop.VirtualKeyFromKey(key);
                if((I.Keyboard.Modifiers&I.ModifierKeys.Control)!=0)keys|=Keys.Control;
                if((I.Keyboard.Modifiers&I.ModifierKeys.Shift)!=0)keys|=Keys.Shift;
                if((I.Keyboard.Modifiers&I.ModifierKeys.Alt)!=0)keys|=Keys.Alt;
                var args=new System.Windows.Forms.KeyEventArgs(keys);OnKeyDown(args);if(args.SuppressKeyPress||args.Handled)e.Handled=true;
            };
            UpdateFont();
        }
        static M.SolidColorBrush Brush(Color c){var b=new M.SolidColorBrush(M.Color.FromArgb(c.A,c.R,c.G,c.B));b.Freeze();return b;}
        public C.TextBox TextView {get{return input;}}
        public override string Text {get{return input==null?"":input.Text;}set{if(input!=null)input.Text=value??"";}}
        public int MaxLength {get{return input.MaxLength;}set{input.MaxLength=value;}}
        public bool ReadOnly {get{return input.IsReadOnly;}set{input.IsReadOnly=value;}}
        public int PreferredHeight {get{return (int)Math.Ceiling(Font.SizeInPoints*96.0/72.0*input.FontFamily.LineSpacing*DeviceScale)+2;}}
        float DeviceScale {get{return DeviceDpi/96f;}}
        public bool Multiline {get{return multiline;}set{multiline=value;input.AcceptsReturn=value;input.TextWrapping=value?W.TextWrapping.Wrap:W.TextWrapping.NoWrap;input.VerticalContentAlignment=value?W.VerticalAlignment.Top:W.VerticalAlignment.Center;}}
        public ScrollBars ScrollBars {get{return bars;}set{bars=value;input.VerticalScrollBarVisibility=value==ScrollBars.Vertical||value==ScrollBars.Both?C.ScrollBarVisibility.Auto:C.ScrollBarVisibility.Hidden;}}
        public HorizontalAlignment TextAlign {set{input.TextAlignment=value==HorizontalAlignment.Center?W.TextAlignment.Center:value==HorizontalAlignment.Right?W.TextAlignment.Right:W.TextAlignment.Left;}}
        public override bool Focused {get{return input!=null&&input.IsKeyboardFocusWithin;}}
        void UpdateFont(){if(input==null)return;input.FontFamily=new M.FontFamily(Font.Name);input.FontSize=Font.SizeInPoints*96.0/72.0;input.FontWeight=Font.Bold?W.FontWeights.SemiBold:W.FontWeights.Normal;}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);UpdateFont();}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(input!=null)input.IsEnabled=Enabled;}
        protected override void OnForeColorChanged(EventArgs e){base.OnForeColorChanged(e);if(input!=null)input.Foreground=Brush(ForeColor);}
        protected override void OnBackColorChanged(EventArgs e){base.OnBackColorChanged(e);if(input!=null)input.Background=Brush(BackColor);}
        protected override void Dispose(bool disposing){if(disposing&&!released){released=true;if(host!=null&&!host.IsDisposed&&host.Child!=null)host.Child=null;}base.Dispose(disposing);}
        public Bitmap RenderTextSnapshot()
        {
            input.UpdateLayout();float scale=DeviceScale;
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1,Width),Math.Max(1,Height),96*scale,96*scale,M.PixelFormats.Pbgra32);bitmap.Render(input);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var stream=new MemoryStream()){encoder.Save(stream);stream.Position=0;using(var image=Image.FromStream(stream))return new Bitmap(image);}
        }
    }
}
