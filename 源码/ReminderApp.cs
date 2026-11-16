using System;
using System.IO;
using System.IO.Pipes;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;
namespace QingMove
{
    public class LogEntry {public string Time;public string Kind;public string Detail;}
    public class ReminderApp:ApplicationContext
    {
        public Settings Config {get;private set;}
        public ReminderEngine Engine {get;private set;}
        public List<LogEntry> Events {get;private set;}
        public string DataDirectory {get;private set;}
        public ActivityStore Activity {get;private set;}
        UpdateSchedule updateSchedule;
        readonly CancellationTokenSource updateLifetime=new CancellationTokenSource();
        bool checkingUpdate;
        string updateStatus="每天 09:00 自动检查；也可随时手动检查。",activityFailure;
        UpdateForm updater;
        public IList<ActivityRecord> GetActivities(){return Activity==null?new List<ActivityRecord>():Activity.Snapshot();}
        public string ActivityWarning {get{return activityFailure??(Activity==null?null:Activity.Warning);}}
        public string UpdateStatusText {get{return UpdateHint??(updateSchedule!=null&&updateSchedule.Warning!=null?updateSchedule.Warning:updateStatus);}}
        public string UpdateHint {get{Version v;if(Config==null||updateSchedule==null||!string.Equals(Config.UpdateRepository,updateSchedule.CachedRepository,StringComparison.OrdinalIgnoreCase)||!Version.TryParse(updateSchedule.CachedVersion,out v)||v<=Assembly.GetExecutingAssembly().GetName().Version)return null;return "新版本 "+new Version(v.Major,v.Minor,Math.Max(0,v.Build)).ToString(3)+" 可更新";}}
        readonly SettingsStore store;
        readonly SettingsForm form;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer clock=new System.Windows.Forms.Timer(){Interval=1000};
        ToastForm toast;
        string startupWarning;
        volatile bool exiting;
        NamedPipeServerStream pendingPipe;
        readonly object pipeLock=new object();
        const string StartupKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run",StartupValue="QingMoveReminder";
        public ReminderApp(string command)
        {
            DataDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QingMoveReminder");Directory.CreateDirectory(DataDirectory);
            store=new SettingsStore(Path.Combine(DataDirectory,"settings.json"));
            try {Config=store.Load();}
            catch(Exception ex)
            {
                Config=new Settings();startupWarning="原配置无法读取，已使用默认设置。原文件保留，可在数据文件夹检查。\r\n"+ex.Message;
                try{File.Copy(store.FilePath,store.FilePath+".invalid-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),true);}catch{}
            }
            Events=new List<LogEntry>();
            try {var path=Path.Combine(DataDirectory,"history.json");if(File.Exists(path)&&new FileInfo(path).Length<=2*1024*1024)Events=Json.Read<List<LogEntry>>(File.ReadAllText(path,Encoding.UTF8))??Events;Events=Events.Where(x=>x!=null).Take(300).ToList();}catch{}
            Engine=new ReminderEngine(Config,DateTime.Now);
            Activity=new ActivityStore(Path.Combine(DataDirectory,"activity"));
            updateSchedule=new UpdateSchedule(Path.Combine(DataDirectory,"update-state.json"));
            form=new SettingsForm(this);var handle=form.Handle;
            tray=new NotifyIcon(){Text="走动提醒 · 正在运行",Icon=Theme.AppIcon(),Visible=true};
            var menu=new ContextMenuStrip(){Font=Theme.Font(10),Renderer=new CrispMenuRenderer()};menu.Items.Add("打开设置",null,delegate{form.ShowSettings();});menu.Items.Add("立即测试通知",null,delegate{ShowToast(Config,true);});menu.Items.Add(new ToolStripSeparator());
            foreach(int n in new[]{15,30,60}){int mins=n;menu.Items.Add("暂停 "+n+" 分钟",null,delegate{Pause(mins);});}
            menu.Items.Add("立即恢复提醒",null,delegate{Resume();});menu.Items.Add(new ToolStripSeparator());menu.Items.Add("完全退出",null,delegate{Quit();});tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{form.ShowSettings();};
            clock.Tick+=delegate{if(Engine.Tick(DateTime.Now))ShowToast(Config,false);form.UpdateStatus();UpdateTray();CheckDailyUpdate();};clock.Start();
            var thread=new Thread(Listen){IsBackground=true,Name="QingMove command listener"};thread.Start();
            form.BeginInvoke((Action)delegate
            {
                if(command=="--show" || (command=="" && !Config.StartHidden))form.ShowSettings();
                if(command!="" && command!="--show" && command!="--background")Dispatch(command);
                if(startupWarning!=null)MessageBox.Show(startupWarning,"走动提醒 · 配置已恢复",MessageBoxButtons.OK,MessageBoxIcon.Information);
                form.UpdateStatus();
                CheckDailyUpdate();
            });
            Log("启动","程序已启动");
        }
        void Listen()
        {
            while(!exiting)
            {
                try
                {
                    var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(Program.UserId),PipeAccessRights.FullControl,AccessControlType.Allow));
                    using(var pipe=new NamedPipeServerStream(Program.PipeName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.None,1024,1024,security))
                    {
                        lock(pipeLock){if(exiting)return;pendingPipe=pipe;}
                        pipe.WaitForConnection();string cmd;
                        using(var reader=new StreamReader(pipe)){cmd=reader.ReadLine();}
                        if(cmd!=null && cmd.Length<=64 && !exiting)form.BeginInvoke((Action)delegate{Dispatch(cmd);});
                        lock(pipeLock){pendingPipe=null;}
                    }
                }
                catch {if(!exiting)Thread.Sleep(200);}
            }
        }
        void Dispatch(string cmd)
        {
            if(cmd=="--test")ShowToast(Config,true);else if(cmd=="--show")form.ShowSettings();else if(cmd=="--resume")Resume();else if(cmd=="--exit")Quit();
            else if(cmd.StartsWith("--pause=")){int n;if(int.TryParse(cmd.Substring(8),out n)&&n>=1&&n<=1440)Pause(n);}
        }
        public void Apply(Settings settings)
        {
            settings.Validate();object previous;
            using(var key=Registry.CurrentUser.CreateSubKey(StartupKey))
            {
                previous=key.GetValue(StartupValue);
                try
                {
                    if(settings.AutoStart)key.SetValue(StartupValue,"\""+Application.ExecutablePath+"\" --background",RegistryValueKind.String);else key.DeleteValue(StartupValue,false);
                    store.Save(settings);
                }
                catch {if(previous==null)key.DeleteValue(StartupValue,false);else key.SetValue(StartupValue,previous);throw;}
            }
            Config=settings.Copy();Engine=Engine.Reconfigure(Config,DateTime.Now);Log("设置","设置已保存，提醒计划已更新");form.UpdateStatus();UpdateTray();
        }
        public void ShowToast(Settings settings,bool test)
        {
            settings.Validate();if(toast!=null && !toast.IsDisposed)toast.ReplaceClose();
            Log(test?"测试":"提醒",settings.Title);
            DateTimeOffset shown=DateTimeOffset.Now;string activityId=null;
            if(!test)try{activityId=Activity.Begin(shown);activityFailure=null;}catch(Exception ex){activityFailure="打卡记录未能保存："+ex.Message;Log("记录失败",ex.Message);}
            Func<string,bool> save=delegate(string action)
            {
                if(test)return true;
                try{if(activityId==null)activityId=Activity.Begin(shown);if(!Activity.Complete(activityId,action,DateTimeOffset.Now))throw new InvalidOperationException("此提醒已有终态记录，请关闭弹窗后查看打卡明细。");activityFailure=null;if(form!=null)form.RefreshActivities();return true;}
                catch(Exception ex){activityFailure="打卡记录未能保存："+ex.Message;Log("记录失败",ex.Message);if(form!=null)form.RefreshActivities();return false;}
            };
            toast=new ToastForm(settings,test,delegate(string action)
            {
                if(action=="snooze") {Engine.Snooze(DateTime.Now,settings.SnoozeMinutes);Log("稍后",settings.SnoozeMinutes+" 分钟后再次提醒"+(test?"（测试通知）":""));}
                else if(action=="done")Log(test?"测试确认":"已起身",test?"已确认测试弹窗":"已确认起身活动");
                else if(action=="work")Log(test?"测试确认":"继续工作",test?"已确认测试弹窗":"本次选择继续工作");
                else if(action=="timeout")Log("自动关闭",test?"测试通知已自动关闭":"提醒已自动关闭");
                else Log("关闭",test?"测试通知已关闭":"提醒已手动关闭");
                if(form!=null)form.UpdateStatus();
            },test?null:UpdateHint,ShowUpdater,save);
            toast.Show(form);
            if(form!=null)form.RefreshActivities();
            if(settings.Sound=="asterisk")System.Media.SystemSounds.Asterisk.Play();else if(settings.Sound=="beep")System.Media.SystemSounds.Beep.Play();
        }
        public void Pause(int minutes)
        {
            Engine.Pause(DateTime.Now,minutes);if(toast!=null&&!toast.IsDisposed)toast.ReplaceClose("paused");Log("暂停",minutes+" 分钟后自动恢复");form.UpdateStatus();UpdateTray();
        }
        public void Resume() {Engine.Resume(DateTime.Now);Log("恢复","已恢复提醒，下一次按当前规则安排");form.UpdateStatus();UpdateTray();}
        void UpdateTray() {string text=!Config.Enabled?"走动提醒 · 已停用":Engine.PausedUntil.HasValue?"走动提醒 · 已暂停至 "+Engine.PausedUntil.Value.ToString("HH:mm"):"走动提醒 · 下次 "+(Engine.NextDue.HasValue?Engine.NextDue.Value.ToString("MM-dd HH:mm"):"无可用时刻");tray.Text=text;}
        public void TrayHint() {tray.ShowBalloonTip(2500,"走动提醒仍在后台运行","双击托盘图标打开设置；右键菜单可以完全退出。",ToolTipIcon.Info);}
        public void Log(string kind,string detail)
        {
            Events.Add(new LogEntry(){Time=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),Kind=kind,Detail=detail});if(Events.Count>300)Events.RemoveRange(0,Events.Count-300);
            try {var p=Path.Combine(DataDirectory,"history.json");File.WriteAllText(p,Json.Write(Events),new UTF8Encoding(false));}catch{}
            if(form!=null && !form.IsDisposed)form.RefreshHistory();
        }
        public void ExportHistory(string path)
        {
            Func<string,string> csv=delegate(string s){s=s??"";if(s.Length>0 && "=+-@\t\r".Contains(s[0]))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";};
            var lines=new List<string>{"时间,类型,详情"};lines.AddRange(Events.Select(x=>csv(x.Time)+","+csv(x.Kind)+","+csv(x.Detail)));File.WriteAllLines(path,lines,new UTF8Encoding(true));
        }
        public void ReportError(Exception ex) {Log("错误",ex.Message);MessageBox.Show("操作未能完成："+ex.Message,"走动提醒",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        public void SetUpdateRepository(string repository)
        {
            if(repository!="")repository=UpdateService.NormalizeRepository(repository);
            var next=Config.Copy();next.UpdateRepository=repository;next.Validate();store.Save(next);Config=next;
            updateStatus=repository==""?"请先填写发布仓库。":"仓库已保存；每天 09:00 自动检查。";Log("更新仓库",repository);form.RefreshUpdateInfo();CheckDailyUpdate();
        }
        public void ShowUpdater()
        {
            if(exiting)return;if(string.IsNullOrWhiteSpace(Config.UpdateRepository)){form.ShowUpdatePage();return;}
            if(updater!=null&&!updater.IsDisposed){updater.Show();updater.Activate();return;}
            string repo=Config.UpdateRepository;
            updater=new UpdateForm(repo,Quit,delegate(UpdateRelease release){RememberRelease(repo,release);});updater.Show(form);
        }
        void RememberRelease(string repo,UpdateRelease release)
        {
            if(exiting||!string.Equals(repo,Config.UpdateRepository,StringComparison.OrdinalIgnoreCase))return;
            if(release.IsNewer)updateSchedule.RememberAvailable(repo,release.Version.ToString(),release.TagName,release.Notes,release.PageUrl);else updateSchedule.ClearAvailable(repo);
            updateStatus=release.IsNewer?"发现新版本，将在下一次起立提醒中提示。":"当前已是最新版本 · "+DateTime.Now.ToString("MM-dd HH:mm");form.RefreshUpdateInfo();
        }
        async void CheckDailyUpdate()
        {
            if(exiting||checkingUpdate||string.IsNullOrWhiteSpace(Config.UpdateRepository))return;
            string repo=Config.UpdateRepository;
            if(!updateSchedule.TryBegin(DateTime.Now,repo))return;checkingUpdate=true;updateStatus="正在检查新版本…";form.RefreshUpdateInfo();
            try{using(var service=new UpdateService()){var result=await service.CheckAsync(repo,Assembly.GetExecutingAssembly().GetName().Version,updateLifetime.Token);RememberRelease(repo,result);}}
            catch(OperationCanceledException){}
            catch(Exception ex){if(!exiting&&string.Equals(repo,Config.UpdateRepository,StringComparison.OrdinalIgnoreCase)){updateStatus="今日自动检查未完成，可手动重试："+ex.Message;Log("检查更新",ex.Message);form.RefreshUpdateInfo();}}
            finally{checkingUpdate=false;}
        }
        public void Quit()
        {
            if(exiting)return;exiting=true;clock.Stop();updateLifetime.Cancel();if(updater!=null&&!updater.IsDisposed)updater.Close();Log("退出","程序已完全退出");lock(pipeLock){if(pendingPipe!=null)pendingPipe.Dispose();}
            tray.Visible=false;tray.Dispose();if(toast!=null&&!toast.IsDisposed)toast.ReplaceClose("exit");form.AllowClose=true;form.Close();form.Dispose();ExitThread();
        }
        protected override void Dispose(bool disposing) {if(disposing){if(!exiting)Quit();clock.Dispose();}base.Dispose(disposing);}
    }
}
