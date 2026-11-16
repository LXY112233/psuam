using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
namespace QingMove
{
    public class FieldTextBox:UserControl
    {
        protected readonly CrispTextBox editor;
        bool arranging;
        public FieldTextBox()
        {
            DoubleBuffered=true;BackColor=Color.White;Size=new Size(200,36);TabStop=false;
            editor=new CrispTextBox(){BackColor=Theme.Field,ForeColor=Theme.Ink,Font=Theme.Font(10)};
            Controls.Add(editor);editor.TextChanged+=delegate{OnTextChanged(EventArgs.Empty);};
            editor.GotFocus+=delegate{Invalidate();};editor.LostFocus+=delegate{Invalidate();};LayoutEditor();
        }
        public override string Text {get{return editor==null?"":editor.Text;}set{if(editor!=null)editor.Text=value;}}
        public int MaxLength {get{return editor.MaxLength;}set{editor.MaxLength=value;}}
        public bool Multiline {get{return editor.Multiline;}set{editor.Multiline=value;LayoutEditor();}}
        public ScrollBars ScrollBars {get{return editor.ScrollBars;}set{editor.ScrollBars=value;}}
        protected override void OnResize(EventArgs e){base.OnResize(e);if(editor!=null)LayoutEditor();}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(editor!=null){editor.Font=Font;LayoutEditor();}}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(editor!=null){editor.Enabled=Enabled;Invalidate();}}
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(editor!=null)LayoutEditor();}
        void LayoutEditor(){if(arranging)return;arranging=true;try{float scale=DeviceDpi/96f;int pad=(int)Math.Round(10*scale);editor.Location=new Point(pad,editor.Multiline?(int)(9*scale):Math.Max(3,(Height-editor.PreferredHeight)/2));editor.Width=Math.Max(10,Width-pad*2);editor.Height=editor.Multiline?Math.Max(10,Height-(int)(18*scale)):editor.PreferredHeight;}finally{arranging=false;}}
        protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);LayoutEditor();Invalidate(true);}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);
            using(var p=Theme.Rounded(new RectangleF(0,0,Width-1,Height-1),8*e.Graphics.DpiX/96f))
            {using(var b=new SolidBrush(Theme.Field))e.Graphics.FillPath(b,p);if(editor.Focused)using(var pen=new Pen(Theme.Blue,1.5f))e.Graphics.DrawPath(pen,p);}
        }
    }
    public class TimeBox:FieldTextBox
    {
        public DateTime Value {get{return DateTime.Today.Add(Schedule.Clock(Text));}set{Text=value.ToString("HH:mm",CultureInfo.InvariantCulture);}}
    }
    public class StepperBox:UserControl
    {
        readonly CrispTextBox editor;
        readonly Button minus,plus;
        bool arranging;
        decimal minimum,maximum=100;
        public event EventHandler ValueChanged;
        public StepperBox()
        {
            DoubleBuffered=true;BackColor=Color.White;Size=new Size(140,36);TabStop=false;
            editor=new CrispTextBox(){BackColor=Theme.Field,ForeColor=Theme.Ink,Font=Theme.Font(10),Text="0",TextAlign=HorizontalAlignment.Center};Controls.Add(editor);
            minus=Theme.Button(this,"−",0,0,30,36,false,delegate{Step(-1);});plus=Theme.Button(this,"+",100,0,30,36,false,delegate{Step(1);});
            minus.BackColor=plus.BackColor=Theme.Field;minus.FlatAppearance.BorderSize=plus.FlatAppearance.BorderSize=0;minus.Font=plus.Font=Theme.Font(13);
            editor.TextChanged+=delegate{if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);};editor.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Up){Step(1);e.SuppressKeyPress=true;}if(e.KeyCode==Keys.Down){Step(-1);e.SuppressKeyPress=true;}};
            LayoutParts();
        }
        public decimal Minimum {get{return minimum;}set{minimum=value;}}
        public decimal Maximum {get{return maximum;}set{maximum=value;}}
        public decimal Value
        {
            get{decimal n;if(!decimal.TryParse(editor.Text,out n)||n<minimum||n>maximum||n!=decimal.Truncate(n))throw new ArgumentException("请填写 "+minimum+"–"+maximum+" 之间的整数。");return n;}
            set{editor.Text=Math.Max(minimum,Math.Min(maximum,value)).ToString("0");}
        }
        void Step(int amount)
        {
            decimal n;if(!decimal.TryParse(editor.Text,out n))n=minimum;
            n=Math.Max(minimum,Math.Min(maximum,decimal.Truncate(n)));
            if(amount>0&&n>=maximum){Value=maximum;return;}
            if(amount<0&&n<=minimum){Value=minimum;return;}
            Value=Math.Max(minimum,Math.Min(maximum,n+amount));
        }
        protected override void OnResize(EventArgs e){base.OnResize(e);if(editor!=null&&plus!=null)LayoutParts();}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(editor!=null){editor.Font=Font;if(plus!=null)LayoutParts();}}
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(editor!=null&&plus!=null)LayoutParts();}
        void LayoutParts(){if(arranging)return;arranging=true;try{int w=Math.Max(24,(int)(Height*.8));minus.Bounds=new Rectangle(0,0,w,Height);plus.Bounds=new Rectangle(Width-w,0,w,Height);editor.Bounds=new Rectangle(w,Math.Max(3,(Height-editor.PreferredHeight)/2),Math.Max(8,Width-2*w),editor.PreferredHeight);}finally{arranging=false;}}
        protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);LayoutParts();Invalidate(true);}
        protected override void OnPaintBackground(PaintEventArgs e)
        {e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);using(var p=Theme.Rounded(new RectangleF(0,0,Width-1,Height-1),8*e.Graphics.DpiX/96f))using(var b=new SolidBrush(Theme.Field))e.Graphics.FillPath(b,p);}
    }
    public class ChoiceBox:Button
    {
        public readonly List<string> Items=new List<string>();
        int selected=-1;
        ContextMenuStrip menu;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex {get{return selected;}set{if(value<-1||value>=Items.Count)throw new ArgumentOutOfRangeException();if(selected==value)return;selected=value;Text=selected<0?"":Items[selected];Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
        public ChoiceBox(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Theme.Field;ForeColor=Theme.Ink;FlatStyle=FlatStyle.Flat;Cursor=Cursors.Default;AccessibleRole=AccessibleRole.ComboBox;}
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);if(IsDisposed||Items.Count==0)return;
            if(menu==null)menu=new ContextMenuStrip(){Font=Theme.Font(10),BackColor=Color.White,ForeColor=Theme.Ink,ShowImageMargin=false,Renderer=new CrispMenuRenderer(),Padding=new Padding(5),AutoClose=true};
            if(menu.Visible){menu.Close();return;}
            for(int i=menu.Items.Count-1;i>=0;i--)menu.Items[i].Dispose();menu.Items.Clear();
            for(int i=0;i<Items.Count;i++){int n=i;var item=new ToolStripMenuItem(Items[i]){Checked=i==selected,Padding=new Padding(10,7,16,7)};item.Click+=delegate{SelectedIndex=n;};menu.Items.Add(item);}
            menu.Show(this,new Point(0,Height));
        }
        protected override void Dispose(bool disposing){if(disposing&&menu!=null){menu.Dispose();menu=null;}base.Dispose(disposing);}
        protected override bool IsInputKey(Keys keyData){return keyData==Keys.Up||keyData==Keys.Down||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e){if(Items.Count>0&&(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)){SelectedIndex=Math.Max(0,Math.Min(Items.Count-1,selected+(e.KeyCode==Keys.Down?1:-1)));e.Handled=true;}base.OnKeyDown(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Color.White:Parent.BackColor);float s=g.DpiX/96f;
            using(var p=Theme.Rounded(new RectangleF(0,0,Width-1,Height-1),8*s)){using(var b=new SolidBrush(Theme.Field))g.FillPath(b,p);if(Focused&&ShowFocusCues)using(var pen=new Pen(Theme.Blue,1.5f))g.DrawPath(pen,p);}
            Typography.DrawText(g,Text,Font,new Rectangle((int)(11*s),0,Width-(int)(40*s),Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            float x=Width-18*s,y=Height/2f;using(var pen=new Pen(Theme.Muted,1.6f*s){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLines(pen,new[]{new PointF(x-4*s,y-2*s),new PointF(x,y+2*s),new PointF(x+4*s,y-2*s)});
        }
    }
    public class SwitchCheck:CheckBox
    {
        public bool Chip;
        public SwitchCheck(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Default;}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;float s=g.DpiX/96f;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Color.White:Parent.BackColor);
            if(Chip)
            {
                using(var path=Theme.Rounded(new RectangleF(1,1,Width-2,Height-2),9*s))using(var b=new SolidBrush(Checked?Theme.Blue:Theme.Field))g.FillPath(b,path);
                Typography.DrawText(g,Text,Font,ClientRectangle,Checked?Color.White:Theme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            }
            else
            {
                float w=36*s,h=22*s,x=Width-w-1,y=(Height-h)/2;
                using(var p=Theme.Rounded(new RectangleF(x,y,w,h),h/2))using(var b=new SolidBrush(Checked?Theme.Blue:Color.FromArgb(208,210,215)))g.FillPath(b,p);
                float d=h-4*s;using(var b=new SolidBrush(Color.White))g.FillEllipse(b,Checked?x+w-d-2*s:x+2*s,y+2*s,d,d);
                Typography.DrawText(g,Text,Font,new Rectangle(0,0,(int)(x-12*s),Height),Theme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            }
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-2,-2));
        }
    }
}
