using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
namespace QingMove
{
    public class ToastForm:Form
    {
        readonly Settings config;
        readonly bool test;
        readonly Action<string> callback;
        readonly Func<string,bool> saveDecision;
        readonly Timer timer=new Timer(){Interval=1000};
        readonly Color accent;
        Label countdown;
        int remaining;
        bool acted;
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08000000 | 0x00000080;p.ClassStyle|=0x00020000;return p;}}
        public ToastForm(Settings s,bool isTest,Action<string> onAction,string updateHint=null,Action onUpdate=null,Func<string,bool> saveDecision=null)
        {
            SuspendLayout();
            config=s.Copy();test=isTest;callback=onAction;this.saveDecision=saveDecision;remaining=s.DurationSeconds;accent=Theme.Accent(s.Accent);
            Text="走动提醒 · "+(test?"测试通知":"起来活动一下");
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            Font=Theme.Font(s.FontSize);ForeColor=Theme.Ink;BackColor=Color.White;DoubleBuffered=true;
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
            var work=(s.Monitor=="cursor"?Screen.FromPoint(Cursor.Position):Screen.PrimaryScreen).WorkingArea;
            float scale=DeviceDpi/96f;
            int logicalWidth=Math.Min(500,(int)(work.Width/scale)-32),textWidth=logicalWidth-64;
            var headlineFont=Theme.Bold(Math.Max(23,s.FontSize+8));
            var titleSize=Typography.MeasureText(s.Title,headlineFont,textWidth-20,96);
            int titleHeight=Math.Max(48,titleSize.Height+8);
            int extra=string.IsNullOrEmpty(updateHint)?0:44;
            titleHeight=Math.Min(titleHeight,Math.Max(60,(int)(work.Height/scale)-360-extra));
            var bodySize=Typography.MeasureText(s.Message,Font,textWidth-20,96);
            int bodyHeight=Math.Max(60,bodySize.Height+12);
            bodyHeight=Math.Min(bodyHeight,Math.Max(50,(int)(work.Height/scale)-titleHeight-330-extra));
            int buttonY=122+titleHeight+bodyHeight;
            ClientSize=new Size(logicalWidth,buttonY+148+extra);
            var icon=Theme.Card(this,32,26,46,46);icon.BackColor=accent;icon.Controls.Add(new WalkMark(){Location=new Point(9,8),Size=new Size(29,30),ForeColor=Color.White});
            var brand=Theme.Label(this,"走动提醒",90,25,logicalWidth-170,26);brand.Font=Theme.Bold(12);
            var note=Theme.Label(this,test?"通知预览":"给自己两分钟，活动一下。",90,51,logicalWidth-170,24);note.Font=Theme.Font(9);note.ForeColor=Theme.Muted;
            var close=Theme.Button(this,"×",logicalWidth-54,22,30,30,false,delegate{Finish("dismiss");});close.Font=Theme.Font(16);close.BackColor=Theme.Field;close.ForeColor=Theme.Muted;close.FlatAppearance.BorderSize=0;
            var titlePanel=new Panel(){Location=new Point(32,96),Size=new Size(textWidth,titleHeight),AutoScroll=true,BackColor=Color.White};Controls.Add(titlePanel);
            var title=new CrispLabel(){Text=s.Title,Location=Point.Empty,Size=new Size(textWidth-20,titleSize.Height+2),Font=headlineFont,ForeColor=Theme.Ink};titlePanel.Controls.Add(title);
            var bodyPanel=new Panel(){Location=new Point(32,104+titleHeight),Size=new Size(textWidth,bodyHeight),AutoScroll=true,BackColor=Color.White};Controls.Add(bodyPanel);
            var body=new CrispLabel(){Text=s.Message,Location=Point.Empty,Size=new Size(textWidth-20,bodySize.Height+2),Font=Font,ForeColor=Color.FromArgb(94,94,102)};bodyPanel.Controls.Add(body);
            int gap=12,doneWidth=(textWidth-gap)/2;
            var done=Theme.Button(this,"我已起身  ✓",32,buttonY,doneWidth,46,true,delegate{Finish("done");});done.BackColor=accent;done.ForeColor=Color.White;done.FlatAppearance.BorderSize=0;done.Font=Theme.Bold(11);
            var workButton=Theme.Button(this,"继续工作",32+doneWidth+gap,buttonY,textWidth-doneWidth-gap,46,false,delegate{Finish("work");});workButton.BackColor=Theme.Field;workButton.ForeColor=Theme.Ink;workButton.FlatAppearance.BorderSize=0;workButton.Font=Theme.Font(10);
            var later=Theme.Button(this,s.SnoozeMinutes+" 分钟后提醒",32,buttonY+54,textWidth,30,false,delegate{Finish("snooze");});later.BackColor=Color.White;later.ForeColor=Theme.Muted;later.FlatAppearance.BorderSize=0;later.Font=Theme.Font(9);
            if(extra>0){var update=Theme.Button(this,updateHint+"  ›",32,buttonY+90,textWidth,34,false,delegate{if(onUpdate!=null)onUpdate();});update.BackColor=Color.FromArgb(237,245,255);update.ForeColor=Theme.Blue;update.FlatAppearance.BorderSize=0;update.Font=Theme.Font(9);}
            countdown=Theme.Label(this,"",32,buttonY+92+extra,textWidth,34);countdown.Font=Theme.Font(8.5f);countdown.ForeColor=Theme.Muted;UpdateCountdown();
            Opacity=s.OpacityPercent/100.0;
            timer.Tick+=delegate{if(remaining>0 && !Bounds.Contains(Cursor.Position)){remaining--;UpdateCountdown();Invalidate();if(remaining==0)Finish("timeout");}};
            Shown+=delegate {UpdateRegion();PositionToast();if(s.DurationSeconds>0)timer.Start();};
            FormClosed+=delegate{timer.Dispose();if(!acted){acted=true;if(saveDecision!=null)saveDecision("dismiss");if(callback!=null)callback("dismiss");}};
            Paint+=delegate(object sender,PaintEventArgs e)
            {
                float currentScale=e.Graphics.DpiX/96f;
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(var path=Theme.Rounded(new RectangleF(0.5f,0.5f,ClientSize.Width-2,ClientSize.Height-2),20*currentScale))using(var pen=new Pen(Color.FromArgb(215,215,222)))e.Graphics.DrawPath(pen,path);
                float ratio=config.DurationSeconds==0?1f:(float)remaining/config.DurationSeconds;
                using(var b=new SolidBrush(Theme.Field))e.Graphics.FillRectangle(b,32*currentScale,ClientSize.Height-10*currentScale,ClientSize.Width-64*currentScale,2*currentScale);
                using(var b=new SolidBrush(accent))e.Graphics.FillRectangle(b,32*currentScale,ClientSize.Height-10*currentScale,(ClientSize.Width-64*currentScale)*ratio,2*currentScale);
            };
            AutoScaleDimensions=new SizeF(96,96);ResumeLayout(true);
        }
        void UpdateRegion(){if(IsHandleCreated&&Width>0&&Height>0)Theme.RoundWindow(this,(int)(20*DeviceDpi/96f));}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);UpdateRegion();}
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);UpdateRegion();Invalidate(true);
            if(IsHandleCreated&&!IsDisposed)BeginInvoke((Action)delegate{if(!IsDisposed){UpdateRegion();PositionToast();}});
        }
        void UpdateCountdown() {countdown.Text=remaining==0?"准备好了，就站起来走两步。":remaining+" 秒后收起 · 鼠标移入可停留";}
        void PositionToast()
        {
            var area=(config.Monitor=="cursor"?Screen.FromPoint(Cursor.Position):Screen.PrimaryScreen).WorkingArea;
            int margin=(int)Math.Round(16*DeviceDpi/96f);
            int x=config.Position=="center"?area.Left+(area.Width-Width)/2:config.Position.EndsWith("left")?area.Left+margin:area.Right-Width-margin;
            int y=config.Position=="center"?area.Top+(area.Height-Height)/2:config.Position.StartsWith("top")?area.Top+margin:area.Bottom-Height-margin;
            var p=new Point(Math.Max(area.Left,Math.Min(x,area.Right-Width)),Math.Max(area.Top,Math.Min(y,area.Bottom-Height)));Location=p;
            SetWindowPos(Handle,new IntPtr(-1),p.X,p.Y,Width,Height,0x0010|0x0040);
        }
        void Finish(string action) {if(acted)return;if(saveDecision!=null&&!saveDecision(action)){timer.Stop();countdown.Text="未能保存记录，请检查磁盘后再次点击。";countdown.ForeColor=Color.FromArgb(177,92,20);return;}acted=true;if(callback!=null)callback(action);Close();}
        public void ReplaceClose(string reason="replaced") {if(acted)return;acted=true;if(saveDecision!=null)saveDecision(reason);Close();}
    }
}
