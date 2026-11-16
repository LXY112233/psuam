using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace QingMove
{
    public class Settings
    {
        public bool Enabled=true, UseWindow=false, AutoStart=false, StartHidden=false;
        public string Mode="hourly", HourMinutes="30", DailyTimes="09:00,14:30,18:00";
        public int IntervalMinutes=30;
        public int[] Days=new int[]{0,1,2,3,4,5,6};
        public string WindowStart="09:00", WindowEnd="18:00";
        public string Title="起来走动一下", Message="放下手边的工作，站起来伸个懒腰。\r\n走动 2–3 分钟，让身体和眼睛休息一下。";
        public int DurationSeconds=20, SnoozeMinutes=5, FontSize=11, OpacityPercent=100;
        public string Accent="green", Sound="none", Monitor="primary";
        public string Position="bottom-right";
        public string UpdateRepository="LXY112233/psuam";
        public void Validate()
        {
            if(Mode!="hourly" && Mode!="interval" && Mode!="daily") throw new ArgumentException("请选择有效的提醒模式。");
            Schedule.Minutes(HourMinutes); Schedule.Times(DailyTimes);
            if(IntervalMinutes<1 || IntervalMinutes>1440) throw new ArgumentException("间隔应为 1–1440 分钟。");
            if(Days==null || Days.Length==0 || Days.Length>7 || Days.Any(x=>x<0 || x>6)) throw new ArgumentException("请至少选择一个提醒星期。");
            TimeSpan a=Schedule.Clock(WindowStart), b=Schedule.Clock(WindowEnd);
            if(UseWindow && a==b) throw new ArgumentException("提醒时段的开始和结束不能相同；全天提醒请取消时段限制。");
            if(string.IsNullOrWhiteSpace(Title) || Title.Length>60) throw new ArgumentException("标题应为 1–60 个字符。");
            if(string.IsNullOrWhiteSpace(Message) || Message.Length>400) throw new ArgumentException("提醒正文应为 1–400 个字符。");
            if(DurationSeconds<0 || DurationSeconds>300) throw new ArgumentException("显示时长应为 0–300 秒。");
            if(SnoozeMinutes<1 || SnoozeMinutes>120) throw new ArgumentException("稍后提醒应为 1–120 分钟。");
            if(FontSize<9 || FontSize>18 || OpacityPercent<60 || OpacityPercent>100) throw new ArgumentException("字体或透明度超出允许范围。");
            if(!new[]{"green","blue","orange","purple"}.Contains(Accent)) throw new ArgumentException("主题颜色无效。");
            if(!new[]{"none","asterisk","beep"}.Contains(Sound)) throw new ArgumentException("提示音无效。");
            if(Monitor!="primary" && Monitor!="cursor") throw new ArgumentException("显示屏选项无效。");
            if(!new[]{"top-left","top-right","bottom-left","bottom-right","center"}.Contains(Position)) throw new ArgumentException("弹窗位置无效。");
            if(UpdateRepository==null || (UpdateRepository!=""&&!System.Text.RegularExpressions.Regex.IsMatch(UpdateRepository,@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$")))throw new ArgumentException("更新仓库格式应为 GitHub用户名/仓库名。");
        }
        public Settings Copy() { return Json.Read<Settings>(Json.Write(this)); }
    }
    public static class Json
    {
        public static string Write(object obj) { return new JavaScriptSerializer().Serialize(obj); }
        public static T Read<T>(string text) { return new JavaScriptSerializer(){MaxJsonLength=1024*1024}.Deserialize<T>(text); }
    }
    public static class Schedule
    {
        static string[] Parts(string value)
        {
            if(string.IsNullOrWhiteSpace(value) || value.Length>1000) throw new ArgumentException("提醒时间不能为空或过长。");
            return value.Replace('，',',').Replace('；',',').Replace(';',',').Split(',');
        }
        public static int[] Minutes(string text)
        {
            return Parts(text).Select(delegate(string x){int n;if(!int.TryParse(x.Trim(),out n)||n<0||n>59)throw new ArgumentException("每小时分钟请填写 0–59，用逗号分隔，如 0,30。");return n;}).Distinct().OrderBy(x=>x).ToArray();
        }
        public static TimeSpan Clock(string text)
        {
            DateTime t;
            if(text==null || !DateTime.TryParseExact(text.Trim(),new[]{"H:mm","HH:mm"},CultureInfo.InvariantCulture,DateTimeStyles.None,out t)) throw new ArgumentException("时刻应使用 24 小时制，如 09:00、18:30。");
            return t.TimeOfDay;
        }
        public static TimeSpan[] Times(string text) { return Parts(text).Select(Clock).Distinct().OrderBy(x=>x).ToArray(); }
        public static bool Allowed(Settings s,DateTime t)
        {
            if(!s.Enabled || !s.Days.Contains((int)t.DayOfWeek)) return false;
            if(!s.UseWindow) return true;
            var a=Clock(s.WindowStart);var b=Clock(s.WindowEnd);var c=t.TimeOfDay;
            return a<b ? c>=a && c<b : c>=a || c<b;
        }
        public static DateTime? Next(Settings s,DateTime after) { return Next(s,after,after); }
        public static DateTime? Next(Settings s,DateTime after,DateTime anchor)
        {
            if(!s.Enabled) return null;
            if(s.Mode=="interval")
            {
                double steps=Math.Max(1,Math.Floor((after-anchor).TotalMinutes/s.IntervalMinutes)+1);
                var t=anchor.AddMinutes(steps*s.IntervalMinutes);
                int a=s.IntervalMinutes,b=7*24*60;
                while(b!=0){int remainder=a%b;a=b;b=remainder;}
                int cycle=(7*24*60)/a;
                for(int i=0;i<cycle;i++,t=t.AddMinutes(s.IntervalMinutes)) if(Allowed(s,t)) return t;
                return null;
            }
            var times=s.Mode=="daily" ? Times(s.DailyTimes) : Enumerable.Range(0,24).SelectMany(h=>Minutes(s.HourMinutes).Select(m=>new TimeSpan(h,m,0))).ToArray();
            for(int d=0;d<15;d++) foreach(var time in times)
            { var t=after.Date.AddDays(d).Add(time); if(t>after && Allowed(s,t)) return t; }
            return null;
        }
    }
    public class ReminderEngine
    {
        readonly Settings settings;
        DateTime anchor,last;
        DateTime? regular,snoozed;
        public DateTime? PausedUntil { get; private set; }
        public DateTime? NextDue { get { if(snoozed.HasValue && (!regular.HasValue || snoozed<regular)) return snoozed;return regular; } }
        public ReminderEngine(Settings s,DateTime now) { settings=s;anchor=last=now;regular=Schedule.Next(s,now,anchor); }
        public ReminderEngine Reconfigure(Settings s,DateTime now)
        {
            var updated=new ReminderEngine(s,now);
            if(PausedUntil.HasValue&&PausedUntil.Value>now)updated.PausedUntil=PausedUntil;
            if(s.Enabled&&snoozed.HasValue&&snoozed.Value>now)updated.snoozed=snoozed;
            return updated;
        }
        public void Pause(DateTime now,int minutes) { PausedUntil=now.AddMinutes(minutes);snoozed=null; }
        public void Resume(DateTime now) { PausedUntil=null;snoozed=null;anchor=now;regular=Schedule.Next(settings,now,anchor);last=now; }
        public void Snooze(DateTime now,int minutes) { snoozed=now.AddMinutes(minutes); }
        public bool Tick(DateTime now)
        {
            if(now<last.AddMinutes(-2)) {anchor=now;regular=Schedule.Next(settings,now,anchor);snoozed=null;}
            last=now;
            bool paused=PausedUntil.HasValue && now<PausedUntil.Value;
            if(PausedUntil.HasValue && !paused) { PausedUntil=null;regular=Schedule.Next(settings,now,anchor);snoozed=null;return false; }
            bool fire=false;
            if(regular.HasValue && now>=regular.Value)
            {
                fire=!paused && (now-regular.Value).TotalSeconds<=90 && Schedule.Allowed(settings,now);
                regular=Schedule.Next(settings,now,anchor);
            }
            if(snoozed.HasValue && now>=snoozed.Value) { fire=fire || (!paused && settings.Enabled && (now-snoozed.Value).TotalSeconds<=90);snoozed=null; }
            return fire;
        }
    }
    public class SettingsStore
    {
        public string FilePath {get;private set;}
        public SettingsStore(string path) {FilePath=path;}
        public Settings Load()
        {
            if(!File.Exists(FilePath))return new Settings();
            if(new FileInfo(FilePath).Length>1024*1024)throw new ArgumentException("配置文件超过 1MB，请选择本工具导出的 JSON 文件。");
            var s=Json.Read<Settings>(File.ReadAllText(FilePath,Encoding.UTF8));
            if(s==null)throw new ArgumentException("配置文件内容为空。");s.Validate();return s;
        }
        public void Save(Settings s)
        {
            s.Validate();var dir=Path.GetDirectoryName(Path.GetFullPath(FilePath));Directory.CreateDirectory(dir);
            var temp=FilePath+".tmp";File.WriteAllText(temp,Json.Write(s),new UTF8Encoding(false));
            if(File.Exists(FilePath))File.Replace(temp,FilePath,FilePath+".bak");else File.Move(temp,FilePath);
        }
    }
}
