using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Reflection;
[assembly:AssemblyTitle("走动提醒")]
[assembly:AssemblyDescription("可自定义的 Windows 后台走动提醒工具")]
[assembly:AssemblyVersion("1.5.0.0")]
namespace QingMove
{
    static class Program
    {
        [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        public static readonly string UserId=WindowsIdentity.GetCurrent().User.Value;
        public static readonly string PipeName="QingMove.v1."+UserId;
        [STAThread]static void Main(string[] args)
        {
            if(args.Length==2&&args[0]=="--apply-update"){Environment.ExitCode=UpdateInstaller.Run(args[1]);return;}
            try {SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string command=args.Length>0?args[0].ToLowerInvariant():"";
            if(args.Length>1 || !Valid(command)) {MessageBox.Show("支持参数：\r\n--show 显示设置\r\n--test 测试通知\r\n--background 后台启动\r\n--pause=30 暂停30分钟（1–1440）\r\n--resume 恢复\r\n--exit 完全退出","走动提醒 · 使用帮助");return;}
            bool first;
            using(var mutex=new Mutex(true,"Local\\QingMove.v1."+UserId,out first))
            {
                if(!first){Send(command==""?"--show":command);return;}
                try
                {
                    if(command=="--exit")return;
                    using(var app=new ReminderApp(command))
                    {
                        Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e){app.ReportError(e.Exception);};
                        Application.Run(app);
                    }
                }
                catch(Exception ex) {MessageBox.Show("程序未能启动："+ex.Message,"走动提醒",MessageBoxButtons.OK,MessageBoxIcon.Error);}
                finally {mutex.ReleaseMutex();}
            }
        }
        static bool Valid(string s)
        {
            if(s==""||s=="--show"||s=="--test"||s=="--background"||s=="--resume"||s=="--exit")return true;
            int n;return s.StartsWith("--pause=")&&int.TryParse(s.Substring(8),out n)&&n>=1&&n<=1440;
        }
        static void Send(string command)
        {
            try {using(var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.Out)){pipe.Connect(5000);using(var w=new StreamWriter(pipe)){w.WriteLine(command);w.Flush();}}}
            catch(Exception ex){MessageBox.Show("已有程序正在运行，但暂时无法连接。请稍后重试或从托盘打开。\r\n"+ex.Message,"走动提醒");}
        }
    }
}
