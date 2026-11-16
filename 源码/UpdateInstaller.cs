using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
namespace QingMove
{
    public static class UpdateInstaller
    {
        public static string UpdatesRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QingMoveReminder","updates");}}
        sealed class InstallPlan
        {
            public string StagedDir,TargetExe,ExeHash,ConfigHash,OriginalExeHash,OriginalConfigHash,Version,HelperPath,HelperHash;
            public int ParentPid;
            public long ParentStartTicks;
        }
        public static void Launch(string stagedDir,string targetExe)
        {
            targetExe=Path.GetFullPath(targetExe);stagedDir=Path.GetFullPath(stagedDir);
            string entry=Path.GetFullPath(Assembly.GetEntryAssembly().Location);
            if(!string.Equals(entry,targetExe,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(targetExe)!=UpdateService.ExeName)throw new InvalidOperationException("只能更新当前正在运行的走动提醒程序。");
            RequireStage(stagedDir);RequirePlainPath(targetExe);RequirePlainPath(targetExe+".config");
            string exe=Path.Combine(stagedDir,UpdateService.ExeName),config=Path.Combine(stagedDir,UpdateService.ConfigName);
            Version version=AssemblyName.GetAssemblyName(exe).Version;UpdateService.ValidateExecutable(exe,version);
            string job=Path.Combine(UpdatesRoot,"helper-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);RequirePlainPath(job);
            string helper=Path.Combine(job,"PSUAM-update-helper.exe"),planPath=Path.Combine(job,"plan.json");
            File.Copy(targetExe,helper,false);File.Copy(targetExe+".config",helper+".config",false);
            InstallPlan plan;
            using(var parent=Process.GetCurrentProcess())plan=new InstallPlan{StagedDir=stagedDir,TargetExe=targetExe,ExeHash=UpdateService.HashFile(exe),ConfigHash=UpdateService.HashFile(config),OriginalExeHash=UpdateService.HashFile(targetExe),OriginalConfigHash=UpdateService.HashFile(targetExe+".config"),Version=version.ToString(),ParentPid=parent.Id,ParentStartTicks=parent.StartTime.ToUniversalTime().Ticks,HelperPath=helper,HelperHash=UpdateService.HashFile(helper)};
            File.WriteAllText(planPath,new JavaScriptSerializer().Serialize(plan),new UTF8Encoding(false));
            var start=new ProcessStartInfo{FileName=helper,Arguments="--apply-update "+QuoteArgument(planPath),UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=job};
            using(var process=Process.Start(start)){if(process==null)throw new InvalidOperationException("无法启动更新助手，程序尚未退出。");}
        }
        // Called before the application's single-instance mutex is created.
        public static int Run(string planPath)
        {
            bool trustedPlan=false;
            try
            {
                planPath=Path.GetFullPath(planPath);string job=Path.GetDirectoryName(planPath);
                if(!IsWithin(job,UpdatesRoot)||Path.GetFileName(planPath)!="plan.json"||!Regex.IsMatch(Path.GetFileName(job),@"^helper-[a-f0-9]{32}$")||!string.Equals(Path.GetDirectoryName(job),Path.GetFullPath(UpdatesRoot),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新计划不在受信任的更新目录中。");
                RequirePlainPath(planPath);if(new FileInfo(planPath).Length>16384)throw new InvalidDataException("更新计划过大。");trustedPlan=true;
                var plan=new JavaScriptSerializer{MaxJsonLength=16384}.Deserialize<InstallPlan>(File.ReadAllText(planPath,Encoding.UTF8));
                if(plan==null)throw new InvalidDataException("更新计划无效。");
                string running=Path.GetFullPath(Assembly.GetEntryAssembly().Location),helper=Path.Combine(job,"PSUAM-update-helper.exe");
                if(!string.Equals(running,helper,StringComparison.OrdinalIgnoreCase)||!string.Equals(plan.HelperPath,helper,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新助手身份与计划不一致。");
                UpdateService.RequireHash(helper,plan.HelperHash);if(!string.Equals(plan.HelperHash,plan.OriginalExeHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新助手来源校验失败。");
                RequireStage(plan.StagedDir);
                string candidate=Path.GetFullPath(plan.TargetExe);
                if(Path.GetFileName(candidate)!=UpdateService.ExeName||IsWithin(candidate,UpdatesRoot)||string.Equals(Path.GetDirectoryName(candidate),plan.StagedDir,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新目标路径无效。");
                RequirePlainPath(candidate);RequirePlainPath(candidate+".config");
                Version version;if(!System.Version.TryParse(plan.Version,out version))throw new InvalidDataException("计划中的更新版本无效。");
                string stagedExe=Path.Combine(plan.StagedDir,UpdateService.ExeName);
                UpdateService.RequireHash(stagedExe,plan.ExeHash);UpdateService.RequireHash(Path.Combine(plan.StagedDir,UpdateService.ConfigName),plan.ConfigHash);UpdateService.ValidateExecutable(stagedExe,version);
                // Verify installed files before waiting, and again immediately before writing.
                UpdateService.RequireHash(candidate,plan.OriginalExeHash);UpdateService.RequireHash(candidate+".config",plan.OriginalConfigHash);
                WaitForParent(plan.ParentPid,plan.ParentStartTicks);
                UpdateService.RequireHash(candidate,plan.OriginalExeHash);UpdateService.RequireHash(candidate+".config",plan.OriginalConfigHash);
                return ApplyAndRestart(plan.StagedDir,candidate,plan.ExeHash,plan.ConfigHash,plan.OriginalExeHash,plan.OriginalConfigHash,plan.Version,null,Restart,Log);
            }
            catch(Exception ex)
            {
                if(trustedPlan)Log("更新未完成："+ex.Message+"\r\n"+ex);
                return 1;
            }
        }
        // Run and offline fixtures share this exact replacement/restart path.
        internal static int ApplyAndRestart(string staged,string target,string exeHash,string configHash,string originalExeHash,string originalConfigHash,string version,Action beforeConfig,Action<string> restart,Action<string> log)
        {
            try
            {
                ReplacePair(staged,target,exeHash,configHash,beforeConfig);
            }
            catch(Exception ex)
            {
                log("更新未完成："+ex.Message+"\r\n"+ex);
                // A rollback error may leave an old/new mixture. Only the exact
                // original pair is allowed to restart from this failure branch.
                RestartVerifiedPair(target,originalExeHash,originalConfigHash,restart,log);
                return 1;
            }
            log("更新成功："+version+"。旧文件已保留在安装目录中的 .psuam-backup-日期 文件夹。");
            return RestartVerifiedPair(target,exeHash,configHash,restart,log)?0:1;
        }
        static bool RestartVerifiedPair(string target,string exeHash,string configHash,Action<string> restart,Action<string> log)
        {
            try
            {
                RequirePlainPath(target);RequirePlainPath(target+".config");
                UpdateService.RequireHash(target,exeHash);UpdateService.RequireHash(target+".config",configHash);
            }
            catch(Exception ex)
            {
                log("未重新启动程序：无法验证完整的程序和配置。请保留备份，并从日志中的备份目录恢复。原因："+ex.Message);return false;
            }
            try{restart(target);return true;}
            catch(Exception ex){log("文件已验证，但无法重新打开程序，请手动启动："+target+"\r\n"+ex.Message);return false;}
        }
        static void WaitForParent(int pid,long ticks)
        {
            if(pid<=0||pid==Process.GetCurrentProcess().Id||ticks<=0)throw new InvalidDataException("更新计划中的原程序身份无效。");
            Process parent;
            try{parent=Process.GetProcessById(pid);}catch(ArgumentException){return;}
            using(parent)
            {
                try{if(parent.StartTime.ToUniversalTime().Ticks!=ticks)return;}catch(InvalidOperationException){return;}
                if(!parent.WaitForExit(60000))throw new InvalidOperationException("原程序在 60 秒内未退出。此次更新已取消，请关闭程序后重试。");
            }
        }
        static void Restart(string target)
        {using(var process=Process.Start(new ProcessStartInfo{FileName=target,Arguments="--show",WorkingDirectory=Path.GetDirectoryName(target),UseShellExecute=false})){if(process==null)throw new InvalidOperationException("更新后无法启动程序。");}}
        internal static string QuoteArgument(string value)
        {
            if(value==null||value.IndexOf('\0')>=0||value.IndexOf('\r')>=0||value.IndexOf('\n')>=0)throw new ArgumentException("参数无效。");
            var result=new StringBuilder("\"");int slashes=0;
            foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1);result.Append(c);slashes=0;continue;}result.Append('\\',slashes);slashes=0;result.Append(c);}result.Append('\\',slashes*2);return result.Append('"').ToString();
        }
        internal static bool IsWithin(string path,string root)
        {string canonical=Path.GetFullPath(path),basePath=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;return canonical.StartsWith(basePath,StringComparison.OrdinalIgnoreCase);}
        internal static void RequirePlainPath(string path)
        {
            string current=Path.GetFullPath(path);
            while(!string.IsNullOrEmpty(current))
            {
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新路径不能经过符号链接或重解析点。");
                string parent=Path.GetDirectoryName(current);if(parent==current)break;current=parent;
            }
        }
        static void RequireStage(string staged)
        {
            if(string.IsNullOrEmpty(staged)||!IsWithin(staged,UpdatesRoot)||!Regex.IsMatch(Path.GetFileName(staged),@"^stage-[a-f0-9]{32}$")||!string.Equals(Path.GetDirectoryName(staged),Path.GetFullPath(UpdatesRoot),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新暂存目录无效。");
            RequirePlainPath(staged);string exe=Path.Combine(staged,UpdateService.ExeName),config=Path.Combine(staged,UpdateService.ConfigName);RequirePlainPath(exe);RequirePlainPath(config);
            if(Directory.GetFiles(staged).Length!=2||Directory.GetDirectories(staged).Length!=0||!File.Exists(exe)||!File.Exists(config))throw new InvalidDataException("暂存目录必须仅包含程序和对应的配置。");
        }
        internal static void ReplacePair(string staged,string targetExe,string exeHash,string configHash,Action beforeConfig)
        {
            string sourceExe=Path.Combine(staged,UpdateService.ExeName),sourceConfig=Path.Combine(staged,UpdateService.ConfigName),targetConfig=targetExe+".config",directory=Path.GetDirectoryName(Path.GetFullPath(targetExe));
            RequirePlainPath(staged);RequirePlainPath(sourceExe);RequirePlainPath(sourceConfig);RequirePlainPath(targetExe);RequirePlainPath(targetConfig);
            UpdateService.RequireHash(sourceExe,exeHash);UpdateService.RequireHash(sourceConfig,configHash);
            if(!File.Exists(targetExe)||!File.Exists(targetConfig))throw new InvalidDataException("安装目录缺少程序或配置，无法创建完整备份。");
            string backup=Path.Combine(directory,".psuam-backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(backup);
            string backupExe=Path.Combine(backup,UpdateService.ExeName),backupConfig=Path.Combine(backup,UpdateService.ConfigName);
            File.Copy(targetExe,backupExe,false);File.Copy(targetConfig,backupConfig,false);
            string oldExeHash=UpdateService.HashFile(backupExe),oldConfigHash=UpdateService.HashFile(backupConfig);
            string token=".psuam-new-"+Guid.NewGuid().ToString("N"),newExe=Path.Combine(directory,token+".exe"),newConfig=Path.Combine(directory,token+".config");
            bool exeChanged=false,configChanged=false;
            try
            {
                File.Copy(sourceExe,newExe,false);File.Copy(sourceConfig,newConfig,false);UpdateService.RequireHash(newExe,exeHash);UpdateService.RequireHash(newConfig,configHash);
                // File.Replace is atomic for each file. The second-file failure restores
                // the first file from its dated backup before the old app is reopened.
                File.Replace(newExe,targetExe,null);exeChanged=true;
                if(beforeConfig!=null)beforeConfig();
                File.Replace(newConfig,targetConfig,null);configChanged=true;
                UpdateService.RequireHash(targetExe,exeHash);UpdateService.RequireHash(targetConfig,configHash);
            }
            catch(Exception installError)
            {
                try
                {
                    if(exeChanged)Restore(backupExe,targetExe,oldExeHash);
                    if(configChanged)Restore(backupConfig,targetConfig,oldConfigHash);
                    UpdateService.RequireHash(targetExe,oldExeHash);UpdateService.RequireHash(targetConfig,oldConfigHash);
                }
                catch(Exception rollbackError){throw new IOException("更新失败，自动恢复也未完成。请从备份目录恢复程序："+backup+"。原因："+rollbackError.Message,installError);}
                throw new IOException("更新失败，已恢复原来的程序和配置。备份："+backup+"。原因："+installError.Message,installError);
            }
            finally{try{if(File.Exists(newExe))File.Delete(newExe);if(File.Exists(newConfig))File.Delete(newConfig);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        static void Restore(string backup,string target,string hash)
        {
            string temporary=target+".psuam-restore-"+Guid.NewGuid().ToString("N");
            try{File.Copy(backup,temporary,false);UpdateService.RequireHash(temporary,hash);File.Replace(temporary,target,null);UpdateService.RequireHash(target,hash);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        static void Log(string message)
        {try{Directory.CreateDirectory(UpdatesRoot);File.AppendAllText(Path.Combine(UpdatesRoot,"update.log"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+message+"\r\n",Encoding.UTF8);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
