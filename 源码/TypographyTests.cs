using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using QingMove;
class TypographyTests
{
    static int count;
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
    static int Ink(Bitmap b,int top,int bottom){int n=0;for(int y=top;y<bottom;y++)for(int x=0;x<b.Width;x++)if(b.GetPixel(x,y).R<220)n++;return n;}
    static Bitmap Surface(int width,int height,int dpi){var b=new Bitmap(width,height);b.SetResolution(dpi,dpi);return b;}
    [STAThread]static void Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string folder=args.Length>0?args[0]:"typography-previews";Directory.CreateDirectory(folder);
            int priorWidth=0;
            foreach(int dpi in new[]{96,120,144,192})
            {
                float scale=dpi/96f;
                using(var bitmap=Surface((int)(760*scale),(int)(200*scale),dpi))
                using(var g=Graphics.FromImage(bitmap))using(var font=Theme.Font(12))using(var title=Theme.Bold(21))
                {
                    Check(Math.Abs(g.DpiY-dpi)<.1,"drawing surface reports native DPI: "+dpi);g.Clear(Color.White);
                    var measured=Typography.MeasureText("提醒规则 Aa 09:30",font,bitmap.Width,dpi);
                    Check(measured.Width>priorWidth,"text measured at native DPI: "+dpi);priorWidth=measured.Width;
                    Typography.DrawText(g,"提醒规则 · "+(int)(scale*100)+"%",title,new Rectangle((int)(20*scale),(int)(16*scale),(int)(700*scale),(int)(50*scale)),Theme.Ink,TextFormatFlags.SingleLine);
                    Typography.DrawText(g,"放下手边的工作，站起来伸个懒腰。",font,new Rectangle((int)(20*scale),(int)(75*scale),(int)(700*scale),(int)(35*scale)),Theme.Ink,TextFormatFlags.SingleLine);
                    Typography.DrawText(g,"09:30  Aa  0123456789  ·  高清矢量字形",font,new Rectangle((int)(20*scale),(int)(120*scale),(int)(700*scale),(int)(35*scale)),Theme.Muted,TextFormatFlags.SingleLine);
                    int edges=0;for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var pixel=bitmap.GetPixel(x,y);if(Math.Abs(pixel.R-pixel.G)<4&&Math.Abs(pixel.G-pixel.B)<4&&pixel.R>40&&pixel.R<180)edges++;}
                    Check(edges>150*scale,"grayscale antialias coverage at "+dpi+" DPI");
                    Check(Ink(bitmap,0,bitmap.Height)>300*scale,"nonempty vector glyphs at "+dpi+" DPI");
                    bitmap.Save(Path.Combine(folder,"字体_"+(int)(scale*100)+"%.png"));
                }
            }
            using(var bitmap=new Bitmap(260,100))using(var g=Graphics.FromImage(bitmap))using(var font=Theme.Font(12))
            {
                g.Clear(Color.White);g.SmoothingMode=SmoothingMode.None;var before=g.Transform.Elements;
                Typography.DrawText(g,"第一行提醒\n第二行提醒",font,new Rectangle(10,10,220,80),Color.Black,TextFormatFlags.WordBreak|TextFormatFlags.VerticalCenter);
                Check(Ink(bitmap,10,50)>30&&Ink(bitmap,50,90)>30,"vertically centered multiline text retains both lines");
                Check(g.SmoothingMode==SmoothingMode.None&&g.Transform.Elements[4]==before[4],"text drawing preserves graphics state");
                Check(bitmap.GetPixel(1,30).ToArgb()==Color.White.ToArgb(),"text respects clipping bounds");
            }
            using(var form=new Form(){Size=new Size(480,220)})using(var edit=new CrispTextBox(){Bounds=new Rectangle(20,20,400,60),Font=Theme.Font(12),Multiline=true})
            {
                form.Controls.Add(edit);form.Show();Application.DoEvents();
                Console.WriteLine("DISPLAY DPI="+form.DeviceDpi+" editor DPI="+edit.DeviceDpi);
                int changes=0;edit.TextChanged+=delegate{changes++;};edit.Text="中文输入，English 09:30\n第二行";
                Check(edit.TextView.Text==edit.Text&&changes==1,"Unicode and multiline editing propagate one change");
                edit.TextView.Select(0,2);edit.TextView.SelectedText="矢量";
                Check(edit.Text.StartsWith("矢量输入"),"selection replacement preserves Unicode text");
                edit.MaxLength=60;Check(edit.TextView.MaxLength==60,"length limit forwarded to real editor");
                edit.Enabled=false;Check(!edit.TextView.IsEnabled,"disabled state forwarded");edit.Enabled=true;
                Check(edit.TextView.FontFamily.Source==edit.Font.Name,"editor and labels use the same font family");
                Check(edit.TextView.AcceptsReturn&&edit.TextView.TextWrapping==System.Windows.TextWrapping.Wrap,"multiline editor supports return and wrapping");
                edit.TextView.UpdateLayout();using(var snapshot=edit.RenderTextSnapshot())Check(Ink(snapshot,0,snapshot.Height)>50,"WPF text snapshot contains actual glyphs");
                form.Close();
            }
            using(var form=new Form())using(var step=new StepperBox(){Minimum=1,Maximum=120})
            {
                form.Controls.Add(step);form.Show();Application.DoEvents();step.Value=7;
                var editor=(CrispTextBox)step.Controls[0];
                var key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(editor.TextView),0,System.Windows.Input.Key.Up){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};
                editor.TextView.RaiseEvent(key);
                Check(step.Value==8&&key.Handled,"WPF Up key reaches stepper and suppresses caret movement");
                var down=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(editor.TextView),0,System.Windows.Input.Key.Down){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};
                editor.TextView.RaiseEvent(down);Check(step.Value==7&&down.Handled,"WPF Down key reaches stepper");form.Close();
            }
            var settings=new Settings(){DurationSeconds=0};
            using(var toast=new ToastForm(settings,true,delegate{}))
            {
                toast.Show();Application.DoEvents();
                foreach(Control c in toast.Controls)if(c is Panel)foreach(Control child in c.Controls)if(child is CrispLabel)
                {Console.WriteLine("LAYOUT "+child.Text.Replace("\n","/")+" child="+child.Size+" parent="+c.ClientSize);Check(Typography.MeasureText(child.Text,child.Font,child.Width,toast.DeviceDpi).Height<=child.Height,"popup label fits its actual DPI text metrics");Check(child.Height<=c.ClientSize.Height||((Panel)c).VerticalScroll.Visible,"popup text has space or an accessible scrollbar");}
                toast.ClientSize=new Size(toast.ClientSize.Width+80,toast.ClientSize.Height+60);Application.DoEvents();
                Check(toast.Region.IsVisible(toast.Width-30,toast.Height-30),"popup region expands with window instead of clipping buttons");
                toast.ReplaceClose();
            }
            Console.WriteLine("ALL "+count+" TYPOGRAPHY CHECKS PASSED");
        }
        catch(Exception ex){Console.WriteLine("FAIL "+ex);Environment.ExitCode=1;}
    }
}
