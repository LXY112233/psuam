using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows.Forms;
using QingMove;
class VisualTests
{
    [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    static int count;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);count++;}
    static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
    static void Editors(Control c,Control root,Graphics g)
    {
        if(!c.Visible)return;var editor=c as CrispTextBox;
        if(editor!=null){using(var snapshot=editor.RenderTextSnapshot()){var point=editor.PointToScreen(Point.Empty);g.DrawImageUnscaled(snapshot,point.X-root.Left,point.Y-root.Top);}return;}
        foreach(Control child in c.Controls)Editors(child,root,g);
    }
    static void SaveImage(Control c,string path){using(var bitmap=new Bitmap(c.Width,c.Height)){c.DrawToBitmap(bitmap,new Rectangle(Point.Empty,c.Size));using(var g=Graphics.FromImage(bitmap))Editors(c,c,g);var form=c as Form;if(form==null){bitmap.Save(path);return;}var offset=form.PointToScreen(Point.Empty);var crop=new Rectangle(offset.X-form.Left,offset.Y-form.Top,form.ClientSize.Width,form.ClientSize.Height);using(var client=bitmap.Clone(crop,bitmap.PixelFormat))client.Save(path);}}
    static void Bounds(Control parent)
    {
        foreach(Control c in parent.Controls)
        {
            if(!c.Visible)continue;
            if(c is Panel || c is Button || c is FieldTextBox || c is StepperBox || c is SwitchCheck)
                Check(c.Left>=-1&&c.Top>=-1&&c.Right<=parent.ClientSize.Width+1&&c.Bottom<=parent.ClientSize.Height+1,"control fits: "+c.GetType().Name+" "+c.Text);
            Bounds(c);
        }
    }
    [STAThread]static void Main(string[] args)
    {
        try
        {
            try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string output=args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"QingMove-previews");Directory.CreateDirectory(output);
            var s=new Settings(){HourMinutes="0,30",UseWindow=true,WindowStart="08:00",WindowEnd="23:30"};
            var app=(ReminderApp)FormatterServices.GetUninitializedObject(typeof(ReminderApp));
            Action<string,object> set=delegate(string name,object value){typeof(ReminderApp).GetField("<"+name+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,value);};
            set("Config",s);set("Engine",new ReminderEngine(s,DateTime.Now));set("Events",new List<LogEntry>());
            using(var form=new SettingsForm(app))
            {
                form.Show();Application.DoEvents();form.UpdateStatus();
                var dashboard=Field<ActivityDashboard>(form,"dashboard");
                var chart=Field<ActivityChart>(dashboard,"chart");
                Check(Math.Abs(chart.Width-712*form.DeviceDpi/96.0)<=2,"dashboard chart scales with main window DPI");
                SaveImage(form,Path.Combine(output,"走动打卡_主窗口_空状态.png"));Bounds(form);
                Field<Button[]>(form,"navigation")[4].PerformClick();Application.DoEvents();SaveImage(form,Path.Combine(output,"软件更新设置.png"));Bounds(form);
                Field<Button[]>(form,"navigation")[0].PerformClick();Application.DoEvents();
                var read=typeof(SettingsForm).GetMethod("Read",BindingFlags.NonPublic|BindingFlags.Instance);
                Check(Json.Write((Settings)read.Invoke(form,null))==Json.Write(s),"settings survive redesigned controls without changes");
                SaveImage(form,Path.Combine(output,"设置界面.png"));Bounds(form);
                var nav=Field<Button[]>(form,"navigation");
                Check(nav[0].Cursor==Cursors.Default&&nav[1].Cursor==Cursors.Default,"navigation uses native arrow instead of scaled hand cursor");
                var enter=typeof(NavigationButton).GetMethod("OnMouseEnter",BindingFlags.NonPublic|BindingFlags.Instance);
                var leave=typeof(NavigationButton).GetMethod("OnMouseLeave",BindingFlags.NonPublic|BindingFlags.Instance);
                using(var before=new Bitmap(nav[0].Width,nav[0].Height))using(var after=new Bitmap(nav[0].Width,nav[0].Height))
                {
                    nav[0].DrawToBitmap(before,new Rectangle(Point.Empty,nav[0].Size));enter.Invoke(nav[0],new object[]{EventArgs.Empty});
                    nav[0].DrawToBitmap(after,new Rectangle(Point.Empty,nav[0].Size));
                    Check(before.GetPixel(10,nav[0].Height/2)==after.GetPixel(10,nav[0].Height/2),"selected navigation keeps its blue color on hover");
                    leave.Invoke(nav[0],new object[]{EventArgs.Empty});
                }
                enter.Invoke(nav[1],new object[]{EventArgs.Empty});SaveImage(form,Path.Combine(output,"侧栏悬停效果.png"));leave.Invoke(nav[1],new object[]{EventArgs.Empty});
                nav[1].PerformClick();Application.DoEvents();
                var pos=Field<ChoiceBox>(form,"position");pos.SelectedIndex=4;
                Check(((Settings)read.Invoke(form,null)).Position=="center","position selection flows into preview and save settings");
                Field<StepperBox>(form,"duration").Value=0;
                Check(((Settings)read.Invoke(form,null)).DurationSeconds==0,"zero duration retained by new stepper");
                SaveImage(form,Path.Combine(output,"弹窗设置.png"));Bounds(form);
                nav[2].PerformClick();Application.DoEvents();SaveImage(form,Path.Combine(output,"启动与记录.png"));Bounds(form);
                nav[0].PerformClick();Application.DoEvents();Check(Field<ChoiceBox>(form,"position").SelectedIndex==4,"navigation preserves unsaved position");
                Field<ChoiceBox>(form,"mode").SelectedIndex=1;Field<StepperBox>(form,"interval").Value=45;
                Check(((Settings)read.Invoke(form,null)).IntervalMinutes==45,"interval mode accepts edited value");
                Field<ChoiceBox>(form,"mode").SelectedIndex=2;Field<FieldTextBox>(form,"daily").Text="09:00,18:00";
                Check(((Settings)read.Invoke(form,null)).DailyTimes=="09:00,18:00","daily mode accepts edited times");
                form.AllowClose=true;form.Close();
            }
            s.Position="center";s.DurationSeconds=0;
            using(var toast=new ToastForm(s,true,delegate{}))
            {toast.Show();Application.DoEvents();SaveImage(toast,Path.Combine(output,"提醒弹窗.png"));Bounds(toast);toast.ReplaceClose();}
            using(var time=new TimeBox()){time.Value=DateTime.Today.AddHours(8).AddMinutes(30);Check(time.Value.Hour==8&&time.Value.Minute==30,"time input round trip");time.Text="25:99";bool invalid=false;try{var v=time.Value;}catch(ArgumentException){invalid=true;}Check(invalid,"invalid time rejected");}
            using(var step=new StepperBox(){Minimum=1,Maximum=120})
            {
                step.Value=5;var editor=(CrispTextBox)step.Controls[0];editor.Text="45";Check(step.Value==45,"typed stepper value accepted");
                foreach(var bad in new[]{"0","121","2.5","abc"}){editor.Text=bad;bool invalid=false;try{var v=step.Value;}catch(ArgumentException){invalid=true;}Check(invalid,"invalid numeric input rejected: "+bad);}
                step.Value=120;foreach(Control c in step.Controls)if(c is Button && c.Text=="+")((Button)c).PerformClick();Check(step.Value==120,"stepper stops at maximum");
            }
            Console.WriteLine("ALL "+count+" VISUAL AND CONTROL CHECKS PASSED");
        }
        catch(Exception ex){Console.WriteLine("FAIL "+ex);Environment.ExitCode=1;}
    }
}
