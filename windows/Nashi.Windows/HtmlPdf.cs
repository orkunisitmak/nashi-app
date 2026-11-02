using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Nashi.Windows;
/// <summary>
/// Prints a local HTML file to PDF with the Microsoft Edge WebView2 Runtime that Windows
/// includes, so the PDF keeps the layout a browser shows. It follows the Mac WebKit route:
/// page scripts run (e-invoices draw their QR codes with them) without network or device
/// access, Nashi serves the page and regular files from its own folder itself, and every
/// other load is refused. Where the runtime is missing, the office route is used instead.
/// </summary>
internal static partial class HtmlPdf
{
    internal const string Host="nashi-html.invalid";
    internal const string Origin="https://"+Host;
    internal const int MaximumPages=1000;
    internal const long MaximumFile=50_000_000;
    private static readonly HashSet<string> Devices=new(StringComparer.OrdinalIgnoreCase){"CON","PRN","AUX","NUL","CONIN$","CONOUT$","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"};
    internal static readonly TimeSpan Limit=TimeSpan.FromSeconds(120);
    internal const string TooManyPages="This HTML file would need more than 1,000 PDF pages.";
    internal const string Unfinished="The HTML file did not finish rendering. No PDF was exported.";
    internal const string PrintFailed="Microsoft Edge WebView2 could not print this HTML file to PDF.";
    internal const string Stopped="Microsoft Edge WebView2 stopped while rendering the HTML file. No PDF was exported.";
    internal const string OpenFailed="The HTML file could not be opened.";
    // Scripts run, but may not read files beside the page or connect anywhere: fetch, XHR,
    // WebSocket, EventSource and beacons fall under connect-src, and workers are refused.
    internal const string Policy="default-src 'self' data: blob: 'unsafe-inline' 'unsafe-eval'; connect-src 'none'; worker-src 'none'; manifest-src 'none'; object-src 'none'; form-action 'none'; base-uri 'self'";
    // WebRTC reaches the network outside the request filter, device APIs could prompt or record,
    // and window.print would open Edge's print preview over Nashi.
    internal const string WithoutDevices="(()=>{for(const name of ['RTCPeerConnection','webkitRTCPeerConnection','RTCDataChannel','RTCIceCandidate','RTCSessionDescription','speechSynthesis'])delete window[name];for(const name of ['mediaDevices','geolocation','getUserMedia','webkitGetUserMedia','serviceWorker','bluetooth','usb','serial','hid'])delete Navigator.prototype[name];window.print=()=>{};})()";
    // Lazy images and web fonts must be ready. Scripts may swap in an image, such as a QR code,
    // just after loading, so images are checked again after a short pause.
    // A load the page aborts (for example by trying to navigate away) ends with "abort", and no single
    // image or font may hold the PDF for more than 15 seconds.
    private const string Ready="(()=>{const k=Symbol.for('nashi.ready');if(window[k])return 1;window[k]={};const cap=p=>Promise.race([p,new Promise(d=>setTimeout(d,15000))]);const settled=()=>cap(Promise.all([...document.images].map(i=>i.complete||new Promise(d=>{for(const e of ['load','error','abort'])i.addEventListener(e,d,{once:true})}))));(async()=>{for(const i of document.images)i.loading='eager';await settled();await cap(document.fonts.ready);await new Promise(d=>setTimeout(d,100));await settled();return [document.documentElement.scrollHeight,document.title];})().then(v=>window[k].v=v,()=>window[k].v=[0,'']);return 1;})()";
    private const string Result="(()=>{const r=window[Symbol.for('nashi.ready')];return r?(r.v||null):'restart';})()";
    // Every name resolves nowhere and every connection goes to a proxy that cannot exist, so a
    // preconnect hint or an IP address in the page cannot open a socket either.
    private const string Arguments="--proxy-server=http://0.0.0.0:9 --proxy-bypass-list=<-loopback> --host-resolver-rules=\"MAP * ~NOTFOUND\" --force-webrtc-ip-handling-policy=disable_non_proxied_udp --disable-background-networking --disable-component-update --disable-sync --no-pings";

    /// <summary>The installed WebView2 Runtime version, or null when Windows has none.</summary>
    internal static string? RuntimeVersion
    {
        get
        {
            if(Environment.GetEnvironmentVariable("NASHI_HTML_PDF_OFFICE")=="1")return null;
            try{return CoreWebView2Environment.GetAvailableBrowserVersionString();}
            catch(Exception error)when(error is WebView2RuntimeNotFoundException or DllNotFoundException or BadImageFormatException or System.Runtime.InteropServices.COMException){return null;}
        }
    }
    internal static bool Available=>RuntimeVersion is not null;

    /// <summary>Prints <paramref name="input"/> into <paramref name="output"/>; nothing is written unless it succeeds.</summary>
    internal static Task RenderAsync(string input,string output,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var dispatcher=Application.Current?.Dispatcher??throw new InvalidOperationException(PrintFailed);
        // WebView2 lives on the WPF thread; its calls are asynchronous, so the UI stays responsive.
        return dispatcher.InvokeAsync(()=>new Job(input,output).RunAsync(token)).Task.Unwrap();
    }

    /// <summary>A regular file inside the page's folder, reached without links; null for anything else.</summary>
    internal static string? Local(string folder,Uri url)
    {
        if(!url.IsAbsoluteUri||url.Scheme!=Uri.UriSchemeHttps||!string.Equals(url.Host,Host,StringComparison.OrdinalIgnoreCase)||!url.IsDefaultPort)return null;
        string relative;try{relative=Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/'));}catch(UriFormatException){return null;}
        if(relative.Length==0)return null;
        var parts=relative.Split('/');
        // Alternate data streams, device names, ".." and backslashes never reach the disk.
        if(parts.Any(p=>p.Length==0||p is "." or ".."||p.IndexOfAny([':','\\','<','>','"','|','?','*','\0'])>=0||p.EndsWith('.')||p.EndsWith(' ')||Devices.Contains(p.Split('.')[0].TrimEnd())))return null;
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var file=Path.GetFullPath(Path.Combine([root,..parts]));
        if(!file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return null;
        // Each step from the folder to the file must be a real directory or file, not a junction or link.
        var current=root;
        foreach(var part in parts)
        {
            current=Path.Combine(current,part);
            FileAttributes attributes;try{attributes=File.GetAttributes(current);}catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return null;}
            if((attributes&FileAttributes.ReparsePoint)!=0)return null;
        }
        var info=new FileInfo(file);
        return info.Exists&&(info.Attributes&FileAttributes.Directory)==0&&info.Length<=MaximumFile?info.FullName:null;
    }

    /// <summary>Chromium reads an undeclared page as Windows-1252, but such files are nearly always UTF-8.</summary>
    internal static string? Charset(byte[] html)
    {
        var head=Encoding.UTF8.GetString(html,0,Math.Min(html.Length,1024));
        if(Declared().IsMatch(head))return null;
        try{_=new UTF8Encoding(false,true).GetString(html);return "utf-8";}catch(DecoderFallbackException){return null;}
    }
    [GeneratedRegex("<meta[^>]*charset",RegexOptions.IgnoreCase)]private static partial Regex Declared();

    internal static string ContentType(string file)=>Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" or ".htm"=>"text/html",".xhtml" or ".xht"=>"application/xhtml+xml",".css"=>"text/css",".js" or ".mjs"=>"text/javascript",".json"=>"application/json",".txt"=>"text/plain",".xml"=>"application/xml",
        ".svg"=>"image/svg+xml",".png"=>"image/png",".jpg" or ".jpeg"=>"image/jpeg",".gif"=>"image/gif",".webp"=>"image/webp",".avif"=>"image/avif",".bmp"=>"image/bmp",".ico"=>"image/x-icon",
        ".woff"=>"font/woff",".woff2"=>"font/woff2",".ttf"=>"font/ttf",".otf"=>"font/otf",".mp4"=>"video/mp4",".webm"=>"video/webm",".mp3"=>"audio/mpeg",".wav"=>"audio/wav",
        _=>"application/octet-stream"
    };

    /// <summary>The user's region decides the paper: Letter where it is used, A4 elsewhere; half-inch margins as browsers print.</summary>
    internal static (double Width,double Height) Paper(System.Globalization.RegionInfo region)=>region.TwoLetterISORegionName is "US" or "CA" or "MX" or "PH" or "CL" or "CO" or "VE" or "GT" or "PR"?(8.5,11):(8.27,11.69);
    internal const double Margin=.5;

    /// <summary>Removes links back to the page's own files; they cannot open from a shared PDF. Returns the page count.</summary>
    internal static int Finish(string draft,string output,string title)
    {
        int count;var changed=false;
        using(var pdf=PdfReader.Open(draft,PdfDocumentOpenMode.Modify))
        {
            count=pdf.PageCount;
            if(count is 0)throw new InvalidDataException(PrintFailed);
            if(count>MaximumPages)throw new InvalidDataException(TooManyPages);
            foreach(var page in pdf.Pages)
            {
                if(page.Elements.GetArray("/Annots") is null)continue;
                foreach(var annotation in page.Annotations.Cast<PdfSharp.Pdf.Annotations.PdfAnnotation>().ToArray())
                {
                    var uri=annotation.Elements.GetDictionary("/A")?.Elements.GetString("/URI")??"";
                    if(uri.StartsWith(Origin,StringComparison.OrdinalIgnoreCase)){page.Annotations.Remove(annotation);changed=true;}
                }
            }
            // The page title, or for an untitled page its file name, like the Mac print job.
            var current=pdf.Info.Title;
            if(current!=title){pdf.Info.Title=title;changed=true;}
            if(changed)pdf.Save(output);
        }
        if(!changed)File.Move(draft,output);
        return count;
    }

    private sealed class Job(string input,string output)
    {
        private readonly string folder=Path.GetDirectoryName(Path.GetFullPath(input))!;
        private readonly string page=Path.GetFullPath(input);
        private readonly Uri address=new(Origin+"/"+Uri.EscapeDataString(Path.GetFileName(input)));
        private readonly TaskCompletionSource<bool> loaded=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> failed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CoreWebView2Environment? environment;
        private ulong? navigation;

        public async Task RunAsync(CancellationToken outer)
        {
            using var limit=CancellationTokenSource.CreateLinkedTokenSource(outer);limit.CancelAfter(Limit);var token=limit.Token;
            var work=Path.Combine(Path.GetTempPath(),"Nashi-HTML-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
            var draft=Path.Combine(work,"Draft.pdf");
            using var host=new HwndSource(new HwndSourceParameters("Nashi HTML printer"){WindowStyle=unchecked((int)0x80000000),Width=1,Height=1,PositionX=-32000,PositionY=-32000});
            CoreWebView2Controller? controller=null;
            try
            {
                // Profile data stays in Nashi's own folder and the profile is InPrivate, so nothing persists.
                var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi","WebView2");
                environment=await CoreWebView2Environment.CreateAsync(null,data,new CoreWebView2EnvironmentOptions(Arguments){AreBrowserExtensionsEnabled=false}).WaitAsync(token);
                var options=environment.CreateCoreWebView2ControllerOptions();options.IsInPrivateModeEnabled=true;options.ProfileName="HtmlPdf";
                controller=await environment.CreateCoreWebView2ControllerAsync(host.Handle,options).WaitAsync(token);
                var (paperWidth,paperHeight)=Paper(System.Globalization.RegionInfo.CurrentRegion);
                controller.Bounds=new System.Drawing.Rectangle(0,0,(int)((paperWidth-2*Margin)*96),(int)((paperHeight-2*Margin)*96));controller.IsVisible=true;
                var view=controller.CoreWebView2;Configure(view);
                await view.AddScriptToExecuteOnDocumentCreatedAsync(WithoutDevices).WaitAsync(token);
                using(token.Register(()=>loaded.TrySetCanceled()))
                {
                    view.Navigate(address.AbsoluteUri);
                    if(await Task.WhenAny(loaded.Task,failed.Task)==failed.Task)await failed.Task;
                    await loaded.Task;
                }
                // A script result is polled rather than awaited: a promise held across a page's attempt to
                // navigate away can be dropped, and the page then never reports ready.
                await view.ExecuteScriptAsync(Ready).WaitAsync(token);
                JsonElement value;
                while(true)
                {
                    await Task.Delay(50,token);if(failed.Task.IsCompleted)await failed.Task;
                    var reply=JsonDocument.Parse(await view.ExecuteScriptAsync(Result).WaitAsync(token)).RootElement;
                    if(reply.ValueKind==JsonValueKind.String){await view.ExecuteScriptAsync(Ready).WaitAsync(token);continue;}
                    if(reply.ValueKind==JsonValueKind.Array){value=reply.Clone();break;}
                }
                var height=value.GetArrayLength()>0&&value[0].TryGetDouble(out var h)?h:0;var pageTitle=value.GetArrayLength()>1&&value[1].ValueKind==JsonValueKind.String?value[1].GetString():null;
                // CSS pixels are 1/96 inch; the estimate stops endless pages before printing them.
                if(height/((paperHeight-2*Margin)*96)>MaximumPages)throw new InvalidDataException(TooManyPages);
                var settings=environment.CreatePrintSettings();
                settings.PageWidth=paperWidth;settings.PageHeight=paperHeight;settings.MarginTop=settings.MarginBottom=settings.MarginLeft=settings.MarginRight=Margin;
                settings.ShouldPrintBackgrounds=true;settings.ShouldPrintHeaderAndFooter=false;settings.ShouldPrintSelectionOnly=false;settings.Orientation=CoreWebView2PrintOrientation.Portrait;settings.ScaleFactor=1;
                token.ThrowIfCancellationRequested();
                // Printing cannot be interrupted; a cancelled or late job finishes and then publishes nothing.
                var printed=await view.PrintToPdfAsync(draft,settings);
                if(failed.Task.IsCompleted)await failed.Task;
                token.ThrowIfCancellationRequested();
                if(!printed||!File.Exists(draft)||new FileInfo(draft).Length==0)throw new InvalidOperationException(PrintFailed);
                var title=string.IsNullOrWhiteSpace(pageTitle)?Path.GetFileNameWithoutExtension(input):pageTitle.Trim();
                var staged=Path.Combine(work,"Final.pdf");Finish(draft,staged,title);token.ThrowIfCancellationRequested();File.Move(staged,output);
            }
            catch(OperationCanceledException)when(!outer.IsCancellationRequested){throw new TimeoutException(Unfinished);}
            finally
            {
                controller?.Close();
                try{Directory.Delete(work,true);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
            }
        }

        private void Configure(CoreWebView2 view)
        {
            var settings=view.Settings;
            settings.IsScriptEnabled=true;settings.AreDefaultScriptDialogsEnabled=false;settings.AreDevToolsEnabled=false;settings.IsWebMessageEnabled=false;settings.AreHostObjectsAllowed=false;
            settings.AreDefaultContextMenusEnabled=false;settings.IsStatusBarEnabled=false;settings.IsZoomControlEnabled=false;settings.IsPasswordAutosaveEnabled=false;settings.IsGeneralAutofillEnabled=false;settings.AreBrowserAcceleratorKeysEnabled=false;
            settings.IsBuiltInErrorPageEnabled=false;settings.IsSwipeNavigationEnabled=false;settings.IsPinchZoomEnabled=false;
            // A dark app appearance must not switch the page to its dark color scheme.
            view.Profile.PreferredColorScheme=CoreWebView2PreferredColorScheme.Light;
            view.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All,CoreWebView2WebResourceRequestSourceKinds.All);
            view.WebResourceRequested+=(_,e)=>e.Response=Respond(e.Request.Uri);
            view.PermissionRequested+=(_,e)=>{e.State=CoreWebView2PermissionState.Deny;e.Handled=true;};
            view.NewWindowRequested+=(_,e)=>e.Handled=true;
            view.DownloadStarting+=(_,e)=>{e.Cancel=true;e.Handled=true;};
            view.ScriptDialogOpening+=(_,_)=>{/* Unanswered dialogs close as cancelled. */};
            view.LaunchingExternalUriScheme+=(_,e)=>e.Cancel=true;
            // Frames may show files beside the page. Nothing navigates the page itself away.
            view.NavigationStarting+=(_,e)=>{if(navigation is not null||!Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)||uri.AbsoluteUri!=address.AbsoluteUri)e.Cancel=true;else navigation=e.NavigationId;};
            view.FrameNavigationStarting+=(_,e)=>{if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)||!(uri.Scheme is "about" or "data" or "blob"||Local(folder,uri) is not null))e.Cancel=true;};
            view.NavigationCompleted+=(_,e)=>
            {
                // A cancelled attempt to leave the page also completes; only the page's own load counts.
                if(e.NavigationId!=navigation)return;
                if(e.IsSuccess)loaded.TrySetResult(true);
                else failed.TrySetException(new InvalidDataException(OpenFailed));
            };
            view.ProcessFailed+=(_,e)=>{if(e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)failed.TrySetException(new InvalidOperationException(Stopped));};
        }

        private CoreWebView2WebResourceResponse Respond(string url)
        {
            var env=environment!;
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri))return env.CreateWebResourceResponse(null,403,"Forbidden","");
            var file=uri.AbsoluteUri==address.AbsoluteUri||string.Equals(uri.GetLeftPart(UriPartial.Path),address.AbsoluteUri,StringComparison.Ordinal)?page:Local(folder,uri);
            if(file is null)return env.CreateWebResourceResponse(null,uri.Host==Host?404:403,uri.Host==Host?"Not Found":"Forbidden","Content-Security-Policy: "+Policy);
            byte[] data;try{data=File.ReadAllBytes(file);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){return env.CreateWebResourceResponse(null,404,"Not Found","");}
            var type=ContentType(file);if(type=="text/html"&&Charset(data) is {} charset)type+="; charset="+charset;
            var headers=$"Content-Type: {type}\r\nContent-Security-Policy: {Policy}\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\nCross-Origin-Resource-Policy: same-origin";
            return env.CreateWebResourceResponse(new MemoryStream(data,false),200,"OK",headers);
        }
    }
}
