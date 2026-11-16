using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace QingMove
{
    // Only scheduling and local notification metadata live here. HTTP and manual checks are independent.
    public sealed class UpdateSchedule
    {
        internal sealed class StateData
        {
            // Keep the schema scalar textual: the Framework serializer wraps numeric overflow
            // in a plain Exception when converting an untrusted oversized JSON number to int.
            public string Schema;
            public string AttemptDate;
            public List<string> AttemptedRepositories;
            public string Repository,Version,Tag,Notes,Url;
            public StateData(){}
        }
        readonly string stateFile;
        readonly object gate=new object();
        StateData state=EmptyState();
        bool preserveInvalid;
        static StateData EmptyState(){return new StateData{Schema="1",AttemptedRepositories=new List<string>()};}
        public UpdateSchedule(string stateFile)
        {
            if(string.IsNullOrWhiteSpace(stateFile))throw new ArgumentException("更新检查状态文件不能为空。","stateFile");
            this.stateFile=Path.GetFullPath(stateFile);Load();
        }
        public string CachedRepository{get{lock(gate)return state.Repository;}}
        public string CachedVersion{get{lock(gate)return state.Version;}}
        public string CachedTag{get{lock(gate)return state.Tag;}}
        public string CachedNotes{get{lock(gate)return state.Notes;}}
        public string CachedUrl{get{lock(gate)return state.Url;}}
        public string Warning{get;private set;}
        void Warn(string message)
        {if(string.IsNullOrEmpty(Warning))Warning=message;else if(Warning.IndexOf(message,StringComparison.Ordinal)<0 && Warning.Length<1500)Warning+="\r\n"+message;}
        static string Repository(string repository)
        {
            string repo=(repository??"").Trim();
            if(repo.Length==0)return null;
            if(repo.Length>140 || !Regex.IsMatch(repo,@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9_.-]{1,100}$") || repo.EndsWith("/.",StringComparison.Ordinal) || repo.EndsWith("/..",StringComparison.Ordinal))throw new ArgumentException("更新仓库格式应为 owner/repo。","repository");
            return repo.ToLowerInvariant();
        }
        static bool ValidCache(string repository,string version,string tag,string notes,string url)
        {
            Version parsed;Uri uri;
            if(repository==null || string.IsNullOrEmpty(version) || version.Length>64 || !Version.TryParse(version,out parsed) || string.IsNullOrEmpty(tag) || tag.Length>256 || tag.Any(char.IsControl) || notes==null || notes.Length>50000 || string.IsNullOrEmpty(url) || url.Length>2048)return false;
            if(!Uri.TryCreate(url,UriKind.Absolute,out uri) || uri.Scheme!="https" || !string.Equals(uri.Host,"github.com",StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || uri.UserInfo!="" || uri.Query!="" || uri.Fragment!="")return false;
            string path=Uri.UnescapeDataString(uri.AbsolutePath),prefix="/"+repository+"/releases/tag/";
            return path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) && string.Equals(path.Substring(prefix.Length),tag,StringComparison.Ordinal);
        }
        static void ResetCache(StateData data){data.Repository=data.Version=data.Tag=data.Notes=data.Url=null;}
        void Load()
        {
            try
            {
                if(!File.Exists(stateFile))return;
                if(new FileInfo(stateFile).Length>1024*1024)throw new InvalidDataException("状态文件超过 1 MB。");
                StateData loaded;
                try{loaded=Json.Read<StateData>(File.ReadAllText(stateFile,Encoding.UTF8));}
                catch(ArgumentException ex){throw new InvalidDataException("状态内容无法读取。",ex);}
                catch(InvalidOperationException ex){throw new InvalidDataException("状态内容无法读取。",ex);}
                DateTime date;
                if(loaded==null || loaded.Schema!="1" || loaded.AttemptedRepositories==null || (loaded.AttemptDate!=null && !DateTime.TryParseExact(loaded.AttemptDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)) || (loaded.AttemptDate==null && loaded.AttemptedRepositories.Count!=0))throw new InvalidDataException("状态内容不完整。");
                try
                {
                    if(loaded.AttemptedRepositories.Any(x=>string.IsNullOrWhiteSpace(x)))throw new ArgumentException("无效仓库。");
                    loaded.AttemptedRepositories=loaded.AttemptedRepositories.Select(Repository).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    bool hasCache=loaded.Repository!=null || loaded.Version!=null || loaded.Tag!=null || loaded.Notes!=null || loaded.Url!=null;
                    if(hasCache)
                    {
                        loaded.Repository=Repository(loaded.Repository);
                        if(!ValidCache(loaded.Repository,loaded.Version,loaded.Tag,loaded.Notes,loaded.Url))
                        {ResetCache(loaded);preserveInvalid=true;Warn("已忽略无效的更新通知缓存；每日检查记录仍然保留。");}
                    }
                }
                catch(ArgumentException ex){throw new InvalidDataException("状态包含无效仓库。",ex);}
                state=loaded;
            }
            catch(InvalidDataException ex){preserveInvalid=true;state=EmptyState();Warn("更新检查状态损坏，将使用新状态；原文件会保留副本："+ex.Message);}
            catch(IOException ex){Warn("更新检查状态暂时无法读取："+ex.Message);}
            catch(UnauthorizedAccessException ex){Warn("更新检查状态暂时无法读取："+ex.Message);}
        }
        void Save()
        {
            string temp=stateFile+".tmp-"+Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stateFile));
                byte[] bytes=new UTF8Encoding(false).GetBytes(Json.Write(state));
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(File.Exists(stateFile))
                {
                    if(preserveInvalid)
                    {
                        string invalid=stateFile+"."+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff",CultureInfo.InvariantCulture)+"."+Guid.NewGuid().ToString("N")+".invalidcopy";
                        File.Copy(stateFile,invalid,false);preserveInvalid=false;
                    }
                    File.Replace(temp,stateFile,stateFile+".bak");
                }
                else File.Move(temp,stateFile);
            }
            catch(IOException ex){Warn("更新检查状态未能保存；本次运行仍会避免重复检查："+ex.Message);}
            catch(UnauthorizedAccessException ex){Warn("更新检查状态未能保存；本次运行仍会避免重复检查："+ex.Message);}
            finally
            {try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        // Reserve before HTTP, including after a disk failure, to avoid a retry on every timer tick.
        public bool TryBegin(DateTime localNow,string repository)
        {
            string repo=Repository(repository);
            if(repo==null || localNow.TimeOfDay<TimeSpan.FromHours(9))return false;
            string day=localNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            lock(gate)
            {
                if(state.AttemptDate!=day){state.AttemptDate=day;state.AttemptedRepositories.Clear();}
                if(state.AttemptedRepositories.Contains(repo,StringComparer.OrdinalIgnoreCase))return false;
                state.AttemptedRepositories.Add(repo);Save();return true;
            }
        }
        public void RememberAvailable(string repository,string version,string tag,string notes,string url)
        {
            string repo=Repository(repository),boundedNotes=notes??"";
            if(boundedNotes.Length>50000)boundedNotes=boundedNotes.Substring(0,50000);
            if(!ValidCache(repo,version,tag,boundedNotes,url))throw new ArgumentException("更新通知信息或 GitHub 发布地址无效。");
            lock(gate)
            {state.Repository=repo;state.Version=version;state.Tag=tag;state.Notes=boundedNotes;state.Url=url;Save();}
        }
        // A stale response for another repository must not erase the current repository's badge.
        public void ClearAvailable(string repository)
        {
            string repo=Repository(repository);if(repo==null)return;
            lock(gate)
            {if(!string.Equals(repo,state.Repository,StringComparison.OrdinalIgnoreCase))return;ResetCache(state);Save();}
        }
    }
}
