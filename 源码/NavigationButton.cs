using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace QingMove
{
    public class NavigationButton:Button
    {
        public int Symbol;
        bool selected,hovered,pressed;
        public bool Selected {get{return selected;}set{if(selected==value)return;selected=value;Invalidate();}}
        public NavigationButton()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            Cursor=Cursors.Default;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
            Font=Theme.Font(10);UseVisualStyleBackColor=false;
        }
        protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){pressed=true;Invalidate();}base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture){pressed=false;Invalidate();}base.OnMouseCaptureChanged(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;float scale=g.DpiX/96f;g.SmoothingMode=SmoothingMode.AntiAlias;
            var background=Parent==null?Theme.Paper:Parent.BackColor;g.Clear(background);
            Color fill=selected?(pressed?Color.FromArgb(0,96,201):Theme.Blue):pressed?Color.FromArgb(214,216,223):hovered?Color.FromArgb(225,227,233):background;
            Color ink=Enabled?(selected?Color.White:Theme.Ink):Theme.Muted;
            using(var outline=Theme.Rounded(new RectangleF(1,1,Width-2,Height-2),8*scale))
            {
                using(var brush=new SolidBrush(fill))g.FillPath(brush,outline);
                if(Focused&&ShowFocusCues)using(var pen=new Pen(selected?Color.FromArgb(155,202,255):Theme.Blue,1.4f*scale))g.DrawPath(pen,outline);
            }
            DrawSymbol(g,new RectangleF(14*scale,(Height-18*scale)/2,18*scale,18*scale),ink);
            Typography.DrawText(g,Text,Font,new Rectangle((int)(44*scale),0,Math.Max(1,Width-(int)(54*scale)),Height),ink,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
        void DrawSymbol(Graphics g,RectangleF box,Color color)
        {
            var saved=g.Save();g.TranslateTransform(box.X,box.Y);g.ScaleTransform(box.Width/20,box.Height/20);
            using(var pen=new Pen(color,1.5f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            {
                if(Symbol==0){g.DrawEllipse(pen,2,2,16,16);g.DrawLines(pen,new[]{new PointF(10,5),new PointF(10,10),new PointF(13.5f,12)});}
                else if(Symbol==1){g.DrawArc(pen,5,3,10,11,180,180);g.DrawLines(pen,new[]{new PointF(5,8),new PointF(5,13),new PointF(3,15),new PointF(17,15),new PointF(15,13),new PointF(15,8)});g.DrawArc(pen,8,14,4,4,0,180);g.DrawLine(pen,10,1,10,3);}
                else if(Symbol==2){for(int i=0;i<3;i++){float x=4+i*6,y=i==1?6:12;g.DrawLine(pen,x,2,x,y-2);g.DrawLine(pen,x,y+2,x,18);g.DrawEllipse(pen,x-2,y-2,4,4);}}
                else if(Symbol==3){g.DrawLine(pen,2,18,18,18);g.DrawRectangle(pen,3,10,3,6);g.DrawRectangle(pen,9,5,3,11);g.DrawRectangle(pen,15,2,3,14);}
                else {g.DrawArc(pen,2,2,16,16,35,280);g.DrawLines(pen,new[]{new PointF(17,2),new PointF(18,7),new PointF(13,6)});g.DrawLine(pen,10,6,10,12);g.DrawLines(pen,new[]{new PointF(7,9),new PointF(10,12),new PointF(13,9)});}
            }
            g.Restore(saved);
        }
    }
}
