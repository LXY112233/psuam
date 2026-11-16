using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace QingMove
{
    public sealed class UpdateForm : Form
    {
        readonly string repository;
        readonly Action onReadyToRestart;
        readonly Action<UpdateRelease> onChecked;
        readonly UpdateService service=new UpdateService();
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly Label status,available;
        readonly CrispTextBox notes;
        readonly ProgressBar progress;
        readonly Button check,upgrade;
        UpdateRelease release;
        bool busy,closed,released,shown;
        bool Alive {get{return !closed&&!IsDisposed&&!Disposing;}}
        public UpdateForm(string repository,Action onReadyToRestart,Action<UpdateRelease> onChecked=null)
        {
            SuspendLayout();
            this.repository=repository;this.onReadyToRestart=onReadyToRestart;this.onChecked=onChecked;
            AutoScaleMode=AutoScaleMode.Dpi;
            Text="软件更新 · 走动提醒";StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(610,510);
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;BackColor=Theme.Paper;Font=Theme.Font(10);Icon=Theme.AppIcon();
            var title=Theme.Label(this,"软件更新",28,20,550,38);title.Font=Theme.Bold(20);
            var current=Theme.Label(this,"当前版本  "+Assembly.GetExecutingAssembly().GetName().Version.ToString(3),29,64,550,26);current.ForeColor=Theme.Muted;
            var card=Theme.Card(this,28,106,554,274);card.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            available=Theme.Label(card,"从 GitHub Releases 获取正式版本",20,14,510,30);available.Font=Theme.Bold(12);
            var source=Theme.Label(card,"仓库  "+(repository??""),20,48,510,26);source.ForeColor=Theme.Muted;source.AutoEllipsis=true;
            notes=new CrispTextBox{Location=new Point(20,88),Size=new Size(514,165),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,ForeColor=Theme.Ink,Font=Theme.Font(10),Text="正在准备检查最新版本…",TabStop=true};card.Controls.Add(notes);
            status=Theme.Label(this,"更新由你决定，安装后将重新打开程序。",29,392,552,38);status.AutoEllipsis=true;
            progress=new ProgressBar{Location=new Point(29,435),Size=new Size(552,6),Minimum=0,Maximum=100,Visible=false,Style=ProgressBarStyle.Continuous};Controls.Add(progress);
            check=Theme.Button(this,"检查更新",28,458,132,34,false,async delegate{await CheckAsync();});
            upgrade=Theme.Button(this,"下载并升级",426,458,156,34,true,async delegate{await UpgradeAsync();});upgrade.Enabled=false;
            Theme.Button(this,"关闭",318,458,96,34,false,delegate{Close();});
            // All dimensions above describe the unscaled 96-DPI baseline.
            AutoScaleDimensions=new SizeF(96,96);
            ResumeLayout(true);
        }
        protected override async void OnShown(EventArgs e)
        {base.OnShown(e);if(shown||!Alive)return;shown=true;await CheckAsync();}
        async Task CheckAsync()
        {
            if(busy||!Alive)return;SetBusy(true);release=null;upgrade.Enabled=false;available.Text="正在检查最新版本…";status.Text="正在连接 GitHub…";progress.Visible=false;
            try
            {
                var result=await service.CheckAsync(repository,Assembly.GetExecutingAssembly().GetName().Version,lifetime.Token);
                if(!Alive)return;release=result;available.Text="最新正式版本  "+result.Version.ToString(3);
                notes.Text=string.IsNullOrWhiteSpace(result.Notes)?"此版本未提供更新说明。":result.Notes;
                status.Text=result.IsNewer?"发现新版本。下载完成并校验后，将关闭并重新打开程序。":"当前已是最新版本（或当前版本更新）。";
                if(onChecked!=null)onChecked(result);
            }
            catch(OperationCanceledException){if(Alive)status.Text="已取消检查更新。";}
            catch(Exception ex){if(Alive){available.Text="暂时无法检查更新";status.Text=ex.Message;notes.Text=ex.Message;}}
            finally{if(Alive)SetBusy(false);}
        }
        async Task UpgradeAsync()
        {
            if(busy||!Alive||release==null||!release.IsNewer)return;
            if(onReadyToRestart==null){status.Text="更新退出回调尚未配置，请重新打开设置后重试。";return;}
            if(MessageBox.Show(this,"下载并升级到 "+release.Version.ToString(3)+"？\r\n\r\n更新包通过校验后，走动提醒会关闭并重新打开。你的提醒设置和打卡记录会保留。","确认升级",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            SetBusy(true);progress.Value=0;progress.Visible=true;status.Text="正在下载并校验更新包…";
            try
            {
                var reporter=new Progress<int>(delegate(int value){if(Alive)progress.Value=Math.Max(0,Math.Min(100,value));});
                string stage=await service.DownloadAndStageAsync(release,UpdateInstaller.UpdatesRoot,reporter,lifetime.Token);
                if(!Alive)return;
                status.Text="校验完成，正在启动更新助手…";
                UpdateInstaller.Launch(stage,Assembly.GetEntryAssembly().Location);
                // Launch succeeded: the helper will wait for this process to exit.
                status.Text="正在退出并安装更新…";onReadyToRestart();
                if(Alive)Close();
            }
            catch(OperationCanceledException){if(Alive)status.Text="已取消下载，当前程序可继续使用。";}
            catch(Exception ex){if(Alive){status.Text=ex.Message;MessageBox.Show(this,ex.Message,"未能升级",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
            finally{if(Alive)SetBusy(false);}
        }
        void SetBusy(bool value){busy=value;check.Enabled=!value;upgrade.Enabled=!value&&release!=null&&release.IsNewer;}
        protected override void OnFormClosing(FormClosingEventArgs e)
        {base.OnFormClosing(e);if(!e.Cancel&&!released){closed=true;lifetime.Cancel();}}
        protected override void Dispose(bool disposing)
        {
            if(disposing&&!released){released=true;closed=true;lifetime.Cancel();service.Dispose();lifetime.Dispose();}
            base.Dispose(disposing);
        }
    }
}
