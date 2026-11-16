using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
namespace QingMove
{
    public enum ActivityPeriod { Day,Week,Month,Year }
    public class ActivityRecord
    {
        public string Id,ShownAt,FinishedAt,Outcome;
        [ScriptIgnore] public DateTimeOffset ShownTime { get {return DateTimeOffset.ParseExact(ShownAt,"o",CultureInfo.InvariantCulture,DateTimeStyles.None);} }
        [ScriptIgnore] public DateTimeOffset? FinishedTime { get {return string.IsNullOrEmpty(FinishedAt)?(DateTimeOffset?)null:DateTimeOffset.ParseExact(FinishedAt,"o",CultureInfo.InvariantCulture,DateTimeStyles.None);} }
        internal ActivityRecord Copy() {return new ActivityRecord{Id=Id,ShownAt=ShownAt,FinishedAt=FinishedAt,Outcome=Outcome};}
        internal static bool Terminal(string outcome)
        {return outcome=="done" || outcome=="work" || outcome=="dismiss" || outcome=="timeout" || outcome=="replaced" || outcome=="paused" || outcome=="exit" || outcome=="interrupted" || outcome=="snooze";}
        internal static bool Valid(ActivityRecord record)
        {
            Guid id;DateTimeOffset stamp;
            return record!=null && Guid.TryParse(record.Id,out id) &&
                DateTimeOffset.TryParseExact(record.ShownAt,"o",CultureInfo.InvariantCulture,DateTimeStyles.None,out stamp) &&
                ((record.Outcome=="pending" && string.IsNullOrEmpty(record.FinishedAt)) ||
                (Terminal(record.Outcome) && DateTimeOffset.TryParseExact(record.FinishedAt,"o",CultureInfo.InvariantCulture,DateTimeStyles.None,out stamp)));
        }
    }
    public class ActivityStore
    {
        readonly string directory;
        readonly object gate=new object();
        readonly Dictionary<string,ActivityRecord> records=new Dictionary<string,ActivityRecord>(StringComparer.OrdinalIgnoreCase);
        public ActivityStore(string directory)
        {
            if(string.IsNullOrWhiteSpace(directory))throw new ArgumentException("签到记录目录不能为空。","directory");
            this.directory=Path.GetFullPath(directory);
            Load();
            foreach(var record in records.Values.Where(x=>x.Outcome=="pending").ToArray())
            {
                try{Complete(record.Id,"interrupted",DateTimeOffset.Now);}
                catch(IOException){ }
                catch(UnauthorizedAccessException){ }
            }
        }
        public string Warning {get;private set;}
        public long Revision {get;private set;}
        void Warn(string message)
        {
            if(string.IsNullOrEmpty(Warning))Warning=message;
            else if(Warning.IndexOf(message,StringComparison.Ordinal)<0 && Warning.Length<1500)Warning+="\r\n"+message;
        }
        static bool MonthlyFile(string file)
        {
            DateTime month;string name=Path.GetFileNameWithoutExtension(file);
            return name.Length==7 && DateTime.TryParseExact(name,"yyyy-MM",CultureInfo.InvariantCulture,DateTimeStyles.None,out month);
        }
        void Load()
        {
            try
            {
                if(!Directory.Exists(directory))return;
                foreach(string file in Directory.EnumerateFiles(directory,"*.jsonl").Where(MonthlyFile).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        using(var reader=new StreamReader(file,Encoding.UTF8,true))
                        {
                            string line;
                            while((line=reader.ReadLine())!=null)
                            {
                                if(string.IsNullOrWhiteSpace(line))continue;
                                ActivityRecord record=null;
                                try{record=Json.Read<ActivityRecord>(line);}catch(ArgumentException){ }catch(InvalidOperationException){ }
                                if(!ActivityRecord.Valid(record))
                                {Warn("签到记录中存在损坏或未写完的行，已保留原文件并跳过（"+Path.GetFileName(file)+"）。");continue;}
                                record.Id=Guid.Parse(record.Id).ToString("D");
                                ActivityRecord existing;
                                if(records.TryGetValue(record.Id,out existing))
                                {
                                    if(existing.ShownAt!=record.ShownAt)
                                    {Warn("签到记录包含冲突的提醒编号，已保留首次记录。");continue;}
                                    if(existing.Outcome!="pending")continue;
                                    if(record.Outcome=="pending")continue;
                                }
                                records[record.Id]=record.Copy();Revision++;
                            }
                        }
                    }
                    catch(IOException ex){Warn("无法读取部分签到记录："+ex.Message);}
                    catch(UnauthorizedAccessException ex){Warn("无法读取部分签到记录："+ex.Message);}
                }
            }
            catch(IOException ex){Warn("无法读取签到记录目录："+ex.Message);}
            catch(UnauthorizedAccessException ex){Warn("无法读取签到记录目录："+ex.Message);}
        }
        void Append(ActivityRecord record)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,record.ShownTime.ToString("yyyy-MM",CultureInfo.InvariantCulture)+".jsonl");
                byte[] bytes=new UTF8Encoding(false).GetBytes(Json.Write(record)+"\n");
                using(var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read))
                {
                    if(stream.Length>0)
                    {
                        stream.Seek(-1,SeekOrigin.End);int last=stream.ReadByte();stream.Seek(0,SeekOrigin.End);
                        if(last!=10)stream.WriteByte(10);
                    }
                    stream.Write(bytes,0,bytes.Length);stream.Flush(true);
                }
            }
            catch(IOException ex){Warn("签到记录未能保存，请检查目录后重试："+ex.Message);throw;}
            catch(UnauthorizedAccessException ex){Warn("签到记录未能保存，请检查目录后重试："+ex.Message);throw;}
        }
        // Persistence errors propagate to the UI; an unrecorded decision must not look successful.
        public string Begin(DateTimeOffset now)
        {
            lock(gate)
            {
                var record=new ActivityRecord{Id=Guid.NewGuid().ToString("D"),ShownAt=now.ToString("o",CultureInfo.InvariantCulture),Outcome="pending"};
                Append(record);records.Add(record.Id,record);Revision++;return record.Id;
            }
        }
        // False means unknown ID or already terminal. The first durable terminal outcome wins.
        public bool Complete(string id,string outcome,DateTimeOffset now)
        {
            if(!ActivityRecord.Terminal(outcome))throw new ArgumentException("签到结果无效。","outcome");
            lock(gate)
            {
                ActivityRecord existing;
                if(id==null || !records.TryGetValue(id,out existing) || existing.Outcome!="pending")return false;
                var record=existing.Copy();record.Outcome=outcome;record.FinishedAt=now.ToString("o",CultureInfo.InvariantCulture);
                Append(record);records[id]=record;Revision++;return true;
            }
        }
        public IList<ActivityRecord> Snapshot()
        {
            lock(gate)return records.Values.OrderBy(x=>x.ShownTime.DateTime).ThenBy(x=>x.Id,StringComparer.Ordinal).Select(x=>x.Copy()).ToList();
        }
    }
    public class ActivityBucket
    { public DateTime Start; public string Label; public int Done,Work,Unanswered,Pending,Total; }
    public class ActivityReport
    { public DateTime Start,End; public string Title; public int Done,Work,Unanswered,Pending,ActiveDays,Total; public List<ActivityBucket> Buckets=new List<ActivityBucket>(); public List<ActivityRecord> Records=new List<ActivityRecord>(); }
    public static class ActivityStats
    {
        static readonly DateTime Min=new DateTime(1900,1,1),Max=new DateTime(9998,12,31);
        static DateTime Clamp(DateTime value) {return DateTime.SpecifyKind(value<Min?Min:value>Max?Max:value.Date,DateTimeKind.Unspecified);}
        public static DateTime StartOf(ActivityPeriod period,DateTime anchor)
        {
            DateTime date=Clamp(anchor);
            switch(period)
            {
                case ActivityPeriod.Day:return date;
                case ActivityPeriod.Week:return date.AddDays(-(((int)date.DayOfWeek+6)%7));
                case ActivityPeriod.Month:return new DateTime(date.Year,date.Month,1);
                case ActivityPeriod.Year:return new DateTime(date.Year,1,1);
                default:throw new ArgumentOutOfRangeException("period");
            }
        }
        public static DateTime EndOf(ActivityPeriod period,DateTime anchor)
        {
            DateTime start=StartOf(period,anchor);
            switch(period)
            {
                case ActivityPeriod.Day:return start.AddDays(1);
                case ActivityPeriod.Week:return start.AddDays(7);
                case ActivityPeriod.Month:return start.AddMonths(1);
                case ActivityPeriod.Year:return start.AddYears(1);
                default:throw new ArgumentOutOfRangeException("period");
            }
        }
        public static DateTime Shift(ActivityPeriod period,DateTime anchor,int direction)
        {
            DateTime start=StartOf(period,anchor),moved;
            try
            {
                switch(period)
                {
                    case ActivityPeriod.Day:moved=start.AddDays(direction);break;
                    case ActivityPeriod.Week:moved=start.AddDays((double)direction*7);break;
                    case ActivityPeriod.Month:moved=start.AddMonths(direction);break;
                    case ActivityPeriod.Year:moved=start.AddYears(direction);break;
                    default:throw new ArgumentOutOfRangeException("period");
                }
            }
            catch(ArgumentOutOfRangeException){moved=direction<0?Min:Max;}
            return StartOf(period,Clamp(moved));
        }
        public static ActivityReport Build(IEnumerable<ActivityRecord> records,ActivityPeriod period,DateTime anchor)
        {
            var report=new ActivityReport{Start=StartOf(period,anchor),End=EndOf(period,anchor)};
            report.Title=period==ActivityPeriod.Year?report.Start.ToString("yyyy 年",CultureInfo.InvariantCulture):
                period==ActivityPeriod.Month?report.Start.ToString("yyyy 年 M 月",CultureInfo.InvariantCulture):
                period==ActivityPeriod.Week?report.Start.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture)+" – "+report.End.AddDays(-1).ToString("yyyy.MM.dd",CultureInfo.InvariantCulture):
                report.Start.ToString("yyyy 年 M 月 d 日",CultureInfo.InvariantCulture);
            int count=period==ActivityPeriod.Day?24:period==ActivityPeriod.Week?7:period==ActivityPeriod.Month?DateTime.DaysInMonth(report.Start.Year,report.Start.Month):12;
            for(int i=0;i<count;i++)
            {
                DateTime start=period==ActivityPeriod.Day?report.Start.AddHours(i):period==ActivityPeriod.Year?report.Start.AddMonths(i):report.Start.AddDays(i);
                string label=period==ActivityPeriod.Day?i.ToString("00",CultureInfo.InvariantCulture):period==ActivityPeriod.Year?(i+1).ToString(CultureInfo.InvariantCulture)+"月":period==ActivityPeriod.Week?new[]{"周一","周二","周三","周四","周五","周六","周日"}[i]:(i+1).ToString(CultureInfo.InvariantCulture);
                report.Buckets.Add(new ActivityBucket{Start=start,Label=label});
            }
            // The encoded offset is part of the user's historical local calendar. Never call ToLocalTime here.
            var unique=new Dictionary<string,ActivityRecord>(StringComparer.OrdinalIgnoreCase);
            foreach(var record in records??Enumerable.Empty<ActivityRecord>())
            {
                if(!ActivityRecord.Valid(record))continue;
                string id=Guid.Parse(record.Id).ToString("D");ActivityRecord previous;
                if(unique.TryGetValue(id,out previous) && (previous.Outcome!="pending" || previous.ShownAt!=record.ShownAt))continue;
                unique[id]=record.Copy();
            }
            var active=new HashSet<DateTime>();
            foreach(var record in unique.Values.OrderBy(x=>x.ShownTime.DateTime).ThenBy(x=>x.Id,StringComparer.Ordinal))
            {
                DateTime shown=record.ShownTime.DateTime;
                if(shown<report.Start || shown>=report.End)continue;
                report.Records.Add(record.Copy());report.Total++;
                int index=period==ActivityPeriod.Day?shown.Hour:period==ActivityPeriod.Year?shown.Month-1:(shown.Date-report.Start).Days;
                ActivityBucket bucket=report.Buckets[index];bucket.Total++;
                if(record.Outcome=="done"){report.Done++;bucket.Done++;active.Add(shown.Date);}
                else if(record.Outcome=="work"){report.Work++;bucket.Work++;}
                else if(record.Outcome=="pending"){report.Pending++;bucket.Pending++;}
                else{report.Unanswered++;bucket.Unanswered++;}
            }
            report.ActiveDays=active.Count;return report;
        }
    }
}
