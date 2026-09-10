using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using ChachaCapture;
namespace ChachaCapture
{
internal static class UpdateTests
{
    static int count;
    static string binary;
    static string scratch;
    internal static int Run(string testDirectory)
    {
        count = 0; binary = Assembly.GetExecutingAssembly().Location; scratch = Path.Combine(testDirectory, "updater-regression"); Directory.CreateDirectory(scratch);
        try
        {
            Check("semantic versions and zero components", delegate {
                Need(UpdateService.CompareVersionTag("v1.10.0", new Version(1, 2)) > 0);
                Need(UpdateService.CompareVersionTag("V1.2", new Version(1, 2, 0, 0)) == 0);
                Need(UpdateService.CompareVersionTag("1.2.0.1", new Version(1, 2, 0, 0)) > 0);
                Reject(delegate { UpdateService.CompareVersionTag("v1.2-beta", new Version(1, 0)); });
                Reject(delegate { UpdateService.CompareVersionTag("../../2.0", new Version(1, 0)); });
            });
            Check("stable release and private asset metadata", delegate {
                UpdateInfo info = UpdateService.ParseRelease(Json(Release()), new Version(1, 0));
                Need(info.IsUpdateAvailable && info.Version == "1.1.0" && info.AssetSize == 1024);
                Need(info.AssetApiUrl.EndsWith("/123") && info.ChecksumApiUrl.EndsWith("/456"));
                Need(!UpdateService.ParseRelease(Json(Release()), new Version(1, 2)).IsUpdateAvailable);
            });
            Check("release prerelease/draft rejection", delegate {
                Dictionary<string, object> release = Release(); release["draft"] = true;
                Reject(delegate { UpdateService.ParseRelease(Json(release), new Version(1,0)); });
                release = Release(); release["prerelease"] = true;
                Reject(delegate { UpdateService.ParseRelease(Json(release), new Version(1,0)); });
            });
            Check("release repository and asset size bounds", delegate {
                Dictionary<string, object> release = Release(); release["html_url"] = "https://github.com/other/chacha/releases/tag/v1.1.0";
                Reject(delegate { UpdateService.ParseRelease(Json(release), new Version(1,0)); });
                release = Release(); Asset(release, 0)["size"] = 67108865L;
                Reject(delegate { UpdateService.ParseRelease(Json(release), new Version(1,0)); });
                release = Release(); Asset(release, 1)["size"] = 0;
                Reject(delegate { UpdateService.ParseRelease(Json(release), new Version(1,0)); });
            });
            Check("release duplicate and missing assets", delegate {
                Dictionary<string, object> release = Release(); release["assets"] = new object[] { Asset(release,0), Asset(release,0), Asset(release,1) };
                Reject(delegate { UpdateService.ParseRelease(Json(release), new Version(1,0)); });
                release = Release(); release["assets"] = new object[] { Asset(release,0) };
                Reject(delegate { UpdateService.ParseRelease(Json(release), new Version(1,0)); });
            });
            Check("asset URL exact HTTPS repository and filename", delegate {
                string good = "https://github.com/youkdonghun/chacha/releases/download/v1.1.0/ChachaCapture.exe";
                Need(UpdateService.ValidateAssetUri(good,"v1.1.0","ChachaCapture.exe").Host == "github.com");
                foreach (string bad in new string[] { good.Replace("https:","http:"), good + "?token=x", good.Replace("github.com/", "github.com.evil.test/"), good.Replace("youkdonghun/", "other/"), good.Replace("/v1.1.0/", "/v2.0/"), good.Replace("ChachaCapture.exe", "evil.exe") })
                    Reject(delegate { UpdateService.ValidateAssetUri(bad,"v1.1.0","ChachaCapture.exe"); });
            });
            Check("API assets exact numeric repository identity", delegate {
                string good = "https://api.github.com/repos/youkdonghun/chacha/releases/assets/123";
                Need(UpdateService.ValidateAssetApiUri(good).Host == "api.github.com");
                foreach (string bad in new string[] { good + "?x=1", good + "/../../tags/123", good.Replace("/123", "/0"), good.Replace("youkdonghun", "other"), good.Replace("api.github.com", "evil.test"), good.Replace("/123", "/abc") })
                    Reject(delegate { UpdateService.ValidateAssetApiUri(bad); });
            });
            Check("checksum CRLF BOM binary mode and case", delegate {
                string hash = new string('A',64);
                Need(UpdateService.ParseChecksum("\uFEFF" + hash + " *ChachaCapture.exe\r\n", "ChachaCapture.exe") == hash.ToLowerInvariant());
                Need(UpdateService.ParseChecksum(new string('0',64) + "  other.exe\r\n" + hash + "  ChachaCapture.exe\n", "ChachaCapture.exe") == hash.ToLowerInvariant());
            });
            Check("checksum filename exact duplicate malformed", delegate {
                string row = new string('a',64) + "  ChachaCapture.exe\n";
                Reject(delegate { UpdateService.ParseChecksum(row + row,"ChachaCapture.exe"); });
                Reject(delegate { UpdateService.ParseChecksum(row.Replace("ChachaCapture.exe", "../ChachaCapture.exe"),"ChachaCapture.exe"); });
                Reject(delegate { UpdateService.ParseChecksum(new string('g',64) + " ChachaCapture.exe","ChachaCapture.exe"); });
            });
            Check("real AMD64 executable accepted", delegate { UpdateService.ValidateAmd64Executable(binary); });
            Check("x86 PE rejected", delegate {
                byte[] data = File.ReadAllBytes(binary); int offset=BitConverter.ToInt32(data,0x3c); data[offset+4]=0x4c; data[offset+5]=1;
                RejectPe(data);
            });
            Check("truncated PE and invalid section rejected", delegate {
                RejectPe(new byte[100]); byte[] data = File.ReadAllBytes(binary); int offset=BitConverter.ToInt32(data,0x3c);
                int optional=BitConverter.ToUInt16(data,offset+20); int section=offset+24+optional;
                Array.Copy(BitConverter.GetBytes(UInt32.MaxValue),0,data,section+20,4); RejectPe(data);
            });
            Check("non-executable DLL rejected", delegate {
                byte[] data=File.ReadAllBytes(binary); int offset=BitConverter.ToInt32(data,0x3c); data[offset+23] |= 0x20; RejectPe(data);
            });
            Check("argument quoting exact empty slash quote", delegate {
                Need(UpdateService.QuoteArgument("") == "\"\"");
                Need(UpdateService.QuoteArgument("a\"b") == "\"a\\\"b\"");
                Need(UpdateService.QuoteArgument("C:\\space x\\") == "\"C:\\space x\\\\\"");
            });
            Check("cancelled check never performs network", delegate {
                CancellationTokenSource source=new CancellationTokenSource(); source.Cancel();
                try { UpdateService.CheckAsync(source.Token).GetAwaiter().GetResult(); throw new Exception("not cancelled"); }
                catch(OperationCanceledException) { }
            });
            Check("helper switch rejects missing manifest without target change", delegate {
                int exit; Need(!UpdateService.TryHandleUpdate(new string[0],out exit) && exit==0);
                Need(UpdateService.TryHandleUpdate(new string[]{"--apply-update"},out exit) && exit==1);
                Need(UpdateService.TryHandleUpdate(new string[]{"--apply-update",binary},out exit) && exit==1);
            });
            Check("validated stage discard and helper preservation", delegate {
                string stage=Path.Combine(Path.GetTempPath(),"ChachaCapture-Update-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
                string exe=Path.Combine(stage,"ChachaCapture.exe"); File.WriteAllText(exe,"fixture");
                StagedUpdate update=new StagedUpdate{Directory=stage,ExecutablePath=exe};
                File.WriteAllText(Path.Combine(stage,"update.json"),"{}"); UpdateService.Discard(update); Need(File.Exists(exe));
                File.Delete(Path.Combine(stage,"update.json")); UpdateService.Discard(update); Need(!Directory.Exists(stage));
            });
            Check("discard does not delete outside own stage", delegate {
                string exe=Path.Combine(scratch,"keep.txt"); File.WriteAllText(exe,"keep");
                Reject(delegate {UpdateService.Discard(new StagedUpdate{Directory=scratch,ExecutablePath=exe});}); Need(File.ReadAllText(exe)=="keep");
            });
            Check("bounded stage error read and foreign path rejection", delegate {
                string message; Need(!UpdateService.TryReadUpdateError(new string[]{"--update-error",Path.Combine(scratch,"keep.txt")},out message));
                string stage=Path.Combine(Path.GetTempPath(),"ChachaCapture-Update-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
                string error=Path.Combine(stage,"install-error.txt"); File.WriteAllText(error,"rollback completed");
                Need(UpdateService.TryReadUpdateError(new string[]{"--update-error",error},out message) && message=="rollback completed");
                File.WriteAllText(error,new string('x',65537)); Need(!UpdateService.TryReadUpdateError(new string[]{"--update-error",error},out message));
                File.Delete(error); Directory.Delete(stage,false);
            });
            return count;
        }
        catch(Exception error) { throw new InvalidOperationException("Updater regression failed", error); }
    }
    static void Check(string name,Action action) { try { action(); count++; } catch(Exception error) { throw new InvalidOperationException(name, error); } }
    static void Need(bool value) { if(!value)throw new Exception("assertion failed"); }
    static void Reject(Action action) { try { action(); } catch(InvalidDataException) {return;} catch(ArgumentException) {return;} throw new Exception("invalid input accepted"); }
    static void RejectPe(byte[] data) {string path=Path.Combine(scratch,"bad.exe");File.WriteAllBytes(path,data);Reject(delegate{UpdateService.ValidateAmd64Executable(path);});}
    static string Json(object value) { return new JavaScriptSerializer().Serialize(value); }
    static Dictionary<string,object> Asset(Dictionary<string,object> release,int index) {return (Dictionary<string,object>)((object[])release["assets"])[index];}
    static Dictionary<string,object> Release()
    {
        return new Dictionary<string,object> { {"tag_name","v1.1.0"},{"html_url","https://github.com/youkdonghun/chacha/releases/tag/v1.1.0"},{"draft",false},{"prerelease",false},{"name","fixture"},{"body","notes"},
            {"assets",new object[] {
                new Dictionary<string,object>{{"name","ChachaCapture.exe"},{"size",1024},{"browser_download_url","https://github.com/youkdonghun/chacha/releases/download/v1.1.0/ChachaCapture.exe"},{"url","https://api.github.com/repos/youkdonghun/chacha/releases/assets/123"}},
                new Dictionary<string,object>{{"name","SHA256SUMS.txt"},{"size",100},{"browser_download_url","https://github.com/youkdonghun/chacha/releases/download/v1.1.0/SHA256SUMS.txt"},{"url","https://api.github.com/repos/youkdonghun/chacha/releases/assets/456"}}
            }} };
    }
}

}
