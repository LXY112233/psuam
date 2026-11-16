using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using QingMove;

class ActivityTests
{
    static int passed, failed;
    static string root;
    static DateTimeOffset At(string value) { return DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture); }
    static void Equal<T>(T expected,T actual,string label) { if(!object.Equals(expected,actual))throw new Exception(label+": expected "+expected+", got "+actual); }
    static void True(bool actual,string label) { Equal(true,actual,label); }
    static string Dir() { string p=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p; }
    static ActivityRecord Record(string shown,string outcome,string finished)
    { return new ActivityRecord { Id=Guid.NewGuid().ToString("D"),ShownAt=At(shown).ToString("o"),Outcome=outcome,FinishedAt=finished==null?null:At(finished).ToString("o") }; }
    static void Test(string name,Action body)
    { try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);} }
    static void Run()
    {
        // Catches missing durable writes, duplicate terminal decisions and mutable snapshots.
        Test("durable first decision and independent snapshot",delegate {
            string dir=Dir();var store=new ActivityStore(dir);var now=At("2026-11-16T09:00:00+08:00");
            string id=store.Begin(now);Equal(1,store.Snapshot().Count,"begun count");Equal("pending",store.Snapshot()[0].Outcome,"initial outcome");
            True(store.Complete(id,"done",now.AddMinutes(2)),"first completion");long revision=store.Revision;
            Equal(false,store.Complete(id,"work",now.AddMinutes(3)),"duplicate completion");Equal(false,store.Complete("missing","done",now),"unknown completion");Equal(revision,store.Revision,"duplicate revision");
            var snapshot=store.Snapshot();snapshot[0].Outcome="work";snapshot.Clear();Equal("done",store.Snapshot()[0].Outcome,"snapshot isolation");
            var reloaded=new ActivityStore(dir);Equal(1,reloaded.Snapshot().Count,"reload count");Equal("done",reloaded.Snapshot()[0].Outcome,"durable outcome");Equal(2,File.ReadLines(Path.Combine(dir,"2026-11.jsonl")).Count(),"snapshot line count");
            string json=File.ReadAllText(Path.Combine(dir,"2026-11.jsonl"));Equal(false,json.Contains("ShownTime"),"computed property excluded");
        });
        // Catches restart presenting a pending session as a user decision or recovering twice.
        Test("pending recovery is interrupted and durable",delegate {
            string dir=Dir();var store=new ActivityStore(dir);store.Begin(At("2026-11-16T09:00:00+08:00"));
            var reloaded=new ActivityStore(dir);Equal("interrupted",reloaded.Snapshot()[0].Outcome,"recovered outcome");True(reloaded.Snapshot()[0].FinishedTime.HasValue,"recovery timestamp");
            var again=new ActivityStore(dir);Equal("interrupted",again.Snapshot()[0].Outcome,"second restart");Equal(2,File.ReadLines(Path.Combine(dir,"2026-11.jsonl")).Count(),"one recovery only");
        });
        // Catches committing recovery only in memory when the existing monthly file is read-only in use.
        Test("failed recovery stays visibly pending until durable retry",delegate {
            string dir=Dir();var first=new ActivityStore(dir);first.Begin(At("2026-11-16T09:00:00+08:00"));
            using(var readLock=new FileStream(Path.Combine(dir,"2026-11.jsonl"),FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                var blocked=new ActivityStore(dir);Equal("pending",blocked.Snapshot()[0].Outcome,"not falsely recovered");Equal(1L,blocked.Revision,"no failed recovery revision");True(!string.IsNullOrEmpty(blocked.Warning),"failed recovery warning");
            }
            Equal("interrupted",new ActivityStore(dir).Snapshot()[0].Outcome,"later recovery retry");
        });
        // Catches any inherited 300-item retention limit and dropped older months.
        Test("all records survive beyond 300 and across months",delegate {
            string dir=Dir();var store=new ActivityStore(dir);var now=At("2026-01-01T09:00:00+08:00");
            for(int i=0;i<321;i++){string id=store.Begin(now.AddDays(i));store.Complete(id,"work",now.AddDays(i).AddSeconds(1));}
            var reloaded=new ActivityStore(dir);Equal(321,reloaded.Snapshot().Count,"retention");Equal("2026-01-01",reloaded.Snapshot()[0].ShownTime.ToString("yyyy-MM-dd"),"chronological first");Equal("2026-11-17",reloaded.Snapshot()[320].ShownTime.ToString("yyyy-MM-dd"),"chronological last");
        });
        // Catches grouping by response date, UTC or the running machine's timezone.
        Test("cross midnight decisions stay on shown local date",delegate {
            var data=new[]{Record("2026-12-31T23:59:00-10:00","done","2027-01-01T00:02:00-10:00")};
            var day=ActivityStats.Build(data,ActivityPeriod.Day,new DateTime(2026,12,31));Equal(1,day.Done,"shown day");Equal(1,day.Buckets[23].Done,"shown hour");Equal(24,day.Buckets.Count,"hours");Equal(1,day.ActiveDays,"active day");
            Equal(0,ActivityStats.Build(data,ActivityPeriod.Day,new DateTime(2027,1,1)).Total,"response day excluded");
            Equal(1,ActivityStats.Build(data,ActivityPeriod.Year,new DateTime(2026,1,1)).Buckets[11].Done,"shown year");
        });
        // Catches hard-coded month lengths and omitted Feb 29.
        Test("leap month and aggregate classifications",delegate {
            var data=new[]{Record("2024-02-29T10:00:00+08:00","done","2024-02-29T10:01:00+08:00"),Record("2024-02-29T11:00:00+08:00","work","2024-02-29T11:01:00+08:00"),Record("2024-02-28T10:00:00+08:00","timeout","2024-02-28T10:01:00+08:00"),Record("2024-02-28T11:00:00+08:00","pending",null)};
            var report=ActivityStats.Build(data,ActivityPeriod.Month,new DateTime(2024,2,10));Equal(29,report.Buckets.Count,"leap days");Equal(new DateTime(2024,3,1),report.End,"exclusive end");Equal(1,report.Done,"done");Equal(1,report.Work,"work");Equal(1,report.Unanswered,"unanswered");Equal(1,report.Pending,"pending");Equal(4,report.Total,"total");Equal(1,report.ActiveDays,"movement days");Equal(2,report.Buckets[28].Total,"Feb29 bucket");Equal(28,ActivityStats.Build(data,ActivityPeriod.Month,new DateTime(2025,2,1)).Buckets.Count,"ordinary February");
            report.Records[0].Outcome="changed";Equal("done",data[0].Outcome,"report record isolation");
        });
        // Catches Sunday week starts and cross-year week exclusion.
        Test("Monday week includes both years",delegate {
            var data=new[]{Record("2025-12-29T09:00:00+08:00","work","2025-12-29T09:01:00+08:00"),Record("2026-01-04T09:00:00+08:00","done","2026-01-04T09:01:00+08:00"),Record("2026-01-05T09:00:00+08:00","done","2026-01-05T09:01:00+08:00")};
            var week=ActivityStats.Build(data,ActivityPeriod.Week,new DateTime(2026,1,4));Equal(new DateTime(2025,12,29),week.Start,"Monday start");Equal(new DateTime(2026,1,5),week.End,"Monday exclusive end");Equal(7,week.Buckets.Count,"week days");Equal(2,week.Total,"cross-year count");Equal(1,week.Buckets[6].Done,"Sunday bucket");
        });
        // Catches duplicate snapshot counting, invalid-line aborts and corrupt-tail concatenation.
        Test("malformed lines and partial tail survive append repair",delegate {
            string dir=Dir();string file=Path.Combine(dir,"2026-11.jsonl");var record=Record("2026-11-16T09:00:00+08:00","done","2026-11-16T09:01:00+08:00");
            var pending=new ActivityRecord{Id=record.Id,ShownAt=record.ShownAt,Outcome="pending"};var late=new ActivityRecord{Id=record.Id,ShownAt=record.ShownAt,Outcome="work",FinishedAt=record.FinishedAt};
            File.WriteAllText(file,Json.Write(pending)+"\nnot json\n"+Json.Write(record)+"\n"+Json.Write(record)+"\n"+Json.Write(late)+"\n{\"Id\":",new UTF8Encoding(false));
            var store=new ActivityStore(dir);Equal(1,store.Snapshot().Count,"deduplicated");Equal("done",store.Snapshot()[0].Outcome,"first terminal retained");True(!string.IsNullOrEmpty(store.Warning),"visible corruption warning");
            string id=store.Begin(At("2026-11-17T09:00:00+08:00"));store.Complete(id,"work",At("2026-11-17T09:01:00+08:00"));var again=new ActivityStore(dir);Equal(2,again.Snapshot().Count,"repaired append reload");Equal("work",again.Snapshot()[1].Outcome,"new valid line");True(File.ReadAllText(file).Contains("not json"),"old content preserved");
        });
        // Catches swallowing valid final lines without a newline, or malformed JSON shapes aborting the load.
        Test("valid unterminated line and malformed shapes",delegate {
            string dir=Dir();string file=Path.Combine(dir,"2026-11.jsonl");var record=Record("2026-11-16T09:00:00+08:00","done","2026-11-16T09:01:00+08:00");
            File.WriteAllText(file,"null\n[]\n{}\n123\ntrue\n\"text\"\n"+Json.Write(record),new UTF8Encoding(false));
            var store=new ActivityStore(dir);Equal(1,store.Snapshot().Count,"valid trailing line retained");True(!string.IsNullOrEmpty(store.Warning),"invalid shapes warning");string id=store.Begin(At("2026-11-17T09:00:00+08:00"));store.Complete(id,"done",At("2026-11-17T09:01:00+08:00"));Equal(2,new ActivityStore(dir).Snapshot().Count,"newline inserted");
        });
        // Catches report builders counting raw snapshots instead of one reminder's final outcome.
        Test("report raw snapshots deduplicate first final outcome",delegate {
            var done=Record("2026-11-16T09:00:00+08:00","done","2026-11-16T09:01:00+08:00");var pending=new ActivityRecord{Id=done.Id,ShownAt=done.ShownAt,Outcome="pending"};var work=new ActivityRecord{Id=done.Id,ShownAt=done.ShownAt,Outcome="work",FinishedAt=done.FinishedAt};
            var report=ActivityStats.Build(new[]{pending,done,work,pending,done},ActivityPeriod.Day,new DateTime(2026,11,16));Equal(1,report.Total,"one reminder");Equal(1,report.Done,"first final");Equal(0,report.Work,"later final ignored");
        });
        // Catches treating every dismissal as a response or omitting legitimate terminal reasons.
        Test("all terminal reasons count once as unanswered",delegate {
            string[] reasons={"dismiss","timeout","replaced","paused","exit","interrupted","snooze"};
            var records=reasons.Select(x=>Record("2026-11-16T09:00:00+08:00",x,"2026-11-16T09:01:00+08:00")).ToArray();
            var report=ActivityStats.Build(records,ActivityPeriod.Day,new DateTime(2026,11,16));Equal(7,report.Unanswered,"all non-decision terminals");Equal(0,report.ActiveDays,"no movements");Equal(7,report.Total,"one final state each");
        });
        // Uses a real filesystem conflict: failed writes must leave both state and revision unchanged.
        Test("failed begin and complete never commit memory",delegate {
            string dir=Dir();string blocked=Path.Combine(dir,"blocked");File.WriteAllText(blocked,"occupied");var broken=new ActivityStore(blocked);bool threw=false;try{broken.Begin(At("2026-11-16T09:00:00+08:00"));}catch(IOException){threw=true;}True(threw,"failed begin throws");Equal(0,broken.Snapshot().Count,"failed begin count");Equal(0L,broken.Revision,"failed begin revision");True(!string.IsNullOrEmpty(broken.Warning),"failed begin warning");
            var store=new ActivityStore(dir);var now=At("2026-11-16T09:00:00+08:00");string id=store.Begin(now);long revision=store.Revision;
            using(var locked=new FileStream(Path.Combine(dir,"2026-11.jsonl"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {threw=false;try{store.Complete(id,"done",now.AddMinutes(1));}catch(IOException){threw=true;}True(threw,"failed complete throws");}
            Equal("pending",store.Snapshot()[0].Outcome,"failed completion pending");Equal(revision,store.Revision,"failed completion revision");True(store.Complete(id,"work",now.AddMinutes(2)),"retry succeeds");Equal("work",new ActivityStore(dir).Snapshot()[0].Outcome,"retry durable");
        });
        // Catches navigation retaining a late-month day, overflow and bad period boundaries.
        Test("period navigation and clamps",delegate {
            Equal(new DateTime(2024,2,1),ActivityStats.Shift(ActivityPeriod.Month,new DateTime(2024,1,31),1),"next month normalized");Equal(new DateTime(2027,1,1),ActivityStats.Shift(ActivityPeriod.Year,new DateTime(2026,6,1),1),"next year");Equal(new DateTime(2025,12,22),ActivityStats.Shift(ActivityPeriod.Week,new DateTime(2026,1,4),-1),"previous Monday");Equal(new DateTime(1900,1,1),ActivityStats.Shift(ActivityPeriod.Day,new DateTime(1900,1,1),-1),"lower clamp");Equal(new DateTime(9998,12,31),ActivityStats.Shift(ActivityPeriod.Day,new DateTime(9998,12,31),1),"upper clamp");Equal(12,ActivityStats.Build(new ActivityRecord[0],ActivityPeriod.Year,new DateTime(2026,1,1)).Buckets.Count,"empty year buckets");
        });
        // Catches accepting an unsupported result that can never be classified, or mutating on argument errors.
        Test("invalid completion is rejected without losing pending record",delegate {
            var store=new ActivityStore(Dir());var now=At("2026-11-16T09:00:00+08:00");string id=store.Begin(now);long revision=store.Revision;bool rejected=false;
            try{store.Complete(id,"pending",now);}catch(ArgumentException){rejected=true;}
            True(rejected,"invalid outcome rejected");Equal(revision,store.Revision,"argument error revision");Equal("pending",store.Snapshot()[0].Outcome,"argument error pending");True(store.Complete(id,"done",now.AddMinutes(1)),"valid completion still possible");
        });
    }
    public static int Main()
    {
        root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"activity-test-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{Run();}finally{Directory.Delete(root,true);}
        Console.WriteLine("Activity tests: "+passed+" passed, "+failed+" failed.");return failed==0?0:1;
    }
}
