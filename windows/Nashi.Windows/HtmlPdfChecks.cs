using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nashi.Core;
namespace Nashi.Windows;
/// <summary>
/// Ports the macOS HTMLToPDF tests to the WebView2 route: layout and pixels, a script-drawn
/// code, local assets and UTF-8 without a charset; a loopback TCP/UDP probe; files outside the
/// page's folder through "..", file URLs, a junction, a link and an alternate data stream;
/// cancellation, page limits, local links and titles. Runs in --smoke-test and alone with
/// --smoke-test-html, which sends no desktop input.
/// </summary>
internal static class HtmlPdfChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"HTML to PDF");Directory.CreateDirectory(folder);
        Paths(folder,check);
        if(!HtmlPdf.Available){check(true,"WebView2 Runtime is not installed; HTML to PDF keeps the office route");return;}
        Layout(folder,check);
        Network(folder,check);
        Outside(folder,check);
        Limits(folder,check);
        Links(folder,check);
        Conversion(folder,check);
    }

    private static void Paths(string folder,Action<bool,string> check)
    {
        var site=Path.Combine(folder,"Paths");Directory.CreateDirectory(Path.Combine(site,"assets"));File.WriteAllText(Path.Combine(site,"assets","style.css"),"body{}");File.WriteAllText(Path.Combine(site,"Page.html"),"<p>x</p>");
        Uri U(string path)=>new(HtmlPdf.Origin+"/"+path);
        check(HtmlPdf.Local(site,U("assets/style.css"))==Path.Combine(site,"assets","style.css"),"A stylesheet beside the page must load");
        check(HtmlPdf.Local(site,U("assets/style.css?v=2#top"))==Path.Combine(site,"assets","style.css"),"A query string must not hide a file beside the page");
        check(HtmlPdf.Local(site,U("assets%2Fstyle.css"))==Path.Combine(site,"assets","style.css"),"An escaped separator must still stay inside the folder");
        foreach(var unsafePath in new[]{"../Paths/Page.html","..%2FPaths%2FPage.html","%2e%2e/Paths/Page.html","assets/../../x","Page.html:secret","Page.html%3Asecret","assets%5Cstyle.css","CON","assets/","assets","C:/Windows/win.ini","%00Page.html","missing.css"})
            check(HtmlPdf.Local(site,U(unsafePath)) is null,"A path outside the page's regular files was served: "+unsafePath);
        check(HtmlPdf.Local(site,new("http://"+HtmlPdf.Host+"/Page.html")) is null&&HtmlPdf.Local(site,new("https://example.com/Page.html")) is null&&HtmlPdf.Local(site,new(HtmlPdf.Origin+":8443/Page.html")) is null,"Only Nashi's own HTTPS origin may serve files");
        var big=Path.Combine(site,"big.bin");using(var stream=File.Create(big))stream.SetLength(HtmlPdf.MaximumFile+1);
        check(HtmlPdf.Local(site,U("big.bin")) is null,"Files over 50 MB must not be served");
        check(HtmlPdf.Charset("<p>Türkçe</p>"u8.ToArray())=="utf-8"&&HtmlPdf.Charset("<meta charset=\"windows-1254\"><p>x</p>"u8.ToArray()) is null&&HtmlPdf.Charset([0x3C,0x70,0x3E,0xFC,0x3C])is null,"Undeclared UTF-8 pages must be read as UTF-8 and nothing else changed");
        check(HtmlPdf.Paper(new("US"))==(8.5,11)&&HtmlPdf.Paper(new("TR"))==(8.27,11.69),"Paper must follow the user's region");
        check(HtmlPdf.ContentType("a.JS")=="text/javascript"&&HtmlPdf.ContentType("a.svg")=="image/svg+xml"&&HtmlPdf.ContentType("a.unknown")=="application/octet-stream","Served files must carry their content type");
    }

    private static void Layout(string folder,Action<bool,string> check)
    {
        var site=Path.Combine(folder,"Layout");Directory.CreateDirectory(Path.Combine(site,"css"));
        File.WriteAllText(Path.Combine(site,"css","site.css"),"html,body{margin:0;background:#0080ff}#block{width:300px;height:200px;background:#ff8000}");
        var image=new WriteableBitmap(40,40,96,96,PixelFormats.Bgra32,null);var pixels=Enumerable.Repeat(unchecked((int)0xFF00C000),1600).ToArray();image.WritePixels(new(0,0,40,40),pixels,160,0);ImageEngine.Save(image,Path.Combine(site,"green.png"),"png");
        // No charset declaration: Turkish letters must survive as UTF-8.
        File.WriteAllText(Path.Combine(site,"Invoice.html"),"<!DOCTYPE html><html><head><link rel=stylesheet href=\"css/site.css\"></head><body><div id=block></div><img src=\"green.png\" width=200 height=200 style=\"display:block\"><canvas id=qr width=200 height=200></canvas><p id=text></p>"+
            "<script>const c=document.getElementById('qr').getContext('2d');c.fillStyle='#000';c.fillRect(0,0,200,200);document.getElementById('text').textContent='Script ran · Türkçe ğüşıöç İ';</script></body></html>",new System.Text.UTF8Encoding(false));
        var output=Path.Combine(site,"Invoice.pdf");
        Wait(HtmlPdf.RenderAsync(Path.Combine(site,"Invoice.html"),output,CancellationToken.None));
        check(File.Exists(output),"HTML to PDF did not write a PDF");
        using(var pdf=UglyToad.PdfPig.PdfDocument.Open(output))
        {
            var text=string.Concat(pdf.GetPages().Select(p=>p.Text));
            check(text.Contains("Script ran")&&text.Contains("Türkçe")&&text.Contains("ğüşıöç"),"Page scripts must run and undeclared UTF-8 must print correctly: "+text);
            check(pdf.Information.Title=="Invoice","An untitled page must be named after its file, not "+pdf.Information.Title);
        }
        var page=ImageEngine.FromPng(Wait(NativePdf.RenderAsync(output,72))[0]);var bgra=new byte[page.PixelWidth*page.PixelHeight*4];new FormatConvertedBitmap(page,PixelFormats.Bgra32,null,0).CopyPixels(bgra,page.PixelWidth*4,0);
        (byte R,byte G,byte B) At(double xInch,double yInch){var x=(int)(xInch*72);var y=(int)(yInch*72);var i=(y*page.PixelWidth+x)*4;return (bgra[i+2],bgra[i+1],bgra[i]);}
        bool Near((byte R,byte G,byte B) c,int r,int g,int b)=>Math.Abs(c.R-r)<40&&Math.Abs(c.G-g)<40&&Math.Abs(c.B-b)<40;
        // Half-inch margins; CSS pixels are 1/96 inch, so the 300x200 block spans 0.5–3.6 by 0.5–2.6 inches.
        check(Near(At(1.5,1.5),255,128,0),"A linked stylesheet did not lay out the page: "+At(1.5,1.5));
        check(Near(At(6,1.5),0,128,255),"Backgrounds must print: "+At(6,1.5));
        check(Near(At(1.5,3.6),0,192,0),"An image beside the page did not print: "+At(1.5,3.6));
        check(Near(At(1.5,5.7),0,0,0),"A script-drawn code is missing: "+At(1.5,5.7));
    }

    private static void Network(string folder,Action<bool,string> check)
    {
        var site=Path.Combine(folder,"Network");Directory.CreateDirectory(site);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));var udpPort=((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        var accepted=0;var received=0;using var stop=new CancellationTokenSource();
        _=Task.Run(async()=>{try{while(true){using var client=await listener.AcceptTcpClientAsync(stop.Token);Interlocked.Increment(ref accepted);}}catch(Exception){}});
        _=Task.Run(async()=>{try{while(true){await udp.ReceiveAsync(stop.Token);Interlocked.Increment(ref received);}}catch(Exception){}});
        var targets=new[]{$"http://127.0.0.1:{port}",$"http://localhost:{port}",$"http://[::ffff:127.0.0.1]:{port}",$"ws://127.0.0.1:{port}"};
        var statics=string.Concat(targets.Take(3).Select(t=>$"<img src=\"{t}/i.png\"><link rel=stylesheet href=\"{t}/s.css\"><link rel=preconnect href=\"{t}\"><link rel=prefetch href=\"{t}/p\"><iframe src=\"{t}/f\"></iframe><script src=\"{t}/s.js\"></script><video src=\"{t}/v.mp4\" autoplay muted></video>"));
        var scripts=$"<script>const t={System.Text.Json.JsonSerializer.Serialize(targets)};const log=m=>document.body.insertAdjacentHTML('beforeend','<p>'+m+'</p>');"+
            "for(const u of t){try{fetch(u+'/fetch').then(()=>log('LEAK fetch')).catch(()=>{})}catch(e){}try{const x=new XMLHttpRequest();x.open('GET',u+'/xhr');x.send()}catch(e){}try{navigator.sendBeacon(u+'/beacon','x')}catch(e){}try{new WebSocket(u.replace('http','ws'))}catch(e){}try{new EventSource(u+'/es')}catch(e){}try{new Image().src=u+'/img'}catch(e){}try{new Worker(u+'/w.js')}catch(e){}}"+
            $"try{{const p=new RTCPeerConnection({{iceServers:[{{urls:'stun:127.0.0.1:{udpPort}'}}]}});p.createDataChannel('x');p.createOffer().then(o=>p.setLocalDescription(o));log('LEAK webrtc')}}catch(e){{log('No WebRTC')}}"+
            "log(typeof navigator.mediaDevices==='undefined'&&typeof navigator.geolocation==='undefined'?'No devices':'LEAK devices');try{window.print()}catch(e){}setTimeout(()=>{try{location.href=t[0]+'/nav'}catch(e){}},10);</script>";
        File.WriteAllText(Path.Combine(site,"Probe.html"),"<!DOCTYPE html><html><head><meta charset=utf-8></head><body><p>Probe page</p>"+statics+scripts+"</body></html>");
        var output=Path.Combine(site,"Probe.pdf");
        try
        {
            Wait(HtmlPdf.RenderAsync(Path.Combine(site,"Probe.html"),output,CancellationToken.None));
            DesktopInput.Pump(1500);
            var text=string.Concat(UglyToad.PdfPig.PdfDocument.Open(output).GetPages().Select(p=>p.Text));
            check(text.Contains("Probe page")&&text.Contains("No WebRTC")&&text.Contains("No devices")&&!text.Contains("LEAK"),"The probe page lost its offline guarantees: "+text);
            check(Volatile.Read(ref accepted)==0,$"The page opened {accepted} loopback TCP connections");
            check(Volatile.Read(ref received)==0,$"The page sent {received} loopback UDP packets");
        }
        finally{stop.Cancel();listener.Stop();}
    }

    private static void Outside(string folder,Action<bool,string> check)
    {
        var site=Path.Combine(folder,"Outside","Site");var secret=Path.Combine(folder,"Outside","Secret");Directory.CreateDirectory(site);Directory.CreateDirectory(secret);
        var png=Path.Combine(secret,"secret.png");var image=new WriteableBitmap(4,4,96,96,PixelFormats.Bgra32,null);image.WritePixels(new(0,0,4,4),Enumerable.Repeat(unchecked((int)0xFFFF0000),16).ToArray(),16,0);ImageEngine.Save(image,png,"png");
        File.WriteAllText(Path.Combine(secret,"secret.txt"),"TOP SECRET");File.WriteAllText(Path.Combine(site,"inside.txt"),"INSIDE TEXT");
        var references=new List<string>{"../Secret/secret.png","..%2FSecret%2Fsecret.png",new Uri(png).AbsoluteUri,"Page.html:hidden.png"};
        // A junction needs no administrator rights; a symbolic link may not be allowed for this account.
        var junction=Path.Combine(site,"junction");
        using(var mklink=Process.Start(new ProcessStartInfo("cmd.exe"){ArgumentList={"/c","mklink","/J",junction,secret},UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true})!)mklink.WaitForExit();
        check(Directory.Exists(junction),"Could not create the junction fixture");references.Add("junction/secret.png");
        try{File.CreateSymbolicLink(Path.Combine(site,"link.png"),png);references.Add("link.png");}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
        var page=Path.Combine(site,"Page.html");
        File.WriteAllText(page,"<!DOCTYPE html><html><head><meta charset=utf-8></head><body><p>Outside probe</p>"+string.Concat(references.Select((r,i)=>$"<img src=\"{r}\" onload=\"document.body.insertAdjacentHTML('beforeend','<p>LOADED {i}</p>')\">"))+
            "<script>fetch('inside.txt').then(r=>r.text()).then(t=>document.body.insertAdjacentHTML('beforeend','<p>FETCHED '+t+'</p>')).catch(()=>{});fetch('junction/secret.txt').then(r=>r.text()).then(t=>document.body.insertAdjacentHTML('beforeend','<p>FETCHED '+t+'</p>')).catch(()=>{});</script></body></html>");
        try{File.WriteAllText(page+":hidden.png","x");}catch(Exception error)when(error is IOException or NotSupportedException){}
        var output=Path.Combine(site,"Page.pdf");
        try
        {
            Wait(HtmlPdf.RenderAsync(page,output,CancellationToken.None));
            var text=string.Concat(UglyToad.PdfPig.PdfDocument.Open(output).GetPages().Select(p=>p.Text));
            check(text.Contains("Outside probe")&&!text.Contains("LOADED")&&!text.Contains("FETCHED")&&!text.Contains("SECRET")&&!text.Contains("INSIDE TEXT"),"A file outside the page's regular files, or a script read, reached the PDF: "+text);
        }
        finally{if(Directory.Exists(junction))Directory.Delete(junction);}
    }

    private static void Limits(string folder,Action<bool,string> check)
    {
        var site=Path.Combine(folder,"Limits");Directory.CreateDirectory(site);
        var tall=Path.Combine(site,"Tall.html");File.WriteAllText(tall,FormattableString.Invariant($"<!DOCTYPE html><div style=\"height:{(HtmlPdf.MaximumPages+2)*11.69}in\">tall</div>"));
        check(Throws<InvalidDataException>(()=>Wait(HtmlPdf.RenderAsync(tall,Path.Combine(site,"Tall.pdf"),CancellationToken.None)),HtmlPdf.TooManyPages)&&!File.Exists(Path.Combine(site,"Tall.pdf")),"A page taller than 1,000 PDF pages must be refused before printing");
        var breaks=Path.Combine(site,"Breaks.html");File.WriteAllText(breaks,"<!DOCTYPE html><style>p{break-after:page;margin:0}</style>"+string.Concat(Enumerable.Repeat("<p>x</p>",HtmlPdf.MaximumPages+1)));
        check(Throws<InvalidDataException>(()=>Wait(HtmlPdf.RenderAsync(breaks,Path.Combine(site,"Breaks.pdf"),CancellationToken.None)),HtmlPdf.TooManyPages)&&!File.Exists(Path.Combine(site,"Breaks.pdf")),"Forced page breaks past 1,000 pages must be refused after printing");
        var simple=Path.Combine(site,"Simple.html");File.WriteAllText(simple,"<p>simple</p>");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();check(Throws<OperationCanceledException>(()=>Wait(HtmlPdf.RenderAsync(simple,Path.Combine(site,"Simple.pdf"),cancelled.Token)))&&!File.Exists(Path.Combine(site,"Simple.pdf")),"A cancelled conversion must publish nothing");}
        var slow=Path.Combine(site,"Slow.html");File.WriteAllText(slow,"<!DOCTYPE html><img src=\"never.png\"><script>document.fonts.ready.then=()=>new Promise(()=>{});</script><p>slow</p>");
        using(var cancelled=new CancellationTokenSource(TimeSpan.FromMilliseconds(400)))
        {
            var started=Stopwatch.StartNew();
            check(Throws<OperationCanceledException>(()=>Wait(HtmlPdf.RenderAsync(slow,Path.Combine(site,"Slow.pdf"),cancelled.Token)))&&!File.Exists(Path.Combine(site,"Slow.pdf"))&&started.Elapsed<TimeSpan.FromSeconds(30),"Cancelling while rendering must stop promptly and publish nothing");
        }
        check(!Directory.EnumerateDirectories(Path.GetTempPath(),"Nashi-HTML-*").Any(d=>Directory.GetCreationTimeUtc(d)>DateTime.UtcNow.AddMinutes(-5)&&Directory.EnumerateFileSystemEntries(d).Any()),"HTML to PDF must remove its draft files");
    }

    private static void Links(string folder,Action<bool,string> check)
    {
        var site=Path.Combine(folder,"Links");Directory.CreateDirectory(site);File.WriteAllText(Path.Combine(site,"Other.html"),"<p>other</p>");
        var page=Path.Combine(site,"Linked.html");File.WriteAllText(page,"<!DOCTYPE html><html><head><title>Kept title</title></head><body><p><a href=\"Other.html\">Local link</a></p><p><a href=\"https://example.com/\">Remote link</a></p></body></html>");
        var output=Path.Combine(site,"Linked.pdf");Wait(HtmlPdf.RenderAsync(page,output,CancellationToken.None));
        using var pdf=PdfSharp.Pdf.IO.PdfReader.Open(output,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var uris=pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>().Where(p=>p.Elements.GetArray("/Annots") is not null).SelectMany(p=>p.Annotations.Cast<PdfSharp.Pdf.Annotations.PdfAnnotation>()).Select(a=>a.Elements.GetDictionary("/A")?.Elements.GetString("/URI")??"").ToArray();
        check(uris.Contains("https://example.com/")&&!uris.Any(u=>u.Contains(HtmlPdf.Host)),"Links to files beside the page must be removed and web links kept: "+string.Join(", ",uris));
        check(pdf.Info.Title=="Kept title","A page title must become the PDF title, not "+pdf.Info.Title);
    }

    private static void Conversion(string folder,Action<bool,string> check)
    {
        // The Convert route itself: no office runtime is installed, the original stays as it was.
        var site=Path.Combine(folder,"Conversion");var tools=Path.Combine(site,"No tools");Directory.CreateDirectory(tools);
        var page=Path.Combine(site,"e-Arşiv Fatura.html");File.WriteAllText(page,"<!DOCTYPE html><title></title><canvas id=c width=120 height=120></canvas><script>c.getContext('2d').fillRect(0,0,120,120)</script><p>Fatura</p>",new System.Text.UTF8Encoding(false));
        var before=File.ReadAllBytes(page);var output=Path.Combine(site,"Out");Directory.CreateDirectory(output);
        var office=new OfficeRuntime(tools);check(office.Executable is null,"The conversion fixture must not find an office runtime");
        var outputs=Wait(new DocumentConversion(office,new Toolchain(tools)).ConvertAsync([page],"pdf",output,new DocumentOptions(),null,CancellationToken.None));
        check(outputs.Count==1&&Path.GetFileName(outputs[0])=="e-Arşiv Fatura.pdf"&&File.Exists(outputs[0])&&before.SequenceEqual(File.ReadAllBytes(page)),"Convert to PDF must print HTML without office tools and keep the original");
        var again=Wait(new DocumentConversion(office,new Toolchain(tools)).ConvertAsync([page],"pdf",output,new DocumentOptions(),null,CancellationToken.None));
        check(again.Count==1&&again[0]!=outputs[0]&&File.Exists(outputs[0]),"A second export must choose a new name instead of replacing the first");
        using var pdf=UglyToad.PdfPig.PdfDocument.Open(outputs[0]);check(pdf.Information.Title=="e-Arşiv Fatura"&&pdf.GetPage(1).Text.Contains("Fatura"),"The converted PDF lost its text or file-name title");
    }

    private static bool Throws<T>(Action action,string? message=null)where T:Exception
    {
        try{action();return false;}
        catch(T error){return message is null||error.Message==message;}
    }
    private static void Wait(Task task)
    {
        var until=DateTime.UtcNow.Add(HtmlPdf.Limit+TimeSpan.FromSeconds(30));
        while(!task.IsCompleted&&DateTime.UtcNow<until)DesktopInput.Pump(30);
        if(!task.IsCompleted)throw new TimeoutException("HTML to PDF did not finish.");
        task.GetAwaiter().GetResult();
    }
    private static T Wait<T>(Task<T> task){Wait((Task)task);return task.Result;}
}
