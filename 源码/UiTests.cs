using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using QingMove;
class UiTests
{
    static int count;
    static void Check(bool ok,string name) {if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
    [STAThread]static void Main()
    {
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var s=new Settings();s.DurationSeconds=0;string result=null;
            using(var toast=new ToastForm(s,true,delegate(string a){result=a;}))
            {
                toast.Show();Application.DoEvents();
                Check(toast.Visible,"toast visible");Check(!toast.ShowInTaskbar&&toast.TopMost,"toast topmost without taskbar button");
                Check(Screen.PrimaryScreen.WorkingArea.Contains(toast.Bounds),"toast fully inside working area");
                Check(Screen.PrimaryScreen.WorkingArea.Right-toast.Right==(int)Math.Round(16*toast.DeviceDpi/96f)&&Screen.PrimaryScreen.WorkingArea.Bottom-toast.Bottom==(int)Math.Round(16*toast.DeviceDpi/96f),"bottom right with taskbar clearance");
                toast.Controls.OfType<Button>().First(x=>x.Text.StartsWith("我已起身")).PerformClick();Application.DoEvents();
                Check(result=="done"&&!toast.Visible,"completion callback and dismissal");
            }
            foreach(var position in new[]{"top-left","top-right","bottom-left","bottom-right","center"})
            {
                var placed=Json.Read<Settings>("{\"DurationSeconds\":0,\"Position\":\""+position+"\"}");
                using(var toast=new ToastForm(placed,true,delegate{}))
                {
                    toast.Show();Application.DoEvents();var area=Screen.PrimaryScreen.WorkingArea;
                    Check(area.Contains(toast.Bounds),position+" fits working area");
                    if(position=="center")Check(Math.Abs(toast.Left*2+toast.Width-area.Left-area.Right)<=1 && Math.Abs(toast.Top*2+toast.Height-area.Top-area.Bottom)<=1,"centered in working area");
                    else
                    {
                        Check(position.EndsWith("left")?toast.Left-area.Left==(int)Math.Round(16*toast.DeviceDpi/96f):area.Right-toast.Right==(int)Math.Round(16*toast.DeviceDpi/96f),position+" horizontal placement");
                        Check(position.StartsWith("top")?toast.Top-area.Top==(int)Math.Round(16*toast.DeviceDpi/96f):area.Bottom-toast.Bottom==(int)Math.Round(16*toast.DeviceDpi/96f),position+" vertical placement");
                    }
                    toast.ReplaceClose();
                }
            }
            result=null;s.FontSize=18;s.Title=new string('走',60);s.Message=new string('动',400);
            using(var toast=new ToastForm(s,true,delegate(string a){result=a;}))
            {
                toast.Show();Application.DoEvents();
                Check(Screen.PrimaryScreen.WorkingArea.Contains(toast.Bounds),"maximum content fits working area");
                foreach(var button in toast.Controls.OfType<Button>())Check(toast.ClientRectangle.Contains(button.Bounds),"action button inside toast");
                toast.Controls.OfType<Button>().First(x=>x.Text.EndsWith("分钟后提醒")).PerformClick();Application.DoEvents();Check(result=="snooze","snooze callback");
            }
            Console.WriteLine("ALL "+count+" UI TESTS PASSED");
        }
        catch(Exception ex){Console.WriteLine("FAIL: "+ex.Message);Environment.ExitCode=1;}
    }
}
