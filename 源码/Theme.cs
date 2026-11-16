using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace QingMove
{
    public static class Theme
    {
        public static readonly Color Ink=Color.FromArgb(29,29,31),Muted=Color.FromArgb(113,113,122),Green=Color.FromArgb(45,146,80),Paper=Color.FromArgb(246,246,248);
        public static readonly Color Night=Color.FromArgb(29,29,31),Lime=Color.FromArgb(0,113,227),Line=Color.FromArgb(231,231,236),Blue=Color.FromArgb(0,113,227),Field=Color.FromArgb(243,243,246);
        static string FindFamily(params string[] names) { foreach(var name in names) using(var font=new Font(name,10)) if(string.Equals(font.Name,name,StringComparison.OrdinalIgnoreCase))return name;return "Microsoft YaHei UI"; }
        static readonly string TextFamily=FindFamily("PingFang SC","Noto Sans SC","Microsoft YaHei UI");
        public static Font Font(float size) { return new Font(TextFamily,size,FontStyle.Regular,GraphicsUnit.Point); }
        public static Font Bold(float size) { return new Font(TextFamily,size,FontStyle.Bold,GraphicsUnit.Point); }
        public static Color Accent(string s) {switch(s){case "blue":return Color.FromArgb(45,116,182);case "orange":return Color.FromArgb(190,96,0);case "purple":return Color.FromArgb(139,70,205);default:return Blue;}}
        public static GraphicsPath Rounded(RectangleF r,float radius)
        {
            var path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            if(d<=0){path.AddRectangle(r);return path;}
            path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        public static void RoundWindow(Form f,int radius)
        {using(var p=Rounded(new RectangleF(0,0,f.Width,f.Height),radius)){var old=f.Region;f.Region=new Region(p);if(old!=null)old.Dispose();}}
        public static Label Label(Control parent,string text,int x,int y,int w,int h)
        {var c=new CrispLabel(){Text=text,Location=new Point(x,y),Size=new Size(w,h),ForeColor=Ink,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleLeft};parent.Controls.Add(c);return c;}
        public static Button Button(Control parent,string text,int x,int y,int w,int h,bool primary,EventHandler action)
        {
            var c=new RoundButton(){Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=primary?Blue:Color.White,ForeColor=primary?Color.White:Ink,Cursor=Cursors.Default,UseVisualStyleBackColor=false,Font=Font(10)};
            c.FlatAppearance.BorderColor=primary?Blue:Line;c.FlatAppearance.BorderSize=primary?0:1;
            c.Click+=action;parent.Controls.Add(c);return c;
        }
        public static Panel Card(Control parent,int x,int y,int w,int h)
        {var c=new CardPanel(){Location=new Point(x,y),Size=new Size(w,h),BackColor=Color.White};parent.Controls.Add(c);return c;}
        public static void DrawWalker(Graphics g,RectangleF bounds,Color ink)
        {
            var saved=g.Save();g.TranslateTransform(bounds.X,bounds.Y);g.ScaleTransform(bounds.Width/80f,bounds.Height/80f);g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var brush=new SolidBrush(ink))g.FillEllipse(brush,39,5,14,14);
            using(var pen=new Pen(ink,7){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            {g.DrawLines(pen,new[]{new Point(43,27),new Point(35,46),new Point(50,56),new Point(58,74)});g.DrawLines(pen,new[]{new Point(36,46),new Point(25,62),new Point(14,72)});g.DrawLines(pen,new[]{new Point(40,31),new Point(52,42),new Point(67,42)});g.DrawLines(pen,new[]{new Point(41,28),new Point(26,31),new Point(18,44)});}
            g.Restore(saved);
        }
        public static Icon AppIcon()
        { using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")) { if(stream!=null)return new Icon(stream); }return SystemIcons.Information; }
    }
    public class RoundButton:Button
    {
        bool hovered;
        public RoundButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Paper:Parent.BackColor);
            var fill=hovered?ControlPaint.Light(BackColor,0.08f):BackColor;
            using(var p=Theme.Rounded(new RectangleF(0,0,Width-1,Height-1),8*g.DpiX/96f))
            {using(var b=new SolidBrush(fill))g.FillPath(b,p);if(FlatAppearance.BorderSize>0)using(var pen=new Pen(FlatAppearance.BorderColor))g.DrawPath(pen,p);}
            Typography.DrawText(g,Text,Font,ClientRectangle,Enabled?ForeColor:SystemColors.GrayText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-5,-5),ForeColor,fill);
        }
    }
    public class CardPanel:Panel
    {
        public CardPanel(){DoubleBuffered=true;}
        protected override void OnPaintBackground(PaintEventArgs e)
        {e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Theme.Paper:Parent.BackColor);using(var p=Theme.Rounded(new RectangleF(0,0,Width-1,Height-1),12*e.Graphics.DpiX/96f))using(var b=new SolidBrush(BackColor))e.Graphics.FillPath(b,p);}
    }
    public class WalkMark:Control
    {
        public WalkMark(){SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Color.Transparent;}
        protected override void OnPaint(PaintEventArgs e){Theme.DrawWalker(e.Graphics,new RectangleF(0,0,Width,Height),ForeColor);}
    }
}
