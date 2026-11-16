using System;
using System.IO;
using System.Linq;
using QingMove;

class UpdateScheduleTests
{
    static string root;
    static int passed,failed;
    static void Equal<T>(T expected,T actual,string label){if(!object.Equals(expected,actual))throw new Exception(label+": expected "+expected+", got "+actual);}
    static void True(bool value,string label){Equal(true,value,label);}
    static string PathForState(){string dir=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);return Path.Combine(dir,"schedule.json");}
    static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex);}}
    static void Remember(UpdateSchedule schedule,string repo){schedule.RememberAvailable(repo,"1.6.0","v1.6.0","Release notes","https://github.com/"+repo+"/releases/tag/v1.6.0");}
    static void Run()
    {
        // Catches checking before 09:00, treating an empty repository as an attempt, or missing exact boundary.
        Test("09 local boundary and no repository",delegate{
            string file=PathForState();var schedule=new UpdateSchedule(file);var date=new DateTime(2026,11,16);
            Equal(false,schedule.TryBegin(date.AddHours(8).AddMinutes(59).AddSeconds(59),"owner/repo"),"before nine");Equal(false,File.Exists(file),"no premature reservation");Equal(false,schedule.TryBegin(date.AddHours(9),null),"empty repository");True(schedule.TryBegin(date.AddHours(9),"owner/repo"),"exact nine");Equal(false,schedule.TryBegin(date.AddHours(9).AddSeconds(1),"owner/repo"),"same day once");
        });
        // Catches losing the durable reservation between application starts or not catching up after 09:00.
        Test("late startup and restart exactly once",delegate{
            string file=PathForState();var date=new DateTime(2026,11,16,18,20,0);True(new UpdateSchedule(file).TryBegin(date,"owner/repo"),"catch up");Equal(false,new UpdateSchedule(file).TryBegin(date.AddHours(1),"owner/repo"),"restart no duplicate");
        });
        // Catches a midnight reset allowing an early check, and failing to rearm on a new day.
        Test("day rollover waits for nine",delegate{
            var schedule=new UpdateSchedule(PathForState());True(schedule.TryBegin(new DateTime(2026,11,16,9,0,0),"owner/repo"),"first day");Equal(false,schedule.TryBegin(new DateTime(2026,11,17,8,59,59),"owner/repo"),"next day before nine");True(schedule.TryBegin(new DateTime(2026,11,17,9,0,0),"owner/repo"),"next nine");Equal(false,schedule.TryBegin(new DateTime(2026,11,17,22,0,0),"owner/repo"),"next day once");
        });
        // Catches treating a repo switch as an old repo's check, or duplicating when switched back/case changes.
        Test("repository changes have independent daily reservations",delegate{
            string file=PathForState();var schedule=new UpdateSchedule(file);var now=new DateTime(2026,11,16,10,0,0);True(schedule.TryBegin(now,"Owner/First"),"first repo");True(schedule.TryBegin(now,"owner/second"),"new repo catch up");Equal(false,schedule.TryBegin(now,"owner/first"),"return to first");Equal(false,new UpdateSchedule(file).TryBegin(now,"OWNER/SECOND"),"case normalized restart");
        });
        // Catches UTC conversion or assuming February has 28 days.
        Test("leap day uses supplied local date",delegate{
            var schedule=new UpdateSchedule(PathForState());True(schedule.TryBegin(new DateTime(2024,2,28,23,30,0,DateTimeKind.Utc),"owner/repo"),"Feb28 local fields");True(schedule.TryBegin(new DateTime(2024,2,29,9,0,0,DateTimeKind.Utc),"owner/repo"),"leap day");True(schedule.TryBegin(new DateTime(2024,3,1,9,0,0,DateTimeKind.Utc),"owner/repo"),"March first");
        });
        // Catches losing latest release information over restart or cache updates silently marking daily checks done.
        Test("badge cache survives restart and does not reserve daily check",delegate{
            string file=PathForState();var schedule=new UpdateSchedule(file);Remember(schedule,"Owner/Repo");var loaded=new UpdateSchedule(file);Equal("owner/repo",loaded.CachedRepository,"cached repository");Equal("1.6.0",loaded.CachedVersion,"cached version");Equal("v1.6.0",loaded.CachedTag,"cached tag");Equal("Release notes",loaded.CachedNotes,"cached notes");Equal("https://github.com/Owner/Repo/releases/tag/v1.6.0",loaded.CachedUrl,"cached URL");True(loaded.TryBegin(new DateTime(2026,11,16,9,0,0),"owner/repo"),"manual/cache path independent");
        });
        // Catches a stale response for repo A clearing repo B's badge or latest=current not clearing the active badge.
        Test("cache clear respects repository and persists",delegate{
            string file=PathForState();var schedule=new UpdateSchedule(file);Remember(schedule,"owner/first");Remember(schedule,"owner/second");schedule.ClearAvailable("owner/first");Equal("owner/second",schedule.CachedRepository,"old repo response ignored");schedule.ClearAvailable("OWNER/SECOND");Equal(null,schedule.CachedVersion,"active cache cleared");Equal(null,new UpdateSchedule(file).CachedRepository,"clear durable");
        });
        // Catches corruption aborting startup or overwriting the only evidence of the broken state.
        Test("corrupt state warns and preserves original before replacement",delegate{
            string file=PathForState();File.WriteAllText(file,"{broken-state");var schedule=new UpdateSchedule(file);True(!string.IsNullOrEmpty(schedule.Warning),"corruption visible");True(schedule.TryBegin(new DateTime(2026,11,16,9,0,0),"owner/repo"),"safe fresh gate");string[] preserved=Directory.GetFiles(Path.GetDirectoryName(file),"*.invalidcopy");Equal(1,preserved.Length,"one original preserved");Equal("{broken-state",File.ReadAllText(preserved[0]),"original bytes");Equal(false,new UpdateSchedule(file).TryBegin(new DateTime(2026,11,16,10,0,0),"owner/repo"),"new state valid");
        });
        // Catches repeated HTTP attempts every timer tick while disk writes fail, and leaking temp files.
        Test("write failure still reserves once in memory",delegate{
            string file=PathForState();var schedule=new UpdateSchedule(file);var now=new DateTime(2026,11,16,9,0,0);Directory.CreateDirectory(file);True(schedule.TryBegin(now,"owner/repo"),"first attempt allowed");True(!string.IsNullOrEmpty(schedule.Warning),"write warning");Equal(false,schedule.TryBegin(now.AddSeconds(1),"owner/repo"),"no per-second retry");Equal(0,Directory.GetFiles(Path.GetDirectoryName(file),"*.tmp-*").Length,"temps cleaned");
        });
        // Catches trusting a tampered notification URL, unbounded notes, or replacing good cache on invalid input.
        Test("cache validates URL version and bounds notes",delegate{
            string file=PathForState();var schedule=new UpdateSchedule(file);Remember(schedule,"owner/repo");string[] urls={"http://github.com/owner/repo/releases/tag/v1.6.0","https://evil.test/owner/repo/releases/tag/v1.6.0","https://github.com/owner/other/releases/tag/v1.6.0","https://github.com/owner/repo/releases/tag/v1.6.0?next=evil"};
            foreach(string url in urls){bool rejected=false;try{schedule.RememberAvailable("owner/repo","1.6.0","v1.6.0","notes",url);}catch(ArgumentException){rejected=true;}True(rejected,"untrusted URL rejected");}
            bool invalidVersion=false;try{schedule.RememberAvailable("owner/repo","not-a-version","v1.6.0","notes","https://github.com/owner/repo/releases/tag/v1.6.0");}catch(ArgumentException){invalidVersion=true;}True(invalidVersion,"version rejected");Equal("1.6.0",schedule.CachedVersion,"good cache retained");
            schedule.RememberAvailable("owner/repo","1.6.0","v1.6.0",new string('x',60000),"https://github.com/owner/repo/releases/tag/v1.6.0");Equal(50000,schedule.CachedNotes.Length,"notes bounded");Equal(50000,new UpdateSchedule(file).CachedNotes.Length,"bounded notes reload");
        });
        // Catches loading a hand-edited URL into the badge while discarding a valid same-day gate.
        Test("tampered cache is ignored without losing daily reservation",delegate{
            string file=PathForState();var now=new DateTime(2026,11,16,9,0,0);var schedule=new UpdateSchedule(file);schedule.TryBegin(now,"owner/repo");Remember(schedule,"owner/repo");File.WriteAllText(file,File.ReadAllText(file).Replace("https://github.com/owner/repo/releases/tag/v1.6.0","https://evil.test/owner/repo/releases/tag/v1.6.0"));
            var loaded=new UpdateSchedule(file);Equal(null,loaded.CachedRepository,"tampered cache cleared");True(!string.IsNullOrEmpty(loaded.Warning),"tampering warning");Equal(false,loaded.TryBegin(now.AddHours(1),"owner/repo"),"valid reservation retained");loaded.ClearAvailable("owner/repo");Remember(loaded,"owner/repo");Equal(1,Directory.GetFiles(Path.GetDirectoryName(file),"*.invalidcopy").Length,"tampered original preserved");
        });
        // Catches never persisting an in-memory reservation after the failed path becomes writable again.
        Test("later successful save preserves earlier failed reservation",delegate{
            string file=PathForState();var now=new DateTime(2026,11,16,9,0,0);var schedule=new UpdateSchedule(file);Directory.CreateDirectory(file);True(schedule.TryBegin(now,"owner/repo"),"first in memory");Directory.Delete(file);Remember(schedule,"owner/repo");Equal(false,new UpdateSchedule(file).TryBegin(now.AddHours(1),"owner/repo"),"reservation saved with cache");
        });
        // Catches non-object JSON shapes and huge numeric conversions escaping corrupt-state handling.
        Test("malformed state shapes safely reset",delegate{
            string[] invalid={"null","[]","123","true","\"text\"","{}","{\"Schema\":999999999999999999999999999}","{\"Schema\":1,\"AttemptDate\":\"2026-02-30\",\"AttemptedRepositories\":[]}"};
            foreach(string text in invalid){string file=PathForState();File.WriteAllText(file,text);var schedule=new UpdateSchedule(file);True(!string.IsNullOrEmpty(schedule.Warning),"invalid state warning");True(schedule.TryBegin(new DateTime(2026,11,16,9,0,0),"owner/repo"),"safe fresh reservation");}
        });
        // Catches non-atomic replacement and failing to retain the previous known-good state.
        Test("atomic replacement preserves prior state backup",delegate{
            string file=PathForState();var schedule=new UpdateSchedule(file);schedule.TryBegin(new DateTime(2026,11,16,9,0,0),"owner/repo");string original=File.ReadAllText(file);Remember(schedule,"owner/repo");Equal(original,File.ReadAllText(file+".bak"),"previous state backup");Equal(0,Directory.GetFiles(Path.GetDirectoryName(file),"*.tmp-*").Length,"no leftover temps");
        });
    }
    public static int Main()
    {
        root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"schedule-test-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{Run();}finally{Directory.Delete(root,true);}
        Console.WriteLine("Update schedule tests: "+passed+" passed, "+failed+" failed.");return failed==0?0:1;
    }
}
