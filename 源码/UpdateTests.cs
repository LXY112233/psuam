using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using QingMove;
[assembly:AssemblyVersion("1.5.0.0")]
class UpdateTests
{
    sealed class FixtureHandler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage,HttpResponseMessage> Respond;
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();Calls++;return Task.FromResult(Respond(request));}
    }
    static int count;
    static string root;
    static void Eq<T>(T actual,T expected,string name){if(!object.Equals(actual,expected))throw new Exception(name+": "+actual+" != "+expected);count++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){try{action();}catch(Exception ex){if(ex is InvalidOperationException||ex is ArgumentException||ex is InvalidDataException||ex is IOException){count++;Console.WriteLine("PASS "+name);return;}throw;}throw new Exception("Accepted invalid input: "+name);}
    static string Release(string tag,bool draft,bool prerelease,string digest)
    {
        string asset="PSUAM-windows-"+tag+".zip";
        return new JavaScriptSerializer().Serialize(new {tag_name=tag,draft=draft,prerelease=prerelease,body="Changes",html_url="https://github.com/test/psuam/releases/tag/"+tag,assets=new[]{new{name=asset,browser_download_url="https://github.com/test/psuam/releases/download/"+tag+"/"+asset,size=123,digest=digest}}});
    }
    static string Zip(string name,string exeEntry,string configEntry,bool duplicate)
    {
        string path=Path.Combine(root,name+".zip");
        using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Assembly.GetExecutingAssembly().Location,exeEntry);
            if(configEntry!=null)using(var writer=new StreamWriter(archive.CreateEntry(configEntry).Open()))writer.Write("<configuration><startup><supportedRuntime version=\"v4.0\"/></startup></configuration>");
            if(duplicate)archive.CreateEntry(exeEntry.ToUpperInvariant());
        }
        return path;
    }
    static void Main(){root=Path.Combine(Path.GetTempPath(),"PSUAM-update-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{Run();Console.WriteLine("Passed "+count+" update checks.");}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}finally{Directory.Delete(root,true);}}
    static void Run()
    {
        Eq(UpdateService.NormalizeRepository(" https://github.com/Test/psuam/ "),"Test/psuam","normalize GitHub URL");
        Eq(UpdateService.NormalizeRepository("Test/psuam"),"Test/psuam","normalize owner/repo");
        foreach(string bad in new[]{"http://github.com/test/psuam","https://github.com.evil.test/test/psuam","https://user@github.com/test/psuam","../repo","test/psuam/issues","test/psuam?x=1","https://github.com/test/psuam#main","test/..","test/a%2fb"})Reject(delegate{UpdateService.NormalizeRepository(bad);},"repository: "+bad);
        string hash=new string('a',64);
        var release=UpdateService.ParseRelease(Release("v1.5.0",false,false,"sha256:"+hash),"test/psuam",new Version(1,4,1,0));
        Eq(release.Version,new Version(1,5,0,0),"three component version normalized");Eq(release.IsNewer,true,"upgrade available");Eq(release.Sha256,hash,"GitHub digest parsed");
        Eq(UpdateService.ParseRelease(Release("v1.5.0",false,false,"sha256:"+hash),"TEST/PSUAM",new Version(1,0)).IsNewer,true,"GitHub repository path is case insensitive");
        Eq(UpdateService.ParseRelease(Release("v1.5.0",false,false,null),"test/psuam",new Version(1,5,0,0)).IsNewer,false,"equal version is current");
        Eq(UpdateService.ParseRelease(Release("v1.5.0.0",false,false,null),"test/psuam",new Version(1,6,0,0)).IsNewer,false,"local newer version is current");
        Reject(delegate{UpdateService.ParseRelease(Release("v1.5.0",true,false,null),"test/psuam",new Version(1,0));},"draft rejected");
        Reject(delegate{UpdateService.ParseRelease(Release("v1.5.0",false,true,null),"test/psuam",new Version(1,0));},"prerelease rejected");
        foreach(string tag in new[]{"v1.5","v1.5.0-beta","garbage","v1.5.0.999999999999"})Reject(delegate{UpdateService.ParseRelease(Release(tag,false,false,null),"test/psuam",new Version(1,0));},"tag: "+tag);
        Reject(delegate{UpdateService.ParseRelease(Release("v1.5.0",false,false,"sha256:bad"),"test/psuam",new Version(1,0));},"invalid digest rejected");
        Reject(delegate{UpdateService.ParseRelease(Release("v1.5.0",false,false,null).Replace("https://github.com/test/psuam/releases/download/","https://evil.test/test/psuam/releases/download/"),"test/psuam",new Version(1,0));},"asset outside GitHub rejected");
        Reject(delegate{UpdateService.ParseRelease("{\"tag_name\":\"v1.5.0\",\"html_url\":\"https://github.com/test/psuam/releases/tag/v1.5.0\",\"assets\":[]}","test/psuam",new Version(1,0));},"missing Windows asset rejected");
        foreach(var code in new[]{HttpStatusCode.Forbidden,HttpStatusCode.NotFound})
        {
            var handler=new FixtureHandler{Respond=delegate{return new HttpResponseMessage(code);}};
            using(var client=new UpdateService(handler))Reject(delegate{client.CheckAsync("test/psuam",new Version(1,0),CancellationToken.None).GetAwaiter().GetResult();},"HTTP "+(int)code+" readable failure");Eq(handler.Calls,1,"HTTP "+(int)code+" is not retried");
        }
        var cancellation=new CancellationTokenSource();cancellation.Cancel();var cancelledHandler=new FixtureHandler{Respond=delegate{return new HttpResponseMessage(HttpStatusCode.OK);}};
        using(var client=new UpdateService(cancelledHandler))try{client.CheckAsync("test/psuam",new Version(1,0),cancellation.Token).GetAwaiter().GetResult();throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Eq(cancelledHandler.Calls,0,"cancelled check never contacts server");}cancellation.Dispose();
        Eq(UpdateService.ParseChecksum(hash+"  PSUAM-windows-v1.5.0.zip\n","PSUAM-windows-v1.5.0.zip"),hash,"companion checksum filename verified");
        Reject(delegate{UpdateService.ParseChecksum(hash+"  wrong.zip","PSUAM-windows-v1.5.0.zip");},"wrong checksum filename rejected");
        Reject(delegate{UpdateService.ParseChecksum(hash+"\n"+hash,"x.zip");},"ambiguous checksum rejected");
        string good=Zip("good","中文目录/走动提醒.exe","中文目录/走动提醒.exe.config",false);
        string staged=UpdateService.StageZip(good,UpdateService.HashFile(good),new Version(1,5,0,0),Path.Combine(root,"staging"),CancellationToken.None);
        Eq(Directory.GetFiles(staged).Length,2,"only executable/config staged");Eq(File.Exists(Path.Combine(staged,"走动提醒.exe")),true,"Chinese root folder accepted");
        var unverified=UpdateService.ParseRelease(Release("v1.5.0",false,false,null),"test/psuam",new Version(1,0));
        var noDownload=new FixtureHandler{Respond=delegate{throw new Exception("Unverified ZIP must never be requested");}};
        using(var client=new UpdateService(noDownload))Reject(delegate{client.DownloadAndStageAsync(unverified,Path.Combine(root,"unverified"),null,CancellationToken.None).GetAwaiter().GetResult();},"unverified ZIP never downloaded");Eq(noDownload.Calls,0,"missing checksum performs no download");
        var verified=UpdateService.ParseRelease(Release("v1.5.0",false,false,"sha256:"+UpdateService.HashFile(good)).Replace("\"size\":123,","\"size\":"+new FileInfo(good).Length+","),"test/psuam",new Version(1,0));
        var bytes=File.ReadAllBytes(good);var downloadFixture=new FixtureHandler{Respond=delegate{return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};}};
        using(var client=new UpdateService(downloadFixture)){string result=client.DownloadAndStageAsync(verified,Path.Combine(root,"fixture-download"),null,CancellationToken.None).GetAwaiter().GetResult();Eq(Directory.GetFiles(result).Length,2,"verified network fixture stages exact pair");}
        string companionJson=Release("v1.5.0",false,false,null).Replace("\"size\":123,","\"size\":"+new FileInfo(good).Length+",");
        companionJson=companionJson.Substring(0,companionJson.Length-2)+",{\"name\":\"PSUAM-windows-v1.5.0.zip.sha256\",\"browser_download_url\":\"https://github.com/test/psuam/releases/download/v1.5.0/PSUAM-windows-v1.5.0.zip.sha256\"}]}";
        var companion=UpdateService.ParseRelease(companionJson,"test/psuam",new Version(1,0));
        var companionFixture=new FixtureHandler{Respond=delegate(HttpRequestMessage request){return new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri.AbsolutePath.EndsWith(".sha256")?(HttpContent)new StringContent(UpdateService.HashFile(good)+"  PSUAM-windows-v1.5.0.zip"):new ByteArrayContent(bytes)};}};
        using(var client=new UpdateService(companionFixture)){string result=client.DownloadAndStageAsync(companion,Path.Combine(root,"companion-fixture"),null,CancellationToken.None).GetAwaiter().GetResult();Eq(Directory.GetFiles(result).Length,2,"companion SHA-256 verifies complete download");Eq(companionFixture.Calls,2,"companion checksum and ZIP fetched once each");}
        var evilRedirect=new FixtureHandler{Respond=delegate{var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("https://evil.test/asset.zip");return response;}};
        using(var client=new UpdateService(evilRedirect))Reject(delegate{client.DownloadAndStageAsync(verified,Path.Combine(root,"redirect-fixture"),null,CancellationToken.None).GetAwaiter().GetResult();},"download redirect outside GitHub hosts rejected");
        Reject(delegate{UpdateService.StageZip(good,new string('0',64),new Version(1,5,0,0),Path.Combine(root,"bad-hash"),CancellationToken.None);},"tampered download rejected");
        Reject(delegate{UpdateService.StageZip(good,UpdateService.HashFile(good),new Version(9,0,0,0),Path.Combine(root,"wrong-version"),CancellationToken.None);},"executable version mismatch rejected");
        foreach(string entry in new[]{"../走动提醒.exe","/走动提醒.exe","C:/走动提醒.exe","a\\..\\走动提醒.exe","a:走动提醒.exe"})
        {
            string badZip=Zip("bad-"+Guid.NewGuid().ToString("N"),entry,"走动提醒.exe.config",false);
            Reject(delegate{UpdateService.StageZip(badZip,UpdateService.HashFile(badZip),new Version(1,5,0,0),Path.Combine(root,"bad-stage"),CancellationToken.None);},"unsafe ZIP path: "+entry);
        }
        string dup=Zip("duplicate","走动提醒.exe","走动提醒.exe.config",true);
        Reject(delegate{UpdateService.StageZip(dup,UpdateService.HashFile(dup),new Version(1,5,0,0),Path.Combine(root,"duplicate-stage"),CancellationToken.None);},"case insensitive duplicate rejected");
        string separate=Zip("separate","a/走动提醒.exe","b/走动提醒.exe.config",false);
        Reject(delegate{UpdateService.StageZip(separate,UpdateService.HashFile(separate),new Version(1,5,0,0),Path.Combine(root,"separate-stage"),CancellationToken.None);},"mismatched pair rejected");
        string link=Zip("symlink","走动提醒.exe","走动提醒.exe.config",false);
        using(var archive=ZipFile.Open(link,ZipArchiveMode.Update))archive.GetEntry("走动提醒.exe").ExternalAttributes=unchecked((int)0xA1FF0000);
        Reject(delegate{UpdateService.StageZip(link,UpdateService.HashFile(link),new Version(1,5,0,0),Path.Combine(root,"link-stage"),CancellationToken.None);},"ZIP symlink rejected");
        string oversized=Zip("oversized","走动提醒.exe","走动提醒.exe.config",false);
        using(var archive=ZipFile.Open(oversized,ZipArchiveMode.Update))using(var output=archive.CreateEntry("padding.bin",CompressionLevel.Optimal).Open()){byte[] zeros=new byte[65536];for(int i=0;i<1025;i++)output.Write(zeros,0,zeros.Length);}
        Reject(delegate{UpdateService.StageZip(oversized,UpdateService.HashFile(oversized),new Version(1,5,0,0),Path.Combine(root,"oversized-stage"),CancellationToken.None);},"oversized ZIP entry rejected before extraction");
        string missing=Zip("missing","走动提醒.exe",null,false);
        Reject(delegate{UpdateService.StageZip(missing,UpdateService.HashFile(missing),new Version(1,5,0,0),Path.Combine(root,"missing-stage"),CancellationToken.None);},"missing config rejected");
        string badExe=Zip("native-invalid","走动提醒.exe","走动提醒.exe.config",false);
        using(var archive=ZipFile.Open(badExe,ZipArchiveMode.Update)){archive.GetEntry("走动提醒.exe").Delete();using(var output=new StreamWriter(archive.CreateEntry("走动提醒.exe").Open()))output.Write("not a Windows executable");}
        Reject(delegate{UpdateService.StageZip(badExe,UpdateService.HashFile(badExe),new Version(1,5,0,0),Path.Combine(root,"invalid-exe-stage"),CancellationToken.None);},"invalid PE executable rejected");
        string target=Path.Combine(root,"installed");Directory.CreateDirectory(target);string targetExe=Path.Combine(target,"走动提醒.exe");string targetConfig=targetExe+".config";
        File.Copy(Assembly.GetExecutingAssembly().Location,targetExe);File.WriteAllText(targetConfig,"old config");File.WriteAllText(Path.Combine(target,"user-history.jsonl"),"private history");
        string exeHash=UpdateService.HashFile(Path.Combine(staged,"走动提醒.exe")),configHash=UpdateService.HashFile(Path.Combine(staged,"走动提醒.exe.config"));
        Reject(delegate{UpdateInstaller.ReplacePair(staged,targetExe,exeHash,configHash,delegate{throw new IOException("simulated locked config");});},"replacement failure triggers rollback");
        Eq(File.ReadAllText(targetConfig),"old config","old config restored after failure");Eq(UpdateService.HashFile(targetExe),UpdateService.HashFile(Assembly.GetExecutingAssembly().Location),"old executable restored after failure");
        string recoveryDir=Path.Combine(root,"failed-rollback");Directory.CreateDirectory(recoveryDir);string recoveryExe=Path.Combine(recoveryDir,"走动提醒.exe"),recoveryConfig=recoveryExe+".config";
        File.Copy(Assembly.GetExecutingAssembly().Location,recoveryExe);File.AppendAllText(recoveryExe,"old executable marker");File.WriteAllText(recoveryConfig,"original config");
        string originalExeHash=UpdateService.HashFile(recoveryExe),originalConfigHash=UpdateService.HashFile(recoveryConfig);
        int restartCalls=0;var recoveryLogs=new List<string>();FileStream lockedExe=null;
        try
        {
            int failed=UpdateInstaller.ApplyAndRestart(staged,recoveryExe,exeHash,configHash,originalExeHash,originalConfigHash,"1.5.0.0",delegate{lockedExe=new FileStream(recoveryExe,FileMode.Open,FileAccess.Read,FileShare.Read);throw new IOException("simulated config replacement failure with EXE locked against restore");},delegate{restartCalls++;},delegate(string message){recoveryLogs.Add(message);});
            Eq(failed,1,"rollback failure returns failure");
            Eq(UpdateService.HashFile(recoveryExe),exeHash,"failed rollback leaves new executable in place");Eq(UpdateService.HashFile(recoveryConfig),originalConfigHash,"failed rollback leaves original configuration in place");
            Eq(restartCalls,0,"mixed installation is never restarted after rollback failure");
            Eq(recoveryLogs.Exists(delegate(string message){return message.Contains("自动恢复也未完成");}),true,"rollback failure logged with backup location");
            Eq(recoveryLogs.Exists(delegate(string message){return message.Contains("未重新启动程序");}),true,"unverified mixed pair logs restart suppression");
            Eq(Directory.GetDirectories(recoveryDir,".psuam-backup-*").Length,1,"backup retained after failed rollback");
            string saved=Directory.GetDirectories(recoveryDir,".psuam-backup-*")[0];Eq(UpdateService.HashFile(Path.Combine(saved,"走动提醒.exe")),originalExeHash,"failed rollback keeps original EXE backup");Eq(UpdateService.HashFile(Path.Combine(saved,"走动提醒.exe.config")),originalConfigHash,"failed rollback keeps original config backup");
        }
        finally{if(lockedExe!=null)lockedExe.Dispose();}
        string restoredDir=Path.Combine(root,"verified-rollback");Directory.CreateDirectory(restoredDir);string restoredExe=Path.Combine(restoredDir,"走动提醒.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location,restoredExe);File.AppendAllText(restoredExe,"original executable");File.WriteAllText(restoredExe+".config","original config");
        string restoredExeHash=UpdateService.HashFile(restoredExe),restoredConfigHash=UpdateService.HashFile(restoredExe+".config");restartCalls=0;
        int rolledBack=UpdateInstaller.ApplyAndRestart(staged,restoredExe,exeHash,configHash,restoredExeHash,restoredConfigHash,"1.5.0.0",delegate{throw new IOException("simulated second-file replacement failure");},delegate(string path){UpdateService.RequireHash(path,restoredExeHash);UpdateService.RequireHash(path+".config",restoredConfigHash);restartCalls++;},delegate{});
        Eq(rolledBack,1,"verified rollback still reports failed update");Eq(restartCalls,1,"verified complete rollback restarts original pair once");
        restartCalls=0;
        int installed=UpdateInstaller.ApplyAndRestart(staged,restoredExe,exeHash,configHash,restoredExeHash,restoredConfigHash,"1.5.0.0",null,delegate(string path){UpdateService.RequireHash(path,exeHash);UpdateService.RequireHash(path+".config",configHash);restartCalls++;},delegate{});
        Eq(installed,0,"verified successful replacement reports success");Eq(restartCalls,1,"verified successful replacement restarts new pair once");
        UpdateInstaller.ReplacePair(staged,targetExe,exeHash,configHash,null);
        Eq(UpdateService.HashFile(targetConfig),configHash,"successful replacement config verified");Eq(File.ReadAllText(Path.Combine(target,"user-history.jsonl")),"private history","user history preserved");
        File.AppendAllText(Path.Combine(staged,"走动提醒.exe.config"),"tamper");
        Reject(delegate{UpdateInstaller.ReplacePair(staged,targetExe,exeHash,configHash,null);},"staged tampering rejected before replacement");
        Eq(UpdateService.HashFile(targetConfig),configHash,"installed config unchanged after rejected tampering");
        Eq(UpdateInstaller.QuoteArgument("C:\\space path\\file.json"),"\"C:\\space path\\file.json\"","helper path safely quoted");
        Eq(UpdateInstaller.QuoteArgument("x\"y\\"),"\"x\\\"y\\\\\"","helper quote and trailing slash escaped");
        Eq(UpdateInstaller.Run(Path.Combine(root,"untrusted-plan.json")),1,"helper refuses plan outside updates folder");
    }
}
