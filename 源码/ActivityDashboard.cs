using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
namespace QingMove
{
    public class ActivityDashboard:UserControl
    {
        readonly Func<IList<ActivityRecord>> source;
        readonly Func<string> warning;
        readonly Button[] segments=new Button[4];
        readonly Label periodTitle,description,chartTitle,selection,notice;
        readonly Label[] values=new Label[4];
        readonly Button forward;
        readonly ActivityChart chart;
        readonly ListView details;
        ActivityReport report;
        ActivityPeriod period=ActivityPeriod.Week;
        DateTime anchor=DateTime.Today;
        public ActivityReport Report {get{return report;}}
        public ActivityPeriod Period {get{return period;}}
        public DateTime AnchorDate {get{return anchor;}}
        public ActivityDashboard(Func<IList<ActivityRecord>> getRecords,Func<string> getWarning)
        {
            SuspendLayout();source=getRecords;warning=getWarning;Font=Theme.Font(10);BackColor=Theme.Paper;
            AutoScaleMode=AutoScaleMode.Inherit;AutoScroll=true;AutoScrollMinSize=new Size(738,650);Size=new Size(760,655);
            var tabs=Theme.Card(this,0,0,192,38);tabs.BackColor=Color.FromArgb(232,233,239);
            string[] labels={"日","周","月","年"};
            for(int i=0;i<4;i++){int index=i;segments[i]=Theme.Button(tabs,labels[i],3+i*47,3,45,32,false,delegate{SelectPeriod((ActivityPeriod)index,anchor);});segments[i].FlatAppearance.BorderSize=0;}
            Theme.Button(this,"选择日期",516,0,102,38,false,delegate{ChooseDate();});
            Theme.Button(this,"导出记录",630,0,108,38,false,delegate{Export();});
            periodTitle=Theme.Label(this,"",2,50,492,34);periodTitle.Font=Theme.Bold(15);
            Theme.Button(this,"‹",530,50,38,34,false,delegate{SelectPeriod(period,ActivityStats.Shift(period,anchor,-1));}).Font=Theme.Font(17);
            Theme.Button(this,"今天",578,50,102,34,false,delegate{SelectPeriod(period,DateTime.Today);});
            forward=Theme.Button(this,"›",690,50,48,34,false,delegate{SelectPeriod(period,ActivityStats.Shift(period,anchor,1));});forward.Font=Theme.Font(17);
            description=Theme.Label(this,"",3,88,732,22);description.Font=Theme.Font(8.5f);description.ForeColor=Theme.Muted;
            string[] captions={"起身打卡","继续工作","未回应","打卡天数"};
            for(int i=0;i<4;i++)
            {
                var card=Theme.Card(this,i*188,122,174,82);
                var caption=Theme.Label(card,captions[i],16,9,142,22);caption.Font=Theme.Font(9);caption.ForeColor=Theme.Muted;
                values[i]=Theme.Label(card,"0",15,31,144,42);values[i].Font=Theme.Font(23);values[i].ForeColor=i==0?Theme.Blue:Theme.Ink;
            }
            var plot=Theme.Card(this,0,220,738,246);
            chartTitle=Theme.Label(plot,"",18,9,270,28);chartTitle.Font=Theme.Bold(10);
            var legend=Theme.Label(plot,"● 起身   ● 工作   ● 未回应",376,11,342,24);legend.Font=Theme.Font(8.5f);legend.ForeColor=Theme.Muted;
            // Colored keys avoid using font-dependent emoji or bitmap icons.
            legend.Visible=false;
            string[] keys={"起身","工作","未回应","待回应"};Color[] colors={ActivityChart.DoneColor,ActivityChart.WorkColor,ActivityChart.MissedColor,Color.FromArgb(231,233,240)};
            for(int i=0;i<4;i++){var dot=new Panel(){Bounds=new Rectangle(400+i*80,21,7,7),BackColor=colors[i]};plot.Controls.Add(dot);var key=Theme.Label(plot,keys[i],411+i*80,12,64,24);key.Font=Theme.Font(8);key.ForeColor=Theme.Muted;}
            chart=new ActivityChart(){Bounds=new Rectangle(13,46,712,170),Font=Font,BackColor=Color.White};plot.Controls.Add(chart);
            selection=Theme.Label(plot,"点击柱形可查看对应月份或当天明细。",18,217,700,22);selection.Font=Theme.Font(8);selection.ForeColor=Theme.Muted;
            chart.BucketClicked+=delegate(DateTime date){if(period==ActivityPeriod.Year)SelectPeriod(ActivityPeriod.Month,date);else if(period!=ActivityPeriod.Day)SelectPeriod(ActivityPeriod.Day,date);};
            chart.BucketHovered+=delegate(string text){selection.Text=text;};
            var listCard=Theme.Card(this,0,480,738,132);
            details=new ListView(){Bounds=new Rectangle(16,12,706,108),View=View.Details,FullRowSelect=true,BorderStyle=BorderStyle.None,HideSelection=false,BackColor=Color.White,Font=Theme.Font(9),OwnerDraw=true};
            details.Columns.Add("提醒时间",185);details.Columns.Add("我的选择",140);details.Columns.Add("回应时间",345);listCard.Controls.Add(details);
            details.DrawColumnHeader+=delegate(object sender,DrawListViewColumnHeaderEventArgs e){e.DrawBackground();Typography.DrawText(e.Graphics,e.Header.Text,details.Font,Rectangle.Inflate(e.Bounds,-5,0),Theme.Muted,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);};
            details.DrawSubItem+=delegate(object sender,DrawListViewSubItemEventArgs e){bool selected=e.Item.Selected;e.Graphics.FillRectangle(selected?SystemBrushes.Highlight:Brushes.White,e.Bounds);Typography.DrawText(e.Graphics,e.SubItem.Text,details.Font,Rectangle.Inflate(e.Bounds,-5,0),selected?SystemColors.HighlightText:e.ColumnIndex==1?OutcomeColor((string)e.Item.Tag):Theme.Ink,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
            notice=Theme.Label(this,"",3,620,732,30);notice.Font=Theme.Font(8);notice.ForeColor=Theme.Muted;
            ResumeLayout(true);RefreshData();
        }
        public void SelectPeriod(ActivityPeriod value,DateTime date){period=value;anchor=date.Date;RefreshData();}
        public void RefreshData()
        {
            report=ActivityStats.Build(source==null?new List<ActivityRecord>():source(),period,anchor);
            for(int i=0;i<4;i++){segments[i].BackColor=(int)period==i?Color.White:Color.FromArgb(232,233,239);segments[i].ForeColor=(int)period==i?Theme.Blue:Theme.Muted;segments[i].Invalidate();}
            periodTitle.Text=report.Title;
            description.Text="共 "+report.Total+" 次正式提醒 · 待回应 "+report.Pending+" 次 · 按提醒出现日期归档";
            values[0].Text=report.Done.ToString();values[1].Text=report.Work.ToString();values[2].Text=report.Unanswered.ToString();values[3].Text=report.ActiveDays.ToString();
            forward.Enabled=report.Start<ActivityStats.StartOf(period,DateTime.Today);
            chartTitle.Text=period==ActivityPeriod.Day?"当天打卡 · 每个点是一条提醒":"起身打卡与提醒选择";
            chart.SetReport(report,period);
            selection.Text=period==ActivityPeriod.Day?"蓝点表示已起身；每条提醒的详细时间见下方。":period==ActivityPeriod.Year?"点击柱形查看该月，再点击日期查看当天。":"点击柱形查看当天的打卡明细。";
            details.BeginUpdate();details.Items.Clear();
            foreach(var record in report.Records.AsEnumerable().Reverse())
            {
                var item=new ListViewItem(record.ShownTime.ToString("MM-dd HH:mm:ss")){Tag=record.Outcome};item.SubItems.Add(OutcomeText(record.Outcome));item.SubItems.Add(record.FinishedTime.HasValue?(record.Outcome=="done"||record.Outcome=="work"?record.FinishedTime.Value.ToString("MM-dd HH:mm:ss"):"未选择起身或继续工作"):"等待选择");details.Items.Add(item);
            }
            if(details.Items.Count==0){var item=new ListViewItem("本期暂无提醒记录"){Tag="pending"};item.SubItems.Add("—");item.SubItems.Add("正式提醒后选择，即会自动记录。");details.Items.Add(item);}
            details.EndUpdate();string problem=warning==null?null:warning();notice.Text=string.IsNullOrEmpty(problem)?"仅记录按钮选择，不测量实际走动时长。预览提醒不计入打卡。":problem;notice.ForeColor=string.IsNullOrEmpty(problem)?Theme.Muted:Color.FromArgb(177,92,20);
        }
        static Color OutcomeColor(string outcome){return outcome=="done"?Theme.Blue:outcome=="work"?Color.FromArgb(164,105,34):Theme.Muted;}
        public static string OutcomeText(string outcome){switch(outcome){case "done":return "我已起身";case "work":return "继续工作";case "pending":return "待回应";case "snooze":return "稍后提醒";case "timeout":return "超时未回应";case "dismiss":return "已关闭";case "paused":return "暂停时收起";case "exit":return "退出时收起";case "interrupted":return "上次未回应";default:return "被新提醒收起";}}
        void ChooseDate()
        {
            using(var dialog=new Form(){Text="选择查看日期",StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,ClientSize=new Size(342,168),Font=Theme.Font(10),BackColor=Theme.Paper})
            {
                Theme.Label(dialog,"输入日期，例如 2026-11-16",20,14,302,26);
                var input=new FieldTextBox(){Bounds=new Rectangle(20,50,302,38),Font=Theme.Font(11),Text=anchor.ToString("yyyy-MM-dd")};dialog.Controls.Add(input);
                Theme.Button(dialog,"查看",204,112,118,36,true,delegate{DateTime value;if(!DateTime.TryParseExact(input.Text.Trim(),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out value)||value.Year<1900||value.Year>9998){MessageBox.Show(dialog,"请填写 1900–9998 年内的有效日期，格式为 yyyy-MM-dd。","日期无效");return;}SelectPeriod(period,value);dialog.DialogResult=DialogResult.OK;});dialog.ShowDialog(FindForm());
            }
        }
        public void ExportTo(string path)
        {
            var lines=new List<string>{"提醒时间,我的选择,回应时间"};
            foreach(var item in report.Records)lines.Add("\""+item.ShownTime.ToString("o")+"\",\""+OutcomeText(item.Outcome)+"\",\""+(item.FinishedTime.HasValue?item.FinishedTime.Value.ToString("o"):"")+"\"");
            File.WriteAllLines(path,lines,new UTF8Encoding(true));
        }
        void Export(){using(var dialog=new SaveFileDialog(){Filter="CSV 打卡记录 (*.csv)|*.csv",FileName="走动打卡_"+report.Start.ToString("yyyyMMdd")+".csv"})if(dialog.ShowDialog(FindForm())==DialogResult.OK)try{ExportTo(dialog.FileName);}catch(Exception ex){MessageBox.Show(FindForm(),"导出失败："+ex.Message,"走动打卡");}}
    }
    public class ActivityChart:Control
    {
        public static readonly Color DoneColor=Theme.Blue,WorkColor=Color.FromArgb(238,174,82),MissedColor=Color.FromArgb(194,199,211);
        ActivityReport report;
        ActivityPeriod period;
        readonly List<RectangleF> hits=new List<RectangleF>();
        public event Action<DateTime> BucketClicked;
        public event Action<string> BucketHovered;
        public ActivityChart(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Default;AccessibleName="起身打卡图表";}
        public void SetReport(ActivityReport value,ActivityPeriod kind){report=value;period=kind;hits.Clear();Invalidate();}
        void DrawCaption(Graphics g,string text,Font font,RectangleF rect,Color color,TextFormatFlags align){Typography.DrawText(g,text,font,Rectangle.Round(rect),color,align|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;hits.Clear();if(report==null)return;float s=g.DpiX/96f;
            var plot=new RectangleF(34*s,13*s,Width-46*s,Height-42*s);if(plot.Width<=0||plot.Height<=0)return;
            int maximum=Math.Max(1,report.Buckets.Max(x=>x.Total));int top=Math.Max(4,(int)Math.Ceiling(maximum/4.0)*4);
            using(var small=Theme.Font(7.5f))using(var grid=new Pen(Theme.Line,Math.Max(1,s*.7f)))
            {
                for(int i=0;i<=4;i++){float y=plot.Bottom-plot.Height*i/4;g.DrawLine(grid,plot.Left,y,plot.Right,y);if(period!=ActivityPeriod.Day)DrawCaption(g,(top*i/4).ToString(),small,new RectangleF(0,y-9*s,26*s,18*s),Theme.Muted,TextFormatFlags.Right);}
                float step=plot.Width/report.Buckets.Count;
                for(int i=0;i<report.Buckets.Count;i++)
                {
                    var bucket=report.Buckets[i];float x=plot.Left+i*step;hits.Add(new RectangleF(x,plot.Top,step,plot.Height+28*s));
                    bool showLabel=period!=ActivityPeriod.Day?period!=ActivityPeriod.Month||i%5==0||i==report.Buckets.Count-1:i%3==0;
                    if(showLabel)DrawCaption(g,bucket.Label,small,new RectangleF(x-4*s,plot.Bottom+6*s,step+8*s,22*s),Theme.Muted,TextFormatFlags.HorizontalCenter);
                    if(period==ActivityPeriod.Day)
                    {
                        var records=report.Records.Where(r=>r.ShownTime.Hour==i).ToList();int visible=Math.Min(8,records.Count);float spacing=Math.Min(13*s,(plot.Height-14*s)/8);
                        for(int n=0;n<visible;n++){string kind=records[n].Outcome;Color color=kind=="done"?DoneColor:kind=="work"?WorkColor:kind=="pending"?Color.FromArgb(231,233,240):MissedColor;float d=7*s;using(var brush=new SolidBrush(color))g.FillEllipse(brush,x+(step-d)/2,plot.Bottom-(n+1)*spacing,d,d);}
                        if(records.Count>8)DrawCaption(g,"+"+(records.Count-8),small,new RectangleF(x-4*s,plot.Top,step+8*s,18*s),Theme.Muted,TextFormatFlags.HorizontalCenter);
                    }
                    else if(bucket.Total>0)
                    {
                        float width=Math.Min(32*s,step*.58f),height=plot.Height*bucket.Total/top,left=x+(step-width)/2;
                        var state=g.Save();using(var path=Theme.Rounded(new RectangleF(left,plot.Bottom-height,width,Math.Max(1,height)),Math.Min(4*s,height/2)))
                        {
                            g.SetClip(path);float bottom=plot.Bottom;
                            int[] counts={bucket.Done,bucket.Work,bucket.Unanswered,bucket.Pending};Color[] colors={DoneColor,WorkColor,MissedColor,Color.FromArgb(231,233,240)};
                            for(int n=0;n<4;n++){if(counts[n]==0)continue;float h=plot.Height*counts[n]/top;using(var brush=new SolidBrush(colors[n]))g.FillRectangle(brush,left,bottom-h,width,h+1);bottom-=h;}
                        }g.Restore(state);
                    }
                }
            }
            if(report.Total==0)using(var font=Theme.Font(12))using(var brush=new SolidBrush(Color.FromArgb(244,255,255,255)))
            {g.FillRectangle(brush,plot);DrawCaption(g,"本期还没有打卡记录",font,new RectangleF(plot.Left,plot.Top+plot.Height/2-22*s,plot.Width,30*s),Theme.Muted,TextFormatFlags.HorizontalCenter);}
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);for(int i=0;report!=null&&i<hits.Count&&i<report.Buckets.Count;i++)if(hits[i].Contains(e.Location)){var b=report.Buckets[i];if(BucketHovered!=null)BucketHovered(b.Start.ToString(period==ActivityPeriod.Year?"yyyy年M月":period==ActivityPeriod.Day?"HH:00":"M月d日")+" · 起身 "+b.Done+" · 工作 "+b.Work+" · 未回应 "+b.Unanswered+" · 待回应 "+b.Pending);break;}}
        protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button!=MouseButtons.Left)return;for(int i=0;report!=null&&i<hits.Count&&i<report.Buckets.Count;i++)if(hits[i].Contains(e.Location)){if(BucketClicked!=null)BucketClicked(report.Buckets[i].Start);break;}}
    }
}
