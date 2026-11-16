using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using QingMove;

class ActivityIntegrationTests
{
    static readonly BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
    static string root;
    static string previews;
    static string[] filters=new string[0];
    [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    static int passed,failed,assertions;
    static void Equal<T>(T expected,T actual,string label)
    {assertions++;if(!object.Equals(expected,actual))throw new Exception(label+": expected "+expected+", got "+actual);}
    static void True(bool value,string label){Equal(true,value,label);}
    static void Case(string name,Action body)
    {if(filters.Length>0&&!filters.Any(x=>name.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0))return;try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex);}}
    static string DirectoryForCase()
    {string dir=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);return dir;}
    static DateTimeOffset At(string value)
    {return DateTimeOffset.Parse(value,CultureInfo.InvariantCulture);}
    static T Field<T>(object owner,string name)
    {var field=owner.GetType().GetField(name,PrivateInstance);if(field==null)throw new Exception("Missing integration field: "+name);return (T)field.GetValue(owner);}
    static void SetProperty(object owner,string name,object value)
    {var field=owner.GetType().GetField("<"+name+">k__BackingField",PrivateInstance);if(field==null)throw new Exception("Missing auto property: "+name);field.SetValue(owner,value);}
    static void InjectType(object owner,Type type,object value,bool required)
    {var fields=owner.GetType().GetFields(PrivateInstance).Where(x=>x.FieldType==type).ToArray();if(required&&fields.Length!=1)throw new Exception("Expected one injectable "+type.Name+" field; got "+fields.Length);foreach(var field in fields)field.SetValue(owner,value);}
    static IEnumerable<Control> Descendants(Control parent)
    {foreach(Control child in parent.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    static Button Button(Control parent,string starts)
    {return Descendants(parent).OfType<Button>().Single(x=>x.Text.StartsWith(starts,StringComparison.Ordinal));}
    static ToastForm Toast(Settings settings,bool preview,Action<string> action,Func<string,bool> save,string updateHint=null,Action onUpdate=null)
    {
        var constructor=typeof(ToastForm).GetConstructors().Single(x=>x.GetParameters().Any(p=>p.ParameterType==typeof(Func<string,bool>)));
        var parameters=constructor.GetParameters();var args=new object[parameters.Length];
        for(int i=0;i<parameters.Length;i++)
        {
            Type type=parameters[i].ParameterType;
            if(type==typeof(Settings))args[i]=settings;
            else if(type==typeof(bool))args[i]=preview;
            else if(type==typeof(Action<string>))args[i]=action;
            else if(type==typeof(Func<string,bool>))args[i]=save;
            else if(type==typeof(string))args[i]=updateHint;
            else if(type==typeof(Action))args[i]=onUpdate;
            else if(parameters[i].IsOptional)args[i]=parameters[i].DefaultValue;
            else throw new Exception("Unexpected required ToastForm parameter "+parameters[i].Name);
        }
        return (ToastForm)constructor.Invoke(args);
    }
    static Settings Config(){return new Settings{DurationSeconds=0,Sound="none"};}
    static void Show(ToastForm toast){toast.Show();Application.DoEvents();True(toast.Visible,"toast appears");}
    static void FinishAgain(ToastForm toast,string outcome)
    {typeof(ToastForm).GetMethod("Finish",PrivateInstance).Invoke(toast,new object[]{outcome});Application.DoEvents();}

    // This fixture avoids the real app constructor, tray, background listener, registry and AppData.
    sealed class AppFixture:IDisposable
    {
        public readonly ReminderApp App;
        public readonly ActivityStore Activity;
        public readonly string DirectoryPath;
        public AppFixture()
        {
            DirectoryPath=DirectoryForCase();Activity=new ActivityStore(Path.Combine(DirectoryPath,"activity"));
            App=(ReminderApp)FormatterServices.GetUninitializedObject(typeof(ReminderApp));
            Settings config=Config();SetProperty(App,"Config",config);SetProperty(App,"Engine",new ReminderEngine(config,DateTime.Now));SetProperty(App,"Events",new List<LogEntry>());SetProperty(App,"DataDirectory",DirectoryPath);
            InjectType(App,typeof(ActivityStore),Activity,true);
            InjectType(App,typeof(UpdateSchedule),new UpdateSchedule(Path.Combine(DirectoryPath,"updates.json")),false);
        }
        public ToastForm CurrentToast{get{return Field<ToastForm>(App,"toast");}}
        public void Dispose()
        {
            ToastForm toast=CurrentToast;if(toast!=null&&!toast.IsDisposed){toast.ReplaceClose();toast.Dispose();}
            Application.DoEvents();
            // Do not Dispose the deliberately uninitialized ApplicationContext.
        }
    }

    static void Run()
    {
        // A real primary button must durably save before notifying its caller and closing.
        foreach(string outcome in new[]{"done","work"})
        {
            string expectedOutcome=outcome;
            Case("primary "+outcome+" writes durable decision exactly once",delegate{
                string dir=DirectoryForCase();var store=new ActivityStore(dir);DateTimeOffset shown=At("2026-11-16T09:00:00+08:00");string id=store.Begin(shown);int saves=0,callbacks=0;string result=null;
                using(var toast=Toast(Config(),false,delegate(string action){callbacks++;result=action;Equal(expectedOutcome,new ActivityStore(dir).Snapshot()[0].Outcome,"durable before callback");},delegate(string action){saves++;return store.Complete(id,action,shown.AddMinutes(1));}))
                {
                    Show(toast);Button(toast,expectedOutcome=="done"?"我已起身":"继续工作").PerformClick();Application.DoEvents();Equal(expectedOutcome,result,"button outcome");Equal(false,toast.Visible,"saved decision closes toast");Equal(1,saves,"one persistence attempt");Equal(1,callbacks,"one success callback");long revision=store.Revision;
                    FinishAgain(toast,expectedOutcome=="done"?"work":"done");Equal(1,saves,"duplicate finish does not persist");Equal(1,callbacks,"duplicate finish does not notify");Equal(revision,store.Revision,"duplicate finish revision");Equal(1,new ActivityStore(dir).Snapshot().Count,"one stored reminder");Equal(expectedOutcome,new ActivityStore(dir).Snapshot()[0].Outcome,"first final decision retained");
                }
            });
        }
        // Returning false on a real locked-file error must leave the reminder open and retryable.
        Case("failed decision save leaves toast open for retry",delegate{
            string dir=DirectoryForCase();var store=new ActivityStore(dir);var shown=At("2026-11-16T09:00:00+08:00");string id=store.Begin(shown);int calls=0,saves=0;long revision=store.Revision;
            using(var toast=Toast(Config(),false,delegate{calls++;},delegate(string action){saves++;try{return store.Complete(id,action,shown.AddMinutes(1));}catch(IOException){return false;}}))
            {
                Show(toast);
                using(var locked=new FileStream(Path.Combine(dir,"2026-11.jsonl"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                {Button(toast,"继续工作").PerformClick();Application.DoEvents();True(toast.Visible&&!toast.IsDisposed,"failed save stays visible");Equal(0,calls,"failed save has no success callback");Equal("pending",store.Snapshot()[0].Outcome,"failed save stays pending");Equal(revision,store.Revision,"failed save does not advance revision");}
                Button(toast,"继续工作").PerformClick();Application.DoEvents();Equal(false,toast.Visible,"successful retry closes");Equal(2,saves,"retry attempted");Equal(1,calls,"one eventual success callback");Equal("work",new ActivityStore(dir).Snapshot()[0].Outcome,"retry actually durable");
            }
        });
        // Exercises the real ShowToast preview branch, rather than assuming standalone Toast knows storage policy.
        Case("application previews never enter activity history",delegate{
            using(var app=new AppFixture())
            {
                long revision=app.Activity.Revision;
                foreach(string outcome in new[]{"done","work"})
                {
                    app.App.ShowToast(Config(),true);Application.DoEvents();ToastForm toast=app.CurrentToast;True(toast.Visible,"app preview shown");Button(toast,outcome=="done"?"我已起身":"继续工作").PerformClick();Application.DoEvents();Equal(0,app.Activity.Snapshot().Count,"preview excluded");Equal(revision,app.Activity.Revision,"preview does not reserve activity");
                }
                string activityDirectory=Path.Combine(app.DirectoryPath,"activity");Equal(0,Directory.Exists(activityDirectory)?Directory.GetFiles(activityDirectory,"*.jsonl").Length:0,"preview creates no monthly files");
            }
        });
        // Catches integration accidentally excluding formal reminders along with previews.
        Case("application formal show and primary action reload correctly",delegate{
            using(var app=new AppFixture())
            {
                app.App.ShowToast(Config(),false);Application.DoEvents();Equal(1,app.Activity.Snapshot().Count,"formal show begins reminder");Equal("pending",app.Activity.Snapshot()[0].Outcome,"formal starts pending");Button(app.CurrentToast,"我已起身").PerformClick();Application.DoEvents();var reloaded=new ActivityStore(Path.Combine(app.DirectoryPath,"activity"));Equal(1,reloaded.Snapshot().Count,"formal reload count");Equal("done",reloaded.Snapshot()[0].Outcome,"formal done persisted");
            }
        });
        Case("application failed initial write stays retryable without claiming success",delegate{
            using(var app=new AppFixture())
            {
                string activityDirectory=Path.Combine(app.DirectoryPath,"activity");Directory.CreateDirectory(activityDirectory);string month=Path.Combine(activityDirectory,DateTimeOffset.Now.ToString("yyyy-MM",CultureInfo.InvariantCulture)+".jsonl");File.WriteAllText(month,"");
                using(var locked=new FileStream(month,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                {
                    app.App.ShowToast(Config(),false);Application.DoEvents();Equal(0,app.Activity.Snapshot().Count,"failed initial write creates no in-memory record");True(!string.IsNullOrEmpty(app.App.ActivityWarning),"initial failure visible");Button(app.CurrentToast,"我已起身").PerformClick();Application.DoEvents();True(app.CurrentToast.Visible,"app save failure keeps toast open");Equal(0,app.Activity.Snapshot().Count,"failed action creates no record");Equal(false,app.App.Events.Any(x=>x.Kind=="已起身"),"no successful standing log on failure");
                }
                Button(app.CurrentToast,"我已起身").PerformClick();Application.DoEvents();Equal(false,app.CurrentToast.Visible,"app retry closes after save");Equal(1,app.Activity.Snapshot().Count,"one record after recovery");Equal("done",new ActivityStore(activityDirectory).Snapshot()[0].Outcome,"app recovery durable");
            }
        });
        Case("terminal record conflict rejects a different toast decision",delegate{
            using(var app=new AppFixture())
            {
                app.App.ShowToast(Config(),false);Application.DoEvents();ActivityRecord pending=app.Activity.Snapshot().Single();Equal("pending",pending.Outcome,"formal record begins pending");True(app.Activity.Complete(pending.Id,"work",DateTimeOffset.Now),"external first final decision");long revision=app.Activity.Revision;
                Button(app.CurrentToast,"我已起身").PerformClick();Application.DoEvents();True(app.CurrentToast.Visible&&!app.CurrentToast.IsDisposed,"false completion does not close reminder");Equal("work",app.Activity.Snapshot().Single().Outcome,"existing terminal decision preserved");Equal(revision,app.Activity.Revision,"conflicting decision does not append");Equal(false,app.App.Events.Any(x=>x.Kind=="已起身"),"conflicting done has no successful standing log");True(!string.IsNullOrEmpty(app.App.ActivityWarning),"terminal conflict visible");Equal("work",new ActivityStore(Path.Combine(app.DirectoryPath,"activity")).Snapshot().Single().Outcome,"durable first final retained");
            }
        });
        Case("available update appears on next formal reminder only",delegate{
            using(var app=new AppFixture())
            {
                app.App.Config.UpdateRepository="owner/repo";app.App.ShowToast(Config(),false);Application.DoEvents();ToastForm first=app.CurrentToast;Equal(false,Descendants(first).OfType<Button>().Any(x=>x.Text.Contains("新版本")),"no hint before release discovery");
                var schedule=Field<UpdateSchedule>(app.App,"updateSchedule");schedule.RememberAvailable("owner/repo","9.0.0","v9.0.0","演示版本更新说明","https://github.com/owner/repo/releases/tag/v9.0.0");True(app.App.UpdateHint!=null&&app.App.UpdateHint.Contains("9.0.0"),"application exposes newer cached version");Equal(false,Descendants(first).OfType<Button>().Any(x=>x.Text.Contains("新版本")),"open reminder is not changed dynamically");
                app.App.ShowToast(Config(),false);Application.DoEvents();True(Descendants(app.CurrentToast).OfType<Button>().Any(x=>x.Text.Contains("新版本 9.0.0")),"next formal reminder contains update link");Equal(2,app.Activity.Snapshot().Count,"two formal reminders only");Equal("replaced",app.Activity.Snapshot()[0].Outcome,"previous unanswered reminder replaced");
                app.App.ShowToast(Config(),true);Application.DoEvents();Equal(false,Descendants(app.CurrentToast).OfType<Button>().Any(x=>x.Text.Contains("新版本")),"preview omits cached update link");Equal(2,app.Activity.Snapshot().Count,"preview still excluded with cached update");
            }
        });
        Case("basic cached version cannot crash reminder hint",delegate{
            using(var app=new AppFixture())
            {
                app.App.Config.UpdateRepository="owner/repo";var schedule=Field<UpdateSchedule>(app.App,"updateSchedule");schedule.RememberAvailable("owner/repo","9.0","v9.0","cached notification fixture","https://github.com/owner/repo/releases/tag/v9.0");True(!string.IsNullOrEmpty(app.App.UpdateHint),"two-component newer cache remains displayable");app.App.ShowToast(Config(),false);Application.DoEvents();True(app.CurrentToast.Visible,"valid basic cached version does not block reminder");
            }
        });
        Case("dashboard four period controls and visible summaries",delegate{
            string dir=DirectoryForCase();var store=DashboardData(dir);
            using(var host=new Form{ClientSize=new Size(780,680),ShowInTaskbar=false})
            using(var dashboard=new ActivityDashboard(store.Snapshot,delegate{return store.Warning;}))
            {
                host.Controls.Add(dashboard);host.Show();Application.DoEvents();dashboard.SelectPeriod(ActivityPeriod.Day,new DateTime(2024,2,29));
                string[] names={"日","周","月","年"};int[] totals={2,4,3,5},bucketCounts={24,7,29,12},done={1,1,1,2},unanswered={0,1,1,1},pending={0,1,0,1},active={1,1,1,2};
                for(int i=0;i<4;i++)
                {
                    Descendants(dashboard).OfType<Button>().Single(x=>x.Text==names[i]).PerformClick();Application.DoEvents();Equal((ActivityPeriod)i,dashboard.Period,"segment selection");var report=dashboard.Report;Equal(totals[i],report.Total,"period total");Equal(bucketCounts[i],report.Buckets.Count,"period buckets");Equal(done[i],report.Done,"period done");Equal(1,report.Work,"period work");Equal(unanswered[i],report.Unanswered,"period unanswered");Equal(pending[i],report.Pending,"period pending");Equal(active[i],report.ActiveDays,"period active days");var values=Field<Label[]>(dashboard,"values");Equal(done[i].ToString(),values[0].Text,"visible done summary");Equal("1",values[1].Text,"visible work summary");Equal(unanswered[i].ToString(),values[2].Text,"visible unanswered summary");Equal(active[i].ToString(),values[3].Text,"visible active days summary");Equal(totals[i],Field<ListView>(dashboard,"details").Items.Count,"visible detail count");
                }
                host.Close();
            }
        });
        Case("dashboard CSV exports current period with readable UTF8",delegate{
            string dir=DirectoryForCase();var store=DashboardData(dir);
            using(var dashboard=new ActivityDashboard(store.Snapshot,delegate{return null;}))
            {
                dashboard.SelectPeriod(ActivityPeriod.Day,new DateTime(2024,2,29));string csv=Path.Combine(dir,"打卡导出.csv");dashboard.ExportTo(csv);string[] lines=File.ReadAllLines(csv,Encoding.UTF8);Equal(3,lines.Length,"header plus current day only");Equal("提醒时间,我的选择,回应时间",lines[0],"Chinese header");True(lines[1].Contains("我已起身"),"done label exported");True(lines[2].Contains("继续工作"),"work label exported");True(lines[1].Contains("2024-02-29T09:00:00.0000000+08:00"),"shown offset preserved");True(lines[1].Contains("2024-02-29T09:01:00.0000000+08:00"),"response timestamp exported");byte[] bytes=File.ReadAllBytes(csv);True(bytes.Length>3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191,"UTF8 BOM for spreadsheet opening");
                dashboard.SelectPeriod(ActivityPeriod.Month,new DateTime(2024,3,1));dashboard.ExportTo(csv);lines=File.ReadAllLines(csv,Encoding.UTF8);Equal(2,lines.Length,"one March pending reminder");True(lines[1].EndsWith(",\"待回应\",\"\"",StringComparison.Ordinal),"pending response blank");
            }
        });
        Case("dashboard refresh reflects saved actions and storage warning",delegate{
            string dir=DirectoryForCase();var store=new ActivityStore(dir);string warning=null;
            using(var dashboard=new ActivityDashboard(store.Snapshot,delegate{return warning;}))
            {
                dashboard.SelectPeriod(ActivityPeriod.Day,new DateTime(2024,2,29));Equal(0,dashboard.Report.Total,"empty day total");Equal(1,Field<ListView>(dashboard,"details").Items.Count,"empty state row");var now=At("2024-02-29T09:00:00+08:00");string id=store.Begin(now);dashboard.RefreshData();Equal(1,dashboard.Report.Pending,"new pending visible");store.Complete(id,"done",now.AddMinutes(1));dashboard.RefreshData();Equal(1,dashboard.Report.Done,"saved decision visible");Equal(0,dashboard.Report.Pending,"pending cleared");Equal("我已起身",Field<ListView>(dashboard,"details").Items[0].SubItems[1].Text,"detail shows real decision");warning="测试：记录目录暂时无法写入";dashboard.RefreshData();Equal(warning,Field<Label>(dashboard,"notice").Text,"storage warning surfaced");
            }
        });
        Case("chart year and month bars drill into selected period",delegate{
            string dir=DirectoryForCase();var store=DashboardData(dir);
            using(var host=new Form{ClientSize=new Size(780,680),ShowInTaskbar=false})
            using(var dashboard=new ActivityDashboard(store.Snapshot,delegate{return null;}))
            {
                host.Controls.Add(dashboard);host.Show();Application.DoEvents();dashboard.SelectPeriod(ActivityPeriod.Year,new DateTime(2024,2,29));ClickBucket(dashboard,1);Equal(ActivityPeriod.Month,dashboard.Period,"year drills to month");Equal(new DateTime(2024,2,1),dashboard.Report.Start,"February selected");ClickBucket(dashboard,28);Equal(ActivityPeriod.Day,dashboard.Period,"month drills to day");Equal(new DateTime(2024,2,29),dashboard.Report.Start,"leap day selected");Equal(2,dashboard.Report.Total,"drilled day contains both choices");host.Close();
            }
        });
        Case("stale chart hits are ignored until new period has painted",delegate{
            using(var chart=new ActivityChart{Size=new Size(712,170),BackColor=Color.White,Font=Theme.Font(10)})
            {
                var month=ActivityStats.Build(new ActivityRecord[0],ActivityPeriod.Month,new DateTime(2024,7,1));var week=ActivityStats.Build(new ActivityRecord[0],ActivityPeriod.Week,new DateTime(2024,7,1));chart.SetReport(month,ActivityPeriod.Month);
                using(var bitmap=new Bitmap(chart.Width,chart.Height))chart.DrawToBitmap(bitmap,chart.ClientRectangle);
                var oldHits=Field<List<RectangleF>>(chart,"hits").ToArray();Equal(31,oldHits.Length,"old month has 31 hit regions");int hovered=0,clicked=0;DateTime selected=DateTime.MinValue;chart.BucketHovered+=delegate{hovered++;};chart.BucketClicked+=delegate(DateTime date){clicked++;selected=date;};
                chart.SetReport(week,ActivityPeriod.Week);
                foreach(int index in new[]{20,2})
                {Point point=Center(oldHits[index]);MouseEvent(chart,"OnMouseMove",point);MouseEvent(chart,"OnMouseClick",point);}
                Equal(0,hovered,"no old hover event before repaint");Equal(0,clicked,"no old click event before repaint");
                using(var bitmap=new Bitmap(chart.Width,chart.Height))chart.DrawToBitmap(bitmap,chart.ClientRectangle);
                var currentHits=Field<List<RectangleF>>(chart,"hits");Equal(7,currentHits.Count,"new week has seven painted regions");Point current=Center(currentHits[2]);MouseEvent(chart,"OnMouseMove",current);MouseEvent(chart,"OnMouseClick",current);Equal(1,hovered,"new painted hover works");Equal(1,clicked,"new painted click works");Equal(new DateTime(2024,7,3),selected,"new click uses week date");
            }
        });
        Case("export four periods empty state and update toast previews",delegate{
            string dir=DirectoryForCase();var store=DashboardData(dir);
            using(var host=new Form{ClientSize=new Size(780,702),ShowInTaskbar=false,Text="走动打卡 · 演示数据",BackColor=Theme.Paper})
            using(var dashboard=new ActivityDashboard(store.Snapshot,delegate{return null;}){Location=new Point(10,35)})
            {
                host.Controls.Add(new Label{Text="演示数据 · 仅用于界面验证，不会写入用户记录",Location=new Point(13,7),Size=new Size(750,24),Font=Theme.Font(9),ForeColor=Theme.Muted});host.Controls.Add(dashboard);host.Show();Application.DoEvents();
                string[] names={"日","周","月","年"};
                for(int i=0;i<4;i++){dashboard.SelectPeriod((ActivityPeriod)i,new DateTime(2024,2,29));Application.DoEvents();SaveImage(host,Path.Combine(previews,"走动打卡_"+names[i]+"_演示数据.png"));}
                dashboard.SelectPeriod(ActivityPeriod.Month,new DateTime(2023,5,1));Application.DoEvents();SaveImage(host,Path.Combine(previews,"走动打卡_空状态_演示数据.png"));host.Close();
            }
            Settings settings=Config();settings.Message="演示界面 · 不会写入实际打卡记录。\r\n放下手边的工作，站起来伸个懒腰。";
            int updateClicks=0,decisions=0;
            using(var toast=Toast(settings,false,delegate{decisions++;},delegate{return true;},"新版本 1.6.0 可更新",delegate{updateClicks++;}))
            {Show(toast);SaveImage(toast,Path.Combine(previews,"起立提醒_更新提示_演示界面.png"));Button(toast,"新版本").PerformClick();Application.DoEvents();Equal(1,updateClicks,"update link invokes explicit action");Equal(0,decisions,"update link is not a standing decision");True(toast.Visible,"update link keeps reminder available");toast.ReplaceClose();}
            Equal(6,Directory.GetFiles(previews,"*.png").Length,"six preview files");
        });
    }
    static ActivityStore DashboardData(string directory)
    {
        var store=new ActivityStore(directory);
        Add(store,"2024-01-01T08:00:00+08:00","done");Add(store,"2024-02-28T08:00:00+08:00","timeout");Add(store,"2024-02-29T09:00:00+08:00","done");Add(store,"2024-02-29T10:00:00+08:00","work");Add(store,"2024-03-01T11:00:00+08:00","pending");Add(store,"2025-01-01T08:00:00+08:00","done");return store;
    }
    static void Add(ActivityStore store,string shown,string outcome)
    {DateTimeOffset now=At(shown);string id=store.Begin(now);if(outcome!="pending")store.Complete(id,outcome,now.AddMinutes(1));}
    static void ClickBucket(ActivityDashboard dashboard,int index)
    {
        var chart=Field<ActivityChart>(dashboard,"chart");using(var bitmap=new Bitmap(chart.Width,chart.Height))chart.DrawToBitmap(bitmap,chart.ClientRectangle);
        var hits=Field<List<RectangleF>>(chart,"hits");var hit=hits[index];var point=new Point((int)(hit.Left+hit.Width/2),(int)(hit.Top+hit.Height/2));typeof(ActivityChart).GetMethod("OnMouseClick",PrivateInstance).Invoke(chart,new object[]{new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});Application.DoEvents();
    }
    static Point Center(RectangleF rectangle){return new Point((int)(rectangle.Left+rectangle.Width/2),(int)(rectangle.Top+rectangle.Height/2));}
    static void MouseEvent(ActivityChart chart,string name,Point point)
    {typeof(ActivityChart).GetMethod(name,PrivateInstance).Invoke(chart,new object[]{new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});}
    static void Editors(Control control,Control rootControl,Graphics graphics)
    {
        if(!control.Visible)return;var editor=control as CrispTextBox;
        if(editor!=null){using(var snapshot=editor.RenderTextSnapshot()){Point point=editor.PointToScreen(Point.Empty),origin=rootControl.PointToScreen(Point.Empty);graphics.DrawImageUnscaled(snapshot,point.X-origin.X,point.Y-origin.Y);}return;}
        foreach(Control child in control.Controls)Editors(child,rootControl,graphics);
    }
    static void SaveImage(Form form,string path)
    {
        using(var bitmap=new Bitmap(form.Width,form.Height))
        {
            form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
            Point offset=form.PointToScreen(Point.Empty);var crop=new Rectangle(offset.X-form.Left,offset.Y-form.Top,form.ClientSize.Width,form.ClientSize.Height);
            using(var client=bitmap.Clone(crop,bitmap.PixelFormat)){using(var graphics=Graphics.FromImage(client))Editors(form,form,graphics);client.Save(path);}
        }
    }
    [STAThread]public static int Main(string[] args)
    {
        try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>1)filters=args[1].Split(new[]{','},StringSplitOptions.RemoveEmptyEntries);
        previews=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"activity-previews");Directory.CreateDirectory(previews);
        root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"activity-integration-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{Run();}finally{Directory.Delete(root,true);}
        Console.WriteLine("Activity integration tests: "+passed+" passed, "+failed+" failed; "+assertions+" assertions.");return failed==0?0:1;
    }
}
