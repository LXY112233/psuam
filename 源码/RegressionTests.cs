using System;
using System.Reflection;
using System.Globalization;
using QingMove;
class RegressionTests
{
    static int count,failed;
    static DateTime D(string text){return DateTime.ParseExact(text,"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);}
    static void Case(string name,Action test){try{test();count++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static void Main()
    {
        Case("valid interval can occur beyond fifteen days",delegate
        {
            var s=new Settings(){Mode="interval",IntervalMinutes=1439,UseWindow=true,WindowStart="11:30",WindowEnd="11:31"};
            if(Schedule.Next(s,D("2026-11-16 12:00:00"))!=D("2026-12-16 11:30:00"))throw new Exception("A valid reminder was incorrectly rejected");
        });
        Case("saving settings preserves active pause",delegate
        {
            var now=D("2026-11-16 12:00:00");var engine=new ReminderEngine(new Settings(),now);engine.Pause(now,30);
            var reconfigure=typeof(ReminderEngine).GetMethod("Reconfigure",BindingFlags.Public|BindingFlags.Instance);
            var updated=reconfigure==null?new ReminderEngine(new Settings(),now.AddMinutes(5)):(ReminderEngine)reconfigure.Invoke(engine,new object[]{new Settings(),now.AddMinutes(5)});
            if(updated.PausedUntil!=now.AddMinutes(30))throw new Exception("Active pause was lost when settings changed");
        });
        Case("impossible interval is still rejected",delegate
        {
            var s=new Settings(){Mode="interval",IntervalMinutes=1440,UseWindow=true,WindowStart="09:00",WindowEnd="10:00"};
            if(Schedule.Next(s,D("2026-11-16 12:00:00"))!=null)throw new Exception("An impossible reminder was accepted");
        });
        Case("settings changes retain snooze",delegate
        {
            var now=D("2026-11-16 12:00:00");var engine=new ReminderEngine(new Settings(),now);engine.Snooze(now,5);
            var updated=engine.Reconfigure(new Settings(){Accent="purple"},now.AddMinutes(1));
            if(updated.NextDue!=now.AddMinutes(5)||!updated.Tick(now.AddMinutes(5)))throw new Exception("Snoozed reminder lost");
        });
        Case("expired pause is not revived",delegate
        {
            var now=D("2026-11-16 12:00:00");var engine=new ReminderEngine(new Settings(),now);engine.Pause(now,5);
            if(engine.Reconfigure(new Settings(),now.AddMinutes(6)).PausedUntil.HasValue)throw new Exception("Expired pause retained");
        });
        Console.WriteLine(count+" REGRESSION TESTS PASSED; "+failed+" FAILED");Environment.ExitCode=failed==0?0:1;
    }
}
