using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using W=System.Windows;
using M=System.Windows.Media;
namespace QingMove
{
    // Shape the actual font outlines and paint at device pixel size, without a scaled bitmap.
    public static class Typography
    {
        static readonly Dictionary<string,GraphicsPath> paths=new Dictionary<string,GraphicsPath>();
        static M.FormattedText Format(string text,Font font,float dpi,int width,TextFormatFlags flags)
        {
            var face=new M.Typeface(new M.FontFamily(font.Name),W.FontStyles.Normal,(font.Bold?W.FontWeights.Medium:W.FontWeights.Normal),W.FontStretches.Normal);
            var result=new M.FormattedText(text,CultureInfo.GetCultureInfo("zh-CN"),W.FlowDirection.LeftToRight,face,font.SizeInPoints*dpi/72.0,M.Brushes.Black,1.0);
            result.MaxTextWidth=Math.Max(1,width);
            if((flags&TextFormatFlags.SingleLine)!=0)result.MaxLineCount=1;
            if((flags&TextFormatFlags.EndEllipsis)!=0)result.Trimming=W.TextTrimming.CharacterEllipsis;
            if((flags&TextFormatFlags.HorizontalCenter)!=0)result.TextAlignment=W.TextAlignment.Center;
            else if((flags&TextFormatFlags.Right)!=0)result.TextAlignment=W.TextAlignment.Right;
            return result;
        }
        public static Size MeasureText(string text,Font font,int width,float dpi)
        {
            if(string.IsNullOrEmpty(text))return Size.Empty;
            var f=Format(text,font,dpi,width,TextFormatFlags.WordBreak);
            return new Size((int)Math.Ceiling(f.WidthIncludingTrailingWhitespace),(int)Math.Ceiling(f.Height));
        }
        static GraphicsPath Outline(M.Geometry geometry)
        {
            var result=new GraphicsPath(FillMode.Winding);
            var flat=geometry.GetFlattenedPathGeometry(.08,M.ToleranceType.Absolute);
            foreach(var figure in flat.Figures)
            {
                var points=new List<PointF>();points.Add(new PointF((float)figure.StartPoint.X,(float)figure.StartPoint.Y));
                foreach(var segment in figure.Segments)
                {
                    var poly=segment as M.PolyLineSegment;
                    if(poly!=null)foreach(var p in poly.Points)points.Add(new PointF((float)p.X,(float)p.Y));
                    var line=segment as M.LineSegment;if(line!=null)points.Add(new PointF((float)line.Point.X,(float)line.Point.Y));
                }
                if(points.Count<2)continue;result.StartFigure();result.AddLines(points.ToArray());if(figure.IsClosed)result.CloseFigure();
            }
            return result;
        }
        public static void DrawText(Graphics graphics,string text,Font font,Rectangle bounds,Color color,TextFormatFlags flags)
        {
            if(string.IsNullOrEmpty(text)||bounds.Width<=0||bounds.Height<=0)return;
            string key=text+"\u001f"+font.Name+"|"+font.SizeInPoints.ToString("R",CultureInfo.InvariantCulture)+"|"+font.Style+"|"+graphics.DpiY+"|"+bounds.Width+"|"+bounds.Height+"|"+flags;
            GraphicsPath path;
            if(!paths.TryGetValue(key,out path))
            {
                var formatted=Format(text,font,graphics.DpiY,bounds.Width,flags);
                double y=(flags&TextFormatFlags.VerticalCenter)!=0?Math.Max(0,(bounds.Height-formatted.Height)/2):(flags&TextFormatFlags.Bottom)!=0?Math.Max(0,bounds.Height-formatted.Height):0;
                path=Outline(formatted.BuildGeometry(new W.Point(0,y)));
                if(paths.Count>=384){foreach(var old in paths.Values)old.Dispose();paths.Clear();}
                paths.Add(key,path);
            }
            var state=graphics.Save();
            try
            {
                graphics.SetClip(bounds,CombineMode.Intersect);graphics.TranslateTransform(bounds.X,bounds.Y);
                graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                using(var brush=new SolidBrush(color))graphics.FillPath(brush,path);
            }
            finally{graphics.Restore(state);}
        }
    }
    public class CrispLabel:Label
    {
        public CrispLabel(){UseCompatibleTextRendering=false;UseMnemonic=false;}
        public override Size GetPreferredSize(Size proposedSize)
        {
            int width=MaximumSize.Width>0?MaximumSize.Width:proposedSize.Width>1?proposedSize.Width:100000;
            var size=Typography.MeasureText(Text,Font,Math.Max(1,width-Padding.Horizontal),DeviceDpi);
            return new Size(size.Width+Padding.Horizontal,size.Height+Padding.Vertical);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            TextFormatFlags flags=TextFormatFlags.WordBreak;
            if(AutoEllipsis)flags|=TextFormatFlags.EndEllipsis;
            if(TextAlign==ContentAlignment.TopCenter||TextAlign==ContentAlignment.MiddleCenter||TextAlign==ContentAlignment.BottomCenter)flags|=TextFormatFlags.HorizontalCenter;
            if(TextAlign==ContentAlignment.TopRight||TextAlign==ContentAlignment.MiddleRight||TextAlign==ContentAlignment.BottomRight)flags|=TextFormatFlags.Right;
            if(TextAlign==ContentAlignment.MiddleLeft||TextAlign==ContentAlignment.MiddleCenter||TextAlign==ContentAlignment.MiddleRight)flags|=TextFormatFlags.VerticalCenter;
            if(TextAlign==ContentAlignment.BottomLeft||TextAlign==ContentAlignment.BottomCenter||TextAlign==ContentAlignment.BottomRight)flags|=TextFormatFlags.Bottom;
            Typography.DrawText(e.Graphics,Text,Font,new Rectangle(Padding.Left,Padding.Top,Width-Padding.Horizontal,Height-Padding.Vertical),Enabled?ForeColor:SystemColors.GrayText,flags);
        }
    }
    public class CrispMenuRenderer:ToolStripProfessionalRenderer
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {Typography.DrawText(e.Graphics,e.Text,e.TextFont,e.TextRectangle,e.TextColor,e.TextFormat|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);}
    }
}
