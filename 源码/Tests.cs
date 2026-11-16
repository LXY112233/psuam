using System;
using System.IO;
using QingMove;

class Tests
{
    static int count;
    static DateTime D(string s) { return DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture); }
    static void Eq<T>(T a, T b, string name) { if (!object.Equals(a,b)) throw new Exception(name + ": expected " + b + ", got " + a); count++; Console.WriteLine("PASS " + name); }
    static void Invalid(Action a, string name) { try { a(); } catch (ArgumentException) { count++; Console.WriteLine("PASS " + name); return; } throw new Exception("Expected invalid: " + name); }
    static void Main() { try { Run(); } catch(Exception ex) { Console.WriteLine("FAIL: " + ex.Message); Environment.ExitCode=1; } }
    static void Run()
    {
        var s = new Settings();
        Eq(Json.Read<Settings>("{\"Title\":\"旧版设置\"}").Position,"bottom-right","legacy config defaults to bottom right");
        foreach(var position in new[]{"top-left","top-right","bottom-left","bottom-right","center"})
        {
            var positionPath=Path.Combine(Path.GetTempPath(),"QingMove-position-"+Guid.NewGuid()+".json");
            try
            {
                var configured=new Settings(){Position=position};var store=new SettingsStore(positionPath);store.Save(configured);
                Eq(store.Load().Position,position,"saved position survives reload: "+position);
                Eq(configured.Copy().Position,position,"notification copy preserves position: "+position);
            }
            finally{if(File.Exists(positionPath))File.Delete(positionPath);}
        }
        Invalid(delegate{new Settings(){Position="invalid"}.Validate();},"reject invalid position");
        Invalid(delegate{new Settings(){Position=null}.Validate();},"reject null position");
        Eq(Schedule.Next(s, D("2026-11-11 14:29:59")), (DateTime?)D("2026-11-11 14:30:00"), "hourly at next minute");
        Eq(Schedule.Next(s, D("2026-11-11 14:30:00")), (DateTime?)D("2026-11-11 15:30:00"), "no duplicate at exact boundary");
        s.HourMinutes = "0,30";
        Eq(Schedule.Next(s, D("2026-11-11 23:31:00")), (DateTime?)D("2026-11-12 00:00:00"), "hourly midnight rollover");
        s.Mode = "interval"; s.IntervalMinutes=30;
        Eq(Schedule.Next(s, D("2026-11-11 14:10:00")), (DateTime?)D("2026-11-11 14:40:00"), "interval anchored to start");
        s.Mode="daily"; s.DailyTimes="09:00,14:30,18:00";
        Eq(Schedule.Next(s, D("2026-11-11 18:00:00")), (DateTime?)D("2026-11-12 09:00:00"), "daily rolls to tomorrow");
        s = new Settings(); s.Days = new int[]{1,2,3,4,5}; s.UseWindow=true; s.WindowStart="09:00"; s.WindowEnd="18:00";
        Eq(Schedule.Next(s,D("2026-11-13 18:00:00")), (DateTime?)D("2026-11-16 09:30:00"), "skip weekend and closed window");
        Eq(Schedule.Allowed(s,D("2026-11-11 18:00:00")),false,"exclusive window end");
        s.WindowStart="22:00";s.WindowEnd="06:00";
        Eq(Schedule.Allowed(s,D("2026-11-11 23:30:00")),true,"overnight late");
        Eq(Schedule.Allowed(s,D("2026-11-11 05:30:00")),true,"overnight early");
        Eq(Schedule.Allowed(s,D("2026-11-11 12:30:00")),false,"overnight midday excluded");
        Invalid(delegate { var x=new Settings();x.HourMinutes="60";x.Validate(); },"minute out of range");
        Invalid(delegate { var x=new Settings();x.Days=new int[0];x.Validate(); },"no active day");
        Invalid(delegate { var x=new Settings();x.DailyTimes="25:00";x.Validate(); },"invalid clock");
        Invalid(delegate { var x=new Settings();x.IntervalMinutes=0;x.Validate(); },"zero interval");
        Invalid(delegate { var x=new Settings();x.Title=null;x.Validate(); },"null title");
        Invalid(delegate { var x=new Settings();x.UseWindow=true;x.WindowStart="09:00";x.WindowEnd="09:00";x.Validate(); },"ambiguous empty window");
        var engine=new ReminderEngine(new Settings(),D("2026-11-11 14:20:00"));
        Eq(engine.Tick(D("2026-11-11 14:30:00")),true,"scheduled event fires");
        Eq(engine.Tick(D("2026-11-11 14:30:01")),false,"event fires once");
        engine=new ReminderEngine(new Settings(),D("2026-11-11 14:20:00"));
        Eq(engine.Tick(D("2026-11-11 14:30:40")),true,"short delayed tick accepted");
        engine=new ReminderEngine(new Settings(),D("2026-11-11 14:20:00"));
        Eq(engine.Tick(D("2026-11-11 17:42:00")),false,"sleep does not cause old popup");
        Eq(engine.NextDue,(DateTime?)D("2026-11-11 18:30:00"),"reschedule after waking");
        engine=new ReminderEngine(new Settings(),D("2026-11-11 14:20:00"));
        engine.Pause(D("2026-11-11 14:21:00"),30);
        Eq(engine.Tick(D("2026-11-11 14:30:00")),false,"pause suppresses reminder");
        Eq(engine.Tick(D("2026-11-11 14:51:00")),false,"no catchup on pause end");
        engine.Snooze(D("2026-11-11 14:51:00"),5);
        Eq(engine.Tick(D("2026-11-11 14:56:00")),true,"snooze emits");
        Eq(engine.Tick(D("2026-11-11 14:56:01")),false,"snooze consumed once");
        var interval = new Settings();interval.Mode="interval";interval.IntervalMinutes=30;
        engine=new ReminderEngine(interval,D("2026-11-11 14:10:00"));
        engine.Tick(D("2026-11-11 14:40:01"));
        Eq(engine.NextDue,(DateTime?)D("2026-11-11 15:10:00"),"interval does not drift per tick");
        var path=Path.Combine(Path.GetTempPath(),"QingMove-test-"+Guid.NewGuid()+".json");
        try { var store=new SettingsStore(path); s=new Settings();s.Title="起来走动一下";store.Save(s);Eq(store.Load().Title,"起来走动一下","unicode config round trip"); File.WriteAllText(path,"{broken"); bool failed=false;try {store.Load();}catch {failed=true;}Eq(failed,true,"corrupt JSON reported"); } finally { if(File.Exists(path))File.Delete(path); }
        Console.WriteLine("ALL " + count + " TESTS PASSED");
    }
}
