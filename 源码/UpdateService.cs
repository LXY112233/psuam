using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
namespace QingMove
{
    public sealed class UpdateRelease
    {
        public Version Version {get;internal set;}
        public string TagName {get;internal set;}
        public string Notes {get;internal set;}
        public string PageUrl {get;internal set;}
        public string AssetUrl {get;internal set;}
        public string AssetName {get;internal set;}
        public long AssetSize {get;internal set;}
        public string Sha256 {get;internal set;}
        public bool IsNewer {get;internal set;}
        internal string Repository,ChecksumUrl;
    }
    // No authentication or cookies are sent. GitHub's HTTPS release metadata and
    // SHA-256 detect transfer corruption; they do not replace publisher signing.
    public sealed class UpdateService : IDisposable
    {
        internal const long MaxDownload=64L*1024*1024,MaxExpanded=128L*1024*1024,MaxEntry=64L*1024*1024;
        internal const string ExeName="走动提醒.exe",ConfigName="走动提醒.exe.config";
        readonly HttpClient client;
        public UpdateService():this(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.None}){}
        internal UpdateService(HttpMessageHandler handler)
        {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            client=new HttpClient(handler);
            client.Timeout=Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PSUAM-Windows-Updater/1.5");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }
        public void Dispose(){client.Dispose();}
        public static string NormalizeRepository(string input)
        {
            string value=(input??"").Trim();
            if(value.StartsWith("https://",StringComparison.OrdinalIgnoreCase))
            {
                Uri uri;
                if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!string.Equals(uri.Host,"github.com",StringComparison.OrdinalIgnoreCase)||!uri.IsDefaultPort||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!="")throw new ArgumentException("请填写 GitHub 仓库地址，例如 https://github.com/用户名/psuam。");
                value=uri.AbsolutePath.Trim('/');
            }
            if(!Regex.IsMatch(value,@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9_.-]{1,100}$")||value.EndsWith("/.",StringComparison.Ordinal)||value.EndsWith("/..",StringComparison.Ordinal))throw new ArgumentException("仓库格式应为 用户名/仓库名，或完整的 GitHub HTTPS 地址。");
            return value;
        }
        public async Task<UpdateRelease> CheckAsync(string repository,Version current,CancellationToken token)
        {
            string repo=NormalizeRepository(repository);
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                try
                {
                    using(var response=await GetAsync("https://api.github.com/repos/"+repo+"/releases/latest",false,timeout.Token).ConfigureAwait(false))
                    {return ParseRelease(Encoding.UTF8.GetString(await ReadBoundedAsync(response,2*1024*1024,timeout.Token).ConfigureAwait(false)),repo,current);}
                }
                catch(OperationCanceledException){if(token.IsCancellationRequested)throw;throw new InvalidOperationException("检查更新超时，请稍后重试。");}
                catch(HttpRequestException ex){throw new InvalidOperationException("无法连接 GitHub，请检查网络后重试。",ex);}
                catch(Exception){if(timeout.IsCancellationRequested){token.ThrowIfCancellationRequested();throw new InvalidOperationException("检查更新超时，请稍后重试。");}throw;}
            }
        }
        static IDictionary<string,object> Obj(object value){var result=value as IDictionary<string,object>;if(result==null)throw new InvalidOperationException("GitHub 返回了无法识别的版本信息。");return result;}
        static string Str(IDictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&value!=null?Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture):"";}
        static bool Flag(IDictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&value is bool&&(bool)value;}
        internal static UpdateRelease ParseRelease(string json,string repository,Version current)
        {
            if(json==null||json.Length>2*1024*1024)throw new InvalidOperationException("版本信息过大。");
            IDictionary<string,object> data;
            try{data=Obj(new JavaScriptSerializer{MaxJsonLength=2*1024*1024,RecursionLimit=30}.DeserializeObject(json));}
            catch(ArgumentException ex){throw new InvalidOperationException("无法读取 GitHub 版本信息。",ex);}
            if(Flag(data,"draft")||Flag(data,"prerelease"))throw new InvalidOperationException("此版本为草稿或预发布版，暂不支持安装。");
            string tag=Str(data,"tag_name");Version version;
            if(!Regex.IsMatch(tag,@"^[vV]?\d+\.\d+\.\d+(?:\.\d+)?$")||!Version.TryParse(tag.TrimStart('v','V'),out version))throw new InvalidOperationException("发布标签不是受支持的版本号（例如 v1.5.0）。");
            version=NormalizeVersion(version);
            string repo=NormalizeRepository(repository),page=Str(data,"html_url");
            ValidateGitHubPath(page,"/"+repo+"/releases/tag/"+tag);
            object raw;var assets=data.TryGetValue("assets",out raw)?raw as IEnumerable:null;
            string wanted="PSUAM-windows-v"+tag.TrimStart('v','V')+".zip";
            IDictionary<string,object> selected=null,checksum=null;
            if(assets!=null)foreach(object item in assets)
            {
                var asset=Obj(item);string name=Str(asset,"name");
                if(name==wanted){if(selected!=null)throw new InvalidOperationException("发布包名称重复，无法安全更新。");selected=asset;}
                if(name==wanted+".sha256"){if(checksum!=null)throw new InvalidOperationException("校验文件重复。");checksum=asset;}
            }
            if(selected==null)throw new InvalidOperationException("此发布尚未提供 Windows 更新包（"+wanted+"）。");
            string url=Str(selected,"browser_download_url");ValidateGitHubPath(url,"/"+repo+"/releases/download/"+tag+"/"+wanted);
            long size;if(!long.TryParse(Str(selected,"size"),out size)||size<=0||size>MaxDownload)throw new InvalidOperationException("更新包大小无效或超过 64 MB 限制。");
            string digest=Str(selected,"digest"),hash=null;
            if(digest!="")
            {if(!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidOperationException("GitHub 提供的 SHA-256 校验值无效。");hash=digest.Substring(7).ToLowerInvariant();}
            string checksumUrl=null;
            if(checksum!=null){checksumUrl=Str(checksum,"browser_download_url");ValidateGitHubPath(checksumUrl,"/"+repo+"/releases/download/"+tag+"/"+wanted+".sha256");}
            return new UpdateRelease{Version=version,TagName=tag,Notes=Str(data,"body"),PageUrl=page,AssetName=wanted,AssetUrl=url,AssetSize=size,Sha256=hash,IsNewer=version>NormalizeVersion(current),Repository=repo,ChecksumUrl=checksumUrl};
        }
        static Version NormalizeVersion(Version value){if(value==null)throw new ArgumentNullException("value");return new Version(value.Major,value.Minor,Math.Max(0,value.Build),Math.Max(0,value.Revision));}
        static void ValidateGitHubPath(string value,string path)
        {
            Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!string.Equals(uri.Host,"github.com",StringComparison.OrdinalIgnoreCase)||!uri.IsDefaultPort||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||!SameGitHubPath(Uri.UnescapeDataString(uri.AbsolutePath),path))throw new InvalidOperationException("更新地址与指定的 GitHub 仓库不一致。");
        }
        static bool SameGitHubPath(string actual,string expected)
        {
            string[] a=actual.Split('/'),b=expected.Split('/');if(a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(!string.Equals(a[i],b[i],i==1||i==2?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return false;
            return true;
        }
        static bool IsAllowedRedirect(Uri uri)
        {return uri!=null&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo==""&&uri.Fragment==""&&(uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com"||uri.Host=="github-releases.githubusercontent.com");}
        async Task<HttpResponseMessage> GetAsync(string url,bool asset,CancellationToken token)
        {
            Uri initial=new Uri(url),current=initial;
            for(int redirects=0;redirects<=5;redirects++)
            {
                HttpResponseMessage response=null;
                for(int attempt=0;attempt<2;attempt++)
                {
                    try{response=await client.GetAsync(current,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);}
                    catch(HttpRequestException){if(attempt==1)throw;}
                    if(response==null){await Task.Delay(600,token).ConfigureAwait(false);continue;}
                    if((int)response.StatusCode>=500&&attempt==0){response.Dispose();response=null;await Task.Delay(600,token).ConfigureAwait(false);continue;}break;
                }
                int code=(int)response.StatusCode;
                if(code>=300&&code<=399)
                {
                    Uri location=response.Headers.Location;response.Dispose();
                    if(location!=null&&!location.IsAbsoluteUri)location=new Uri(current,location);
                    if(!asset||!IsAllowedRedirect(location)||redirects==5)throw new InvalidOperationException("GitHub 返回了不受支持的下载跳转。");current=location;continue;
                }
                if(response.IsSuccessStatusCode)return response;
                response.Dispose();
                if(code==403||code==429)throw new InvalidOperationException("GitHub 暂时限制了请求（403/429）。请稍后重试；也可能是当前网络无法访问 GitHub。");
                if(code==404)throw new InvalidOperationException("未找到公开仓库、正式发布或更新文件（404）。请检查仓库地址和 Releases。");
                throw new InvalidOperationException("GitHub 请求失败（HTTP "+code+"），请稍后重试。");
            }
            throw new InvalidOperationException("下载跳转次数过多。");
        }
        static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response,int limit,CancellationToken token)
        {
            if(response.Content.Headers.ContentLength>limit)throw new InvalidOperationException("GitHub 返回的数据超过大小限制。");
            // Framework HTTP streams may not interrupt a pending ReadAsync solely
            // from its token; disposing the response also aborts the socket read.
            using(var abort=token.Register(response.Dispose))
            using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var buffer=new MemoryStream())
            {byte[] bytes=new byte[16384];int read;while((read=await stream.ReadAsync(bytes,0,bytes.Length,token).ConfigureAwait(false))>0){if(buffer.Length+read>limit)throw new InvalidOperationException("GitHub 返回的数据超过大小限制。");buffer.Write(bytes,0,read);}return buffer.ToArray();}
        }
        internal static string ParseChecksum(string text,string assetName)
        {
            if(text==null||text.Length>4096)throw new InvalidOperationException("校验文件无效。");
            string[] lines=text.Trim().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            if(lines.Length!=1)throw new InvalidOperationException("校验文件必须仅包含一个 SHA-256 值。");
            var match=Regex.Match(lines[0].Trim(),@"^([a-fA-F0-9]{64})(?:\s+\*?(.+))?$");
            if(!match.Success||(match.Groups[2].Success&&match.Groups[2].Value!=assetName))throw new InvalidOperationException("校验文件的 SHA-256 或更新包名称不正确。");
            return match.Groups[1].Value.ToLowerInvariant();
        }
        public async Task<string> DownloadAndStageAsync(UpdateRelease release,string stageRoot,IProgress<int> progress,CancellationToken token)
        {
            if(release==null)throw new ArgumentNullException("release");
            ValidateGitHubPath(release.AssetUrl,"/"+NormalizeRepository(release.Repository)+"/releases/download/"+release.TagName+"/"+release.AssetName);
            if(release.AssetSize<=0||release.AssetSize>MaxDownload)throw new InvalidOperationException("更新包大小不受支持。");
            string work=Path.Combine(Path.GetFullPath(stageRoot),"download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);string zip=Path.Combine(work,"release.zip");
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                try
                {
                    string hash=release.Sha256;
                    if(string.IsNullOrEmpty(hash))
                    {
                        if(string.IsNullOrEmpty(release.ChecksumUrl))throw new InvalidOperationException("此发布未提供 SHA-256 校验，无法安全安装。请发布 .sha256 文件后重试。");
                        ValidateGitHubPath(release.ChecksumUrl,"/"+release.Repository+"/releases/download/"+release.TagName+"/"+release.AssetName+".sha256");
                        using(var response=await GetAsync(release.ChecksumUrl,true,timeout.Token).ConfigureAwait(false))hash=ParseChecksum(Encoding.UTF8.GetString(await ReadBoundedAsync(response,4096,timeout.Token).ConfigureAwait(false)),release.AssetName);
                    }
                    using(var response=await GetAsync(release.AssetUrl,true,timeout.Token).ConfigureAwait(false))
                    using(var abort=timeout.Token.Register(response.Dispose))
                    {
                        if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=release.AssetSize)throw new InvalidOperationException("更新包大小与发布信息不一致。");
                        using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                        {byte[] bytes=new byte[65536];long total=0;int read;while((read=await input.ReadAsync(bytes,0,bytes.Length,timeout.Token).ConfigureAwait(false))>0){total+=read;if(total>MaxDownload||total>release.AssetSize)throw new InvalidOperationException("下载的更新包超过大小限制。");await output.WriteAsync(bytes,0,read,timeout.Token).ConfigureAwait(false);if(progress!=null)progress.Report((int)(90*total/release.AssetSize));}if(total!=release.AssetSize)throw new InvalidOperationException("更新包下载不完整，请重试。");}
                    }
                    string result=await Task.Run(delegate{return StageZip(zip,hash,release.Version,stageRoot,timeout.Token);},timeout.Token).ConfigureAwait(false);
                    if(progress!=null)progress.Report(100);return result;
                }
                catch(OperationCanceledException){if(token.IsCancellationRequested)throw;throw new InvalidOperationException("下载更新超时，请检查网络后重试。");}
                catch(HttpRequestException ex){throw new InvalidOperationException("下载更新失败，请检查网络后重试。",ex);}
                catch(Exception){if(timeout.IsCancellationRequested){token.ThrowIfCancellationRequested();throw new InvalidOperationException("下载更新超时，请检查网络后重试。");}throw;}
                finally{try{Directory.Delete(work,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
            }
        }
        internal static string HashFile(string path)
        {using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        internal static void RequireHash(string path,string expected)
        {if(expected==null||!Regex.IsMatch(expected,@"^[a-fA-F0-9]{64}$")||!string.Equals(HashFile(path),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("文件 SHA-256 校验失败，请重新下载更新。");}
        internal static string StageZip(string zip,string hash,Version expectedVersion,string stageRoot,CancellationToken token)
        {
            if(new FileInfo(zip).Length>MaxDownload)throw new InvalidDataException("更新包超过 64 MB 限制。");RequireHash(zip,hash);token.ThrowIfCancellationRequested();
            string stage=Path.Combine(Path.GetFullPath(stageRoot),"stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try
            {
                using(var archive=ZipFile.OpenRead(zip))
                {
                    if(archive.Entries.Count>2048)throw new InvalidDataException("更新包包含过多文件。");
                    var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);ZipArchiveEntry exe=null,config=null;string pairDir=null;long expanded=0;
                    foreach(var entry in archive.Entries)
                    {
                        token.ThrowIfCancellationRequested();string name=entry.FullName.Replace('\\','/');
                        if(name.Length==0||name.Length>240||name.StartsWith("/",StringComparison.Ordinal)||name.IndexOf(':')>=0||name.IndexOf('\0')>=0)throw new InvalidDataException("更新包包含不安全的文件路径。");
                        string[] parts=name.TrimEnd('/').Split('/');foreach(string part in parts)if(part==""||part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal)||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("更新包包含不安全的文件路径。");
                        if(!seen.Add(name.TrimEnd('/')))throw new InvalidDataException("更新包包含重复的文件路径。");
                        int unixType=(entry.ExternalAttributes>>16)&0xF000;
                        if(unixType==0xA000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("更新包不能包含符号链接或重解析点。");
                        expanded=checked(expanded+entry.Length);if(entry.Length>MaxEntry||expanded>MaxExpanded||entry.Length<0)throw new InvalidDataException("更新包解压大小超过安全限制。");
                        if(name.EndsWith("/",StringComparison.Ordinal))continue;
                        string leaf=parts[parts.Length-1];
                        if(string.Equals(leaf,ExeName,StringComparison.OrdinalIgnoreCase)||string.Equals(leaf,ConfigName,StringComparison.OrdinalIgnoreCase))
                        {
                            string dir=name.Substring(0,name.Length-leaf.Length);
                            if(pairDir!=null&&!string.Equals(pairDir,dir,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("程序和配置必须位于同一目录。");pairDir=dir;
                            if(string.Equals(leaf,ExeName,StringComparison.OrdinalIgnoreCase)){if(exe!=null)throw new InvalidDataException("更新包包含多个主程序。");exe=entry;}
                            else{if(config!=null)throw new InvalidDataException("更新包包含多个程序配置。");config=entry;}
                        }
                    }
                    if(exe==null||config==null)throw new InvalidDataException("更新包缺少走动提醒.exe 或对应的 .config 文件。");
                    ExtractEntry(exe,Path.Combine(stage,ExeName),token);ExtractEntry(config,Path.Combine(stage,ConfigName),token);
                }
                ValidateExecutable(Path.Combine(stage,ExeName),expectedVersion);
                string configuration=Path.Combine(stage,ConfigName);if(new FileInfo(configuration).Length>1024*1024)throw new InvalidDataException("程序配置文件过大。");
                using(var reader=XmlReader.Create(configuration,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024})){var document=new XmlDocument{XmlResolver=null};document.Load(reader);if(document.DocumentElement==null||document.DocumentElement.Name!="configuration")throw new InvalidDataException("更新包中的程序配置无效。");}
                return stage;
            }
            catch{try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}throw;}
        }
        static void ExtractEntry(ZipArchiveEntry entry,string destination,CancellationToken token)
        {
            using(var input=entry.Open())using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {byte[] bytes=new byte[65536];long total=0;int read;while((read=input.Read(bytes,0,bytes.Length))>0){token.ThrowIfCancellationRequested();total+=read;if(total>entry.Length||total>MaxEntry)throw new InvalidDataException("更新包解压大小异常。");output.Write(bytes,0,read);}if(total!=entry.Length)throw new InvalidDataException("更新包文件不完整。");}
        }
        internal static void ValidateExecutable(string path,Version expected)
        {
            try
            {
                using(var stream=File.OpenRead(path))using(var reader=new BinaryReader(stream))
                {
                    if(stream.Length<256||reader.ReadUInt16()!=0x5A4D)throw new InvalidDataException("更新程序不是有效的 Windows 可执行文件。");
                    stream.Position=0x3C;int offset=reader.ReadInt32();if(offset<64||offset>stream.Length-256)throw new InvalidDataException("更新程序的 PE 头无效。");
                    stream.Position=offset;if(reader.ReadUInt32()!=0x4550)throw new InvalidDataException("更新程序的 PE 标记无效。");
                    ushort machine=reader.ReadUInt16();stream.Position=offset+22;ushort characteristics=reader.ReadUInt16();
                    if((machine!=0x14C&&machine!=0x8664)||(characteristics&2)==0||(characteristics&0x2000)!=0)throw new InvalidDataException("更新程序架构或可执行类型无效。");
                    stream.Position=offset+24;ushort magic=reader.ReadUInt16();int cliOffset=magic==0x10B?208:magic==0x20B?224:0;
                    if(cliOffset==0||offset+24+cliOffset+8>stream.Length)throw new InvalidDataException("更新程序格式无效。");
                    stream.Position=offset+24+cliOffset;if(reader.ReadUInt32()==0||reader.ReadUInt32()<72)throw new InvalidDataException("更新程序不是受支持的 .NET 程序。");
                }
                if(NormalizeVersion(AssemblyName.GetAssemblyName(path).Version)!=NormalizeVersion(expected))throw new InvalidDataException("更新程序版本与发布标签不一致。");
            }
            catch(BadImageFormatException ex){throw new InvalidDataException("更新程序不是有效的 .NET 程序。",ex);}
        }
    }
}
