using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Text;
namespace QingMove
{
    public class SettingsForm:Form
    {
        readonly ReminderApp app;
        CheckBox enabled,window,autostart,hidden;
        ChoiceBox mode,accent,sound,monitor,position;
        static readonly string[] PositionValues={"bottom-right","bottom-left","top-right","top-left","center"};
        FieldTextBox minutes,daily,title,message;
        StepperBox interval,duration,snooze,font,opacity;
        TimeBox start,end;
        CheckBox[] days=new CheckBox[7];
        Label scheduleHint,modeLabel,status,next,feedback,pageTitle,pageSubtitle;
        ListView history;
        Panel[] pages;
        Button[] navigation;
        Panel statusCard,pageHost;
        Button previewButton,saveButton;
        ActivityDashboard dashboard;
        FieldTextBox updateRepository;
        Label updateInfo;
        bool loading,dirty;
        Settings loadedSettings;
        public bool AllowClose;
        public SettingsForm(ReminderApp owner)
        {
            SuspendLayout();
            app=owner;Text="走动提醒";Icon=Theme.AppIcon();
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            Font=Theme.Font(10);BackColor=Theme.Paper;ForeColor=Theme.Ink;DoubleBuffered=true;
            ClientSize=new Size(1040,800);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
            AutoScroll=true;AutoScrollMinSize=new Size(1040,800);
            var side=new Panel(){Location=Point.Empty,Size=new Size(208,800),BackColor=Color.FromArgb(237,237,241)};Controls.Add(side);
            var icon=Theme.Card(side,20,28,40,40);icon.BackColor=Theme.Blue;icon.Controls.Add(new WalkMark(){Location=new Point(8,6),Size=new Size(25,27),ForeColor=Color.White});
            var brand=Theme.Label(side,"走动提醒",72,28,120,40);brand.Font=Theme.Bold(14);
            var sub=Theme.Label(side,"留一点时间给自己",22,84,170,28);sub.Font=Theme.Font(9);sub.ForeColor=Theme.Muted;
            var groupLabel=Theme.Label(side,"设置",22,158,160,26);groupLabel.Font=Theme.Font(9);groupLabel.ForeColor=Theme.Muted;
            navigation=new Button[5];string[] labels={"提醒规则","弹窗与声音","启动与记录","走动打卡","软件更新"};
            for(int i=0;i<5;i++){int index=i;var item=new NavigationButton(){Text=labels[i],Symbol=i,Location=new Point(14,194+i*44),Size=new Size(180,36)};item.Click+=delegate{SelectPage(index);};side.Controls.Add(item);navigation[i]=item;}
            var pause=Theme.Button(side,"暂停 30 分钟",20,660,168,38,false,delegate{app.Pause(30);});pause.BackColor=Color.FromArgb(225,225,231);pause.FlatAppearance.BorderSize=0;
            var hide=Theme.Button(side,"隐藏到菜单栏",20,710,168,38,false,delegate{Hide();});hide.Text="隐藏到托盘";hide.BackColor=side.BackColor;hide.ForeColor=Theme.Muted;hide.FlatAppearance.BorderSize=0;
            var privacy=Theme.Label(side,"PSUAM · v1.5",22,760,166,24);privacy.Font=Theme.Font(8.5f);privacy.ForeColor=Theme.Muted;
            pageTitle=Theme.Label(this,"提醒规则",244,26,660,42);pageTitle.Font=Theme.Bold(21);
            pageSubtitle=Theme.Label(this,"安排专注与休息的节奏。",245,74,720,26);pageSubtitle.ForeColor=Theme.Muted;
            var hero=Theme.Card(this,244,120,760,76);
            statusCard=hero;
            status=Theme.Label(hero,"● 正在运行",20,10,710,26);status.Font=Theme.Font(10);
            next=Theme.Label(hero,"",20,38,720,26);next.ForeColor=Theme.Muted;next.Font=Theme.Font(9.5f);
            var host=new Panel(){Location=new Point(244,220),Size=new Size(760,454),BackColor=Theme.Paper};Controls.Add(host);
            pageHost=host;
            pages=new Panel[5];for(int i=0;i<3;i++){pages[i]=new Panel(){Dock=DockStyle.Fill,BackColor=Theme.Paper,AutoScroll=true,Visible=false};host.Controls.Add(pages[i]);}
            BuildRules(pages[0]);BuildNotification(pages[1]);BuildTools(pages[2]);
            for(int i=3;i<5;i++){pages[i]=new Panel(){Location=new Point(244,120),Size=new Size(760,655),BackColor=Theme.Paper,Visible=false};Controls.Add(pages[i]);}
            dashboard=new ActivityDashboard(app.GetActivities,delegate{return app.ActivityWarning;}){Dock=DockStyle.Fill};pages[3].Controls.Add(dashboard);
            BuildUpdates(pages[4]);
            feedback=Theme.Label(this,"修改后，保存设置即可生效。",246,687,752,30);feedback.ForeColor=Theme.Muted;feedback.Font=Theme.Font(9);
            var preview=Theme.Button(this,"预览提醒",244,735,190,40,false,delegate{Try(delegate{app.ShowToast(Read(),true);SetFeedback("已预览当前设置。正式提醒请先保存。");});});preview.AccessibleName="立即测试通知";
            previewButton=preview;saveButton=Theme.Button(this,"保存设置",814,735,190,40,true,delegate{Save();});
            LoadSettings(app.Config);TrackChanges(this);SelectPage(3);
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(!AllowClose && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();app.TrayHint();}};
            Resize+=delegate{if(WindowState==FormWindowState.Minimized){Hide();WindowState=FormWindowState.Normal;}};
            Shown+=delegate{var area=Screen.FromControl(this).WorkingArea;if(Width>area.Width-24||Height>area.Height-24){Size=new Size(Math.Min(Width,area.Width-24),Math.Min(Height,area.Height-24));Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);}};
            AutoScaleDimensions=new SizeF(96,96);ResumeLayout(true);
        }
        void SelectPage(int index)
        {
            string[] names={"提醒规则","弹窗与声音","启动与记录","走动打卡","软件更新"};string[] notes={"安排专注与休息的节奏。","选择你喜欢的提醒方式。","自动启动、配置和最近的活动。","每一次起身，都值得被看见。","从 GitHub Releases 获取正式更新。"};
            for(int i=0;i<pages.Length;i++){pages[i].Visible=i==index;((NavigationButton)navigation[i]).Selected=i==index;}
            pageTitle.Text=names[index];pageSubtitle.Text=notes[index];
            bool settingsPage=index<3;statusCard.Visible=pageHost.Visible=previewButton.Visible=saveButton.Visible=feedback.Visible=settingsPage;
            if(index==3)dashboard.RefreshData();if(index==4)RefreshUpdateInfo();
        }
        void BuildUpdates(Control root)
        {
            var card=Theme.Card(root,0,0,738,250);
            var heading=Theme.Label(card,"GitHub 在线升级",20,16,698,34);heading.Font=Theme.Bold(15);
            var explain=Theme.Label(card,"当前版本 "+System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3),20,56,698,28);explain.ForeColor=Theme.Muted;
            Theme.Label(card,"发布仓库",20,96,698,26);
            updateRepository=new FieldTextBox(){Bounds=new Rectangle(20,130,698,38),Font=Font,MaxLength=200};card.Controls.Add(updateRepository);
            Theme.Button(card,"保存仓库",20,188,140,40,false,delegate{SaveRepository();});
            Theme.Button(card,"检查更新",538,188,180,40,true,delegate{if(SaveRepository())app.ShowUpdater();});
            var rules=Theme.Card(root,0,268,738,204);
            var title=Theme.Label(rules,"每天 09:00，安静地检查一次",20,16,698,30);title.Font=Theme.Bold(12);
            var note=Theme.Label(rules,"错过 09:00 时，在当天稍后启动客户端会补查。\n发现新版后，在下一次起立提醒中提示；由你选择是否安装。\n升级会保留提醒设置和打卡数据，并备份旧程序。",20,60,698,90);note.ForeColor=Theme.Muted;note.Font=Theme.Font(9.5f);
            updateInfo=Theme.Label(rules,"",20,156,698,34);updateInfo.Font=Theme.Font(9);updateInfo.AutoEllipsis=true;
            var local=Theme.Label(root,"填写 owner/psuam 或完整 GitHub 仓库链接。打卡数据仅保存在本机。",4,490,728,42);local.ForeColor=Theme.Muted;local.Font=Theme.Font(9);
        }
        bool SaveRepository()
        {
            try{string value=updateRepository.Text.Trim();if(value!="")value=UpdateService.NormalizeRepository(value);app.SetUpdateRepository(value);updateRepository.Text=value;loadedSettings.UpdateRepository=value;RefreshUpdateInfo();return true;}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"未能保存更新仓库",MessageBoxButtons.OK,MessageBoxIcon.Information);return false;}
        }
        public void RefreshActivities(){if(dashboard!=null&&!dashboard.IsDisposed)dashboard.RefreshData();}
        public void RefreshUpdateInfo(){if(updateInfo!=null)updateInfo.Text=app.UpdateStatusText;}
        public void ShowUpdatePage(){ShowSettings();SelectPage(4);}
        void Section(Control p,string text,int y){var label=Theme.Label(p,text,4,y,700,24);label.ForeColor=Theme.Muted;label.Font=Theme.Font(9);}
        void Divider(Control p,int y){p.Controls.Add(new Panel(){Location=new Point(20,y),Size=new Size(p.Width-40,1),BackColor=Theme.Line});}
        FieldTextBox TextInput(Control p,int x,int y,int w) {var c=new FieldTextBox(){Location=new Point(x,y),Size=new Size(w,36),Font=Font};p.Controls.Add(c);return c;}
        CheckBox Check(Control p,string text,int x,int y,int w) {var c=new SwitchCheck(){Text=text,Location=new Point(x,y),Size=new Size(w,32),AutoSize=false,Font=Font};p.Controls.Add(c);return c;}
        ChoiceBox Combo(Control p,int x,int y,int w,string[] items) {var c=new ChoiceBox(){Location=new Point(x,y),Size=new Size(w,36),Font=Font};c.Items.AddRange(items);p.Controls.Add(c);return c;}
        StepperBox Number(Control p,int x,int y,int w,int min,int max) {var c=new StepperBox(){Location=new Point(x,y),Size=new Size(w,36),Minimum=min,Maximum=max,Font=Font};p.Controls.Add(c);return c;}
        TimeBox Clock(Control p,int x,int y) {var c=new TimeBox(){Location=new Point(x,y),Size=new Size(112,36),Font=Font};p.Controls.Add(c);return c;}
        void BuildRules(Control root)
        {
            Section(root,"提醒计划",0);var p=Theme.Card(root,0,30,738,174);
            enabled=Check(p,"启用走动提醒",20,14,698);Divider(p,56);
            Theme.Label(p,"提醒方式",20,72,310,36);mode=Combo(p,360,72,358,new[]{"每小时的指定分钟","每隔若干分钟","每天的指定时刻"});Divider(p,118);
            modeLabel=Theme.Label(p,"分钟",20,130,320,36);minutes=TextInput(p,360,130,358);interval=Number(p,360,130,168,1,1440);daily=TextInput(p,360,130,358);
            scheduleHint=Theme.Label(root,"",6,210,726,38);scheduleHint.Font=Theme.Font(9);scheduleHint.ForeColor=Theme.Muted;
            Section(root,"提醒日期与时段",254);var q=Theme.Card(root,0,284,738,160);
            string[] names={"周日","周一","周二","周三","周四","周五","周六"};int[] order={1,2,3,4,5,6,0};
            for(int i=0;i<7;i++){int d=order[i];var chip=new SwitchCheck(){Chip=true,Text=names[d],Location=new Point(20+i*74,16),Size=new Size(64,36),Font=Theme.Font(10)};q.Controls.Add(chip);days[d]=chip;}
            var workdays=Theme.Button(q,"工作日",550,16,78,36,false,delegate{for(int i=0;i<7;i++)days[i].Checked=i>=1&&i<=5;});workdays.ForeColor=Theme.Blue;workdays.FlatAppearance.BorderSize=0;
            var every=Theme.Button(q,"每天",638,16,80,36,false,delegate{foreach(var d in days)d.Checked=true;});every.ForeColor=Theme.Blue;every.FlatAppearance.BorderSize=0;
            Divider(q,66);window=Check(q,"限制提醒时段",20,85,300);start=Clock(q,434,84);Theme.Label(q,"至",556,84,40,36);end=Clock(q,606,84);
            var hint=Theme.Label(q,"关闭后全天提醒；可设置跨午夜时段。",20,126,690,24);hint.ForeColor=Theme.Muted;hint.Font=Theme.Font(8.5f);
            window.CheckedChanged+=delegate{start.Enabled=end.Enabled=window.Checked;};mode.SelectedIndexChanged+=delegate{UpdateMode();};
        }
        void UpdateMode()
        {
            minutes.Visible=mode.SelectedIndex==0;interval.Visible=mode.SelectedIndex==1;daily.Visible=mode.SelectedIndex==2;
            modeLabel.Text=mode.SelectedIndex==0?"每小时的分钟":mode.SelectedIndex==1?"间隔（分钟）":"每天的时刻";
            scheduleHint.Text=mode.SelectedIndex==0?"填 30，每小时半点提醒；填 0,30，整点和半点提醒。":mode.SelectedIndex==1?"从保存或恢复提醒时开始计时，支持 1–1440 分钟。":"使用 24 小时制，多个时刻用逗号分隔，如 09:00,14:30。";
        }
        void BuildNotification(Control root)
        {
            Section(root,"提醒内容",0);var p=Theme.Card(root,0,30,738,154);
            Theme.Label(p,"标题",20,14,88,36);title=TextInput(p,116,14,602);title.MaxLength=60;Divider(p,60);
            Theme.Label(p,"正文",20,75,88,36);message=TextInput(p,116,74,602);message.Multiline=true;message.Size=new Size(602,66);message.MaxLength=400;message.ScrollBars=ScrollBars.Vertical;
            Section(root,"外观与显示",190);var q=Theme.Card(root,0,220,738,224);
            Theme.Label(q,"主题颜色",20,14,104,36);accent=Combo(q,124,14,212,new[]{"经典蓝","天空蓝","暖橙色","柔紫色"});
            Theme.Label(q,"弹窗位置",380,14,104,36);position=Combo(q,484,14,234,new[]{"右下角","左下角","右上角","左上角","屏幕中间"});position.AccessibleName="弹窗位置";Divider(q,58);
            Theme.Label(q,"正文字号",20,68,104,36);font=Number(q,124,68,140,9,18);Theme.Label(q,"磅",274,68,60,36);
            Theme.Label(q,"显示屏",380,68,104,36);monitor=Combo(q,484,68,234,new[]{"主显示屏","鼠标所在显示屏"});Divider(q,112);
            Theme.Label(q,"显示时长",20,122,104,36);duration=Number(q,124,122,140,0,300);Theme.Label(q,"秒",274,122,50,36);
            Theme.Label(q,"稍后提醒",380,122,104,36);snooze=Number(q,484,122,140,1,120);Theme.Label(q,"分钟",634,122,80,36);Divider(q,166);
            Theme.Label(q,"不透明度",20,176,104,36);opacity=Number(q,124,176,140,60,100);Theme.Label(q,"%",274,176,50,36);
            Theme.Label(q,"提示声音",380,176,104,36);sound=Combo(q,484,176,234,new[]{"静音","系统提示音","轻提示音"});
        }
        void BuildTools(Control root)
        {
            Section(root,"启动与配置",0);var p=Theme.Card(root,0,30,738,174);
            autostart=Check(p,"登录 Windows 时自动启动",20,12,698);Divider(p,54);hidden=Check(p,"启动时直接隐藏到托盘",20,66,698);Divider(p,110);
            Theme.Button(p,"导入配置",20,124,154,36,false,delegate{Import();});Theme.Button(p,"导出配置",184,124,154,36,false,delegate{Export();});Theme.Button(p,"数据文件夹",348,124,178,36,false,delegate{Try(delegate{Process.Start("explorer.exe",app.DataDirectory);});});
            Theme.Button(p,"恢复默认",536,124,182,36,false,delegate{LoadSettings(new Settings());dirty=true;SetFeedback("已载入默认预设，点击保存后生效。");});
            Section(root,"最近的活动 · 仅保留本机最近 300 条",224);var q=Theme.Card(root,0,254,738,190);
            history=new ListView(){Location=new Point(20,14),Size=new Size(698,110),View=View.Details,FullRowSelect=true,GridLines=false,HideSelection=false,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=Theme.Ink};history.Columns.Add("时间",162);history.Columns.Add("类型",86);history.Columns.Add("详情",426);q.Controls.Add(history);
            history.OwnerDraw=true;
            history.DrawColumnHeader+=delegate(object sender,DrawListViewColumnHeaderEventArgs e){e.DrawBackground();Typography.DrawText(e.Graphics,e.Header.Text,history.Font,Rectangle.Inflate(e.Bounds,-4,0),Theme.Muted,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);};
            history.DrawSubItem+=delegate(object sender,DrawListViewSubItemEventArgs e){bool selected=e.Item.Selected;e.Graphics.FillRectangle(selected?SystemBrushes.Highlight:Brushes.White,e.Bounds);Typography.DrawText(e.Graphics,e.SubItem.Text,history.Font,Rectangle.Inflate(e.Bounds,-4,0),selected?SystemColors.HighlightText:Theme.Ink,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
            Divider(q,136);Theme.Button(q,"刷新记录",20,146,154,32,false,delegate{RefreshHistory();});Theme.Button(q,"导出记录",184,146,154,32,false,delegate{ExportHistory();});
        }

        void TrackChanges(Control root)
        {
            foreach(Control c in root.Controls)
            {
                if(c is FieldTextBox)c.TextChanged+=Changed;
                else if(c is CheckBox)((CheckBox)c).CheckedChanged+=Changed;
                else if(c is ChoiceBox)((ChoiceBox)c).SelectedIndexChanged+=Changed;
                else if(c is StepperBox)((StepperBox)c).ValueChanged+=Changed;
                
                TrackChanges(c);
            }
        }
        void Changed(object s,EventArgs e){if(!loading){dirty=true;feedback.Text="有未保存的修改。测试可直接预览，正式提醒请先保存。";}}
        public void LoadSettings(Settings s)
        {
            loadedSettings=s.Copy();
            if(updateRepository!=null)updateRepository.Text=s.UpdateRepository;
            loading=true;enabled.Checked=s.Enabled;mode.SelectedIndex=s.Mode=="interval"?1:s.Mode=="daily"?2:0;minutes.Text=s.HourMinutes;interval.Value=s.IntervalMinutes;daily.Text=s.DailyTimes;
            for(int i=0;i<7;i++)days[i].Checked=s.Days.Contains(i);
            window.Checked=s.UseWindow;start.Value=DateTime.Today.Add(Schedule.Clock(s.WindowStart));end.Value=DateTime.Today.Add(Schedule.Clock(s.WindowEnd));start.Enabled=end.Enabled=s.UseWindow;
            title.Text=s.Title;message.Text=s.Message;duration.Value=s.DurationSeconds;snooze.Value=s.SnoozeMinutes;font.Value=s.FontSize;opacity.Value=s.OpacityPercent;
            accent.SelectedIndex=Array.IndexOf(new[]{"green","blue","orange","purple"},s.Accent);sound.SelectedIndex=Array.IndexOf(new[]{"none","asterisk","beep"},s.Sound);monitor.SelectedIndex=s.Monitor=="cursor"?1:0;
            position.SelectedIndex=Array.IndexOf(PositionValues,s.Position);
            autostart.Checked=s.AutoStart;hidden.Checked=s.StartHidden;UpdateMode();loading=false;dirty=false;RefreshHistory();
        }
        Settings Read()
        {
            var s=new Settings(){Enabled=enabled.Checked,Mode=new[]{"hourly","interval","daily"}[mode.SelectedIndex],Days=Enumerable.Range(0,7).Where(i=>days[i].Checked).ToArray(),UseWindow=window.Checked,Title=title.Text.Trim(),Message=message.Text.Trim(),DurationSeconds=(int)duration.Value,SnoozeMinutes=(int)snooze.Value,FontSize=(int)font.Value,OpacityPercent=(int)opacity.Value,Accent=new[]{"green","blue","orange","purple"}[accent.SelectedIndex],Sound=new[]{"none","asterisk","beep"}[sound.SelectedIndex],Monitor=monitor.SelectedIndex==1?"cursor":"primary",AutoStart=autostart.Checked,StartHidden=hidden.Checked};
            s.HourMinutes=ReadRule(delegate{var value=minutes.Text.Trim();Schedule.Minutes(value);return value;},mode.SelectedIndex==0,loadedSettings.HourMinutes);
            s.DailyTimes=ReadRule(delegate{var value=daily.Text.Trim();Schedule.Times(value);return value;},mode.SelectedIndex==2,loadedSettings.DailyTimes);
            s.IntervalMinutes=ReadRule(delegate{return (int)interval.Value;},mode.SelectedIndex==1,loadedSettings.IntervalMinutes);
            s.WindowStart=ReadRule(delegate{return start.Value.ToString("HH:mm",System.Globalization.CultureInfo.InvariantCulture);},window.Checked,loadedSettings.WindowStart);
            s.WindowEnd=ReadRule(delegate{return end.Value.ToString("HH:mm",System.Globalization.CultureInfo.InvariantCulture);},window.Checked,loadedSettings.WindowEnd);
            s.UpdateRepository=updateRepository.Text.Trim()==""?"":UpdateService.NormalizeRepository(updateRepository.Text.Trim());
            s.Position=PositionValues[position.SelectedIndex];s.Validate();return s;
        }
        T ReadRule<T>(Func<T> read,bool active,T saved){try{return read();}catch(ArgumentException){if(active)throw;return saved;}}
        void Save() {Try(delegate{var s=Read();if(s.Enabled && !Schedule.Next(s,DateTime.Now).HasValue)throw new ArgumentException("所选时刻与提醒时段没有交集，请调整时间规则。");app.Apply(s);loadedSettings=s.Copy();dirty=false;SetFeedback("设置已保存，提醒计划已更新。");});}
        void Import()
        {
            using(var d=new OpenFileDialog(){Filter="走动提醒配置 (*.json)|*.json",Title="导入配置"}) if(d.ShowDialog(this)==DialogResult.OK)Try(delegate{var s=new SettingsStore(d.FileName).Load();if(!File.Exists(d.FileName))throw new IOException("配置文件不存在。");s.AutoStart=app.Config.AutoStart;LoadSettings(s);dirty=true;SetFeedback("已导入，请检查后保存。开机启动选项保留当前值。");});
        }
        void Export()
        {
            Try(delegate{var s=Read();using(var d=new SaveFileDialog(){Filter="JSON 配置 (*.json)|*.json",FileName="走动提醒配置.json"})if(d.ShowDialog(this)==DialogResult.OK){File.WriteAllText(d.FileName,Json.Write(s),new UTF8Encoding(false));SetFeedback("配置已导出（包含当前未保存的设置）。");}});
        }
        void ExportHistory()
        {
            Try(delegate{using(var d=new SaveFileDialog(){Filter="CSV 记录 (*.csv)|*.csv",FileName="走动提醒记录.csv"})if(d.ShowDialog(this)==DialogResult.OK){app.ExportHistory(d.FileName);SetFeedback("记录已导出。");}});
        }
        public void RefreshHistory()
        {
            if(history==null)return;history.BeginUpdate();history.Items.Clear();foreach(var e in app.Events.AsEnumerable().Reverse()){var item=new ListViewItem(e.Time);item.SubItems.Add(e.Kind);item.SubItems.Add(e.Detail);history.Items.Add(item);}history.EndUpdate();
        }
        public void UpdateStatus()
        {
            var pause=app.Engine.PausedUntil;
            status.Text=!app.Config.Enabled?"● 提醒已停用":pause.HasValue&&pause>DateTime.Now?"● 已暂停，"+pause.Value.ToString("HH:mm")+" 恢复":"● 正在后台运行";
            status.ForeColor=!app.Config.Enabled?Theme.Muted:Theme.Green;
            var due=app.Engine.NextDue;next.Text=!app.Config.Enabled?"启用提醒并保存设置后，开始安排下一次提醒。":pause.HasValue?"暂停期间不提醒；到时自动恢复。":due.HasValue?"下次提醒  "+due.Value.ToString("MM月dd日 dddd HH:mm:ss")+"    ·    "+Remaining(due.Value):"当前规则没有可用的提醒时间，请调整设置。";
        }
        string Remaining(DateTime due){var seconds=Math.Max(0,(int)(due-DateTime.Now).TotalSeconds);return seconds>=3600?(seconds/3600)+" 小时 "+((seconds%3600)/60)+" 分钟后":(seconds/60)+" 分 "+(seconds%60)+" 秒后";}
        void SetFeedback(string text){feedback.Text=text;}
        void Try(Action action) {try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"请检查设置",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
        public void ShowSettings(){Show();WindowState=FormWindowState.Normal;Activate();UpdateStatus();RefreshHistory();RefreshActivities();RefreshUpdateInfo();if(dirty)SetFeedback("有未保存的修改。测试可直接预览，正式提醒请先保存。");}
    }
}
