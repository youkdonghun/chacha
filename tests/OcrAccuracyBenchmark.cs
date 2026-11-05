using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Windows.Foundation;
using Tesseract;
using ChachaCapture;
class OcrAccuracyBench {
    static T Await<T>(IAsyncOperation<T> operation) {
        try { while(operation.Status == AsyncStatus.Started) System.Threading.Thread.Sleep(2); return operation.GetResults(); }
        finally { operation.Close(); }
    }
    static string Compact(string text) { return Regex.Replace(text ?? "", @"\s", ""); }
    static int Distance(string a,string b) {
        a=Compact(a); b=Compact(b); int[] last=Enumerable.Range(0,b.Length+1).ToArray();
        for(int i=1;i<=a.Length;i++) { int[] next=new int[b.Length+1]; next[0]=i;
            for(int j=1;j<=b.Length;j++) next[j]=Math.Min(Math.Min(last[j]+1,next[j-1]+1),last[j-1]+(a[i-1]==b[j-1]?0:1)); last=next; }
        return last[b.Length];
    }
    static string WindowsRead(Bitmap bitmap) {
        using(var bytes=new MemoryStream()) {
            bitmap.Save(bytes,System.Drawing.Imaging.ImageFormat.Png); bytes.Position=0;
            using(var stream=new InMemoryRandomAccessStream()) {
                using(var writer=new DataWriter(stream.GetOutputStreamAt(0))) {
                    writer.WriteBytes(bytes.ToArray()); Await<uint>(writer.StoreAsync());
                }
                stream.Seek(0);
                var decoder=Await(BitmapDecoder.CreateAsync(stream));
                using(var pixels=Await(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore))) {
                    var engine=OcrEngine.TryCreateFromLanguage(new Language("ko"));
                    var result=Await(engine.RecognizeAsync(pixels));
                    return String.Join("\n",result.Lines.Select(line=>line.Text));
                }
            }
        }
    }
    static int Main(string[] args) {
        var reports=new List<object>();
        string[] lines={"금액 80,508원 / 할인 8.0% / 수량 800", "Code: S8S5 0O01 O0O0 B8B8 I1l1", "8050 8088 8000 10:08 8.50%", "주문 S8050O / 2026-11-05 / test08@example.com", "질문? 확인! 할인 25% 이메일 test@example.com 시간 10:30"};
        bool heldout=args.Length>4;
        if(heldout) lines=new[]{"배송비 8,050원 / 할인 0.08% / 수량 18", "Reference: B050 S08O O800 B1S8", "808.05 50,080 0.80% 18:08 -80.5", "주문 OS80-S5 / item08@example.org", "SOFT 8050 STOP 0808 질문? 확인! @ :"};
        string expected=String.Join("\n",lines);
        bool best=args.Length>3 && args[3]=="best", refine=args.Length>3 && args[3]=="refine";
        using(var mixed=new TesseractEngine(args[0],best?"eng+kor":"kor+eng",EngineMode.LstmOnly))
        using(var english=new TesseractEngine(args[0],"eng",EngineMode.LstmOnly)) {
            foreach(string family in heldout?new[]{"Arial","Tahoma"}:new[]{"Malgun Gothic","Segoe UI","Consolas"}) foreach(int size in heldout?new[]{16,22}:new[]{14,18,24}) foreach(bool dark in new[]{false,true}) {
                using(var image=new Bitmap(1200,size*11+40)) {
                    using(var g=Graphics.FromImage(image)) using(var font=new Font(family,size,FontStyle.Regular,GraphicsUnit.Pixel)) {
                        g.Clear(dark?Color.FromArgb(25,28,34):Color.White); g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                        for(int i=0;i<lines.Length;i++) g.DrawString(lines[i],font,dark?Brushes.White:Brushes.Black,20,16+i*size*2);
                    }
                    if(family=="Malgun Gothic" && size==18 && !dark) image.Save(args[2]);
                    foreach(bool prepared in new[]{false,true}) using(var input=prepared?OcrPreprocessing.Prepare(image,true):(Bitmap)image.Clone()) {
                        Stopwatch watch=Stopwatch.StartNew();
                        if(!best && !refine) { string windows=WindowsRead(input); reports.Add(new{family,size,dark,prepared,engine="windows-ko",expected,text=windows,errors=Distance(expected,windows),milliseconds=watch.ElapsedMilliseconds}); }
                        using(var png=new MemoryStream()) {
                            input.Save(png,System.Drawing.Imaging.ImageFormat.Png);
                            using(var pix=Pix.LoadFromMemory(png.ToArray())) {
                              if(refine) {
                                watch.Restart(); string text=OcrRecognition.Read(pix,args[0],PageSegMode.SingleBlock,"mixed");
                                reports.Add(new{family,size,dark,prepared,engine="refined",expected,text,errors=Distance(expected,text),milliseconds=watch.ElapsedMilliseconds});
                              }
                              foreach(var engine in new[]{mixed,english}) {
                                watch.Restart(); using(var page=engine.Process(pix,PageSegMode.SingleBlock)) {
                                    string text=page.GetText().Trim(); reports.Add(new{family,size,dark,prepared,engine=engine==mixed?"tess-mixed":"tess-english",expected,text,errors=Distance(expected,text),milliseconds=watch.ElapsedMilliseconds});
                                }
                              }
                            }
                        }
                    }
                }
            }
        }
        File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(reports)); return 0;
    }
}
