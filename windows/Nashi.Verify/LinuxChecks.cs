using Nashi.Core;
internal static class LinuxChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        if(OperatingSystem.IsWindows())return;
        check(SystemTools.Distribution("NAME=\"CachyOS Linux\"\nID=cachyos\nID_LIKE=arch\n")==SystemTools.Family.Arch,"Arch derivatives must use pacman");
        check(SystemTools.Distribution("ID=pop\nID_LIKE=\"ubuntu debian\"\n")==SystemTools.Family.Debian,"Ubuntu derivatives must use apt");
        check(SystemTools.Distribution("ID=fedora\n")==SystemTools.Family.Fedora&&SystemTools.Distribution("ID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"\n")==SystemTools.Family.Suse,"Fedora and openSUSE must be detected");
        check(SystemTools.InstallCommand(["ffmpeg","ffprobe","deno","exiftool"],SystemTools.Family.Arch)=="sudo pacman -S --needed ffmpeg deno perl-image-exiftool","pacman command lists each package once");
        check(SystemTools.InstallCommand(["deno","exiftool","office"],SystemTools.Family.Debian)=="sudo apt install nodejs libimage-exiftool-perl libreoffice","apt has no Deno package and uses Debian package names");
        check(SystemTools.InstallCommand([],SystemTools.Family.Fedora)=="","Nothing missing means no command");

        var directory=Path.Combine(root,"Linux checks");Directory.CreateDirectory(directory);
        var previousConfig=Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            var config=Path.Combine(directory,"config");Directory.CreateDirectory(config);
            var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            File.WriteAllText(Path.Combine(config,"user-dirs.dirs"),"# test\nXDG_DESKTOP_DIR=\"$HOME/Masaüstü\"\nXDG_DOWNLOAD_DIR=\"$HOME/İndirilenler\"\n");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME",config);
            check(SystemTools.Downloads()==Path.Combine(home,"İndirilenler"),"Localized XDG download folder was ignored");
            check(new Preferences().DownloadFolder==Path.Combine(home,"İndirilenler"),"New preferences must download to the XDG folder");
            File.WriteAllText(Path.Combine(config,"user-dirs.dirs"),"XDG_DOWNLOAD_DIR=\"$HOME/\"\n");
            check(SystemTools.Downloads()==Path.Combine(home,"Downloads"),"A disabled XDG download folder must not resolve to home");

            var tools=Path.Combine(directory,"tools");Directory.CreateDirectory(tools);
            void Tool(string name,bool executable)
            {
                var path=Path.Combine(tools,name);File.WriteAllText(path,"#!/bin/sh\nexit 0\n");
                File.SetUnixFileMode(path,executable?UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute:UnixFileMode.UserRead|UnixFileMode.UserWrite);
            }
            Tool("ffmpeg",true);Tool("ffprobe",false);Tool("deno",true);
            var chain=new Toolchain(tools);
            check(chain.Executable("ffmpeg")==Path.Combine(tools,"ffmpeg"),"A tool in Nashi's folder must win over the system package");
            check(chain.FFmpegLocation==tools,"yt-dlp must merge with the same FFmpeg");
            check(chain.JavaScriptRuntime=="deno:"+Path.Combine(tools,"deno"),"Deno must be passed to yt-dlp as deno");
            check(!SystemTools.IsExecutable(Path.Combine(tools,"ffprobe")),"A non-executable file must not count as a tool");
            check(SystemTools.IsExecutable("/bin/sh")&&!SystemTools.IsExecutable(directory),"Executable detection must follow Unix permissions");
            var install=chain.Ready?null:Record(()=>chain.InstallAsync().GetAwaiter().GetResult());
            check(chain.Ready||install is PlatformNotSupportedException,"Linux setup must point to the package manager instead of Windows archives");

            var library=new LibraryStore(Path.Combine(directory,"library"));
            var upper=Path.Combine(directory,"Photo.png");var lower=Path.Combine(directory,"photo.png");File.WriteAllText(upper,"a");File.WriteAllText(lower,"b");
            library.Record("Convert",[upper,lower]);
            check(library.Entries[0].Paths.Length==2,"Case-sensitive Linux paths are different files");
            var metadata=new MetadataService(Path.Combine(directory,"metadata"));
            check(metadata.Executable is null||Path.IsPathFullyQualified(metadata.Executable),"System ExifTool must be an absolute path");
            PagesWithNode(check,directory);
        }
        finally{Environment.SetEnvironmentVariable("XDG_CONFIG_HOME",previousConfig);Directory.Delete(directory,true);}
    }
    private static Exception? Record(Action action){try{action();return null;}catch(Exception error){return error;}}
    /// DOCX → Pages → HTML through the audited Pages bundle on Node.js (the
    /// Linux fallback when Deno is not packaged), under Node's permission model.
    private static void PagesWithNode(Action<bool,string> check,string directory)
    {
        var node=SystemTools.Find("node")??SystemTools.Find("nodejs");var repo=AppContext.BaseDirectory;
        while(repo is not null&&!File.Exists(Path.Combine(repo,"Resources","PagesInterop.js")))repo=Path.GetDirectoryName(repo);
        if(node is null||repo is null){Console.WriteLine("Skipping Node Pages check: Node.js or the source tree is unavailable.");return;}
        var assets=Path.Combine(directory,"assets");Directory.CreateDirectory(assets);
        File.Copy(Path.Combine(repo,"Resources","PagesInterop.js"),Path.Combine(assets,"PagesInterop.js"));File.Copy(Path.Combine(repo,"scripts","pages-node.mjs"),Path.Combine(assets,"pages-node.mjs"));
        var docx=Path.Combine(directory,"Note.docx");
        using(var zip=System.IO.Compression.ZipFile.Open(docx,System.IO.Compression.ZipArchiveMode.Create))
        {
            void Entry(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(xml);}
            Entry("[Content_Types].xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Entry("_rels/.rels","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Entry("word/document.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Merhaba Linux şğüİ</w:t></w:r></w:p></w:body></w:document>");
        }
        var office=new OfficeDocuments("",node,assets);var pages=Path.Combine(directory,"Note.pages");var work=Path.Combine(directory,"pages-work");Directory.CreateDirectory(work);
        office.PagesWriteAsync(docx,pages,CancellationToken.None).GetAwaiter().GetResult();
        check(File.Exists(pages)&&new FileInfo(pages).Length>0,"Node.js did not write a Pages document");
        var html=File.ReadAllText(office.PagesReadAsync(pages,work,CancellationToken.None).GetAwaiter().GetResult());
        check(System.Net.WebUtility.HtmlDecode(html).Contains("Merhaba Linux şğüİ",StringComparison.Ordinal),"Pages text did not survive the Node.js round trip");
        var outside=Path.Combine(directory,"outside.txt");File.WriteAllText(outside,"secret");
        var denied=Record(()=>ProcessRunner.RunAsync(node,["--permission","--allow-fs-read="+Path.Combine(assets,"pages-node.mjs"),"--allow-fs-read="+Path.Combine(assets,"PagesInterop.js"),"--allow-fs-read="+docx,"--allow-fs-write="+Path.Combine(directory,"x.pages"),Path.Combine(assets,"pages-node.mjs"),Path.Combine(assets,"PagesInterop.js"),"read",outside,Path.Combine(directory,"x.json")]).GetAwaiter().GetResult());
        check(denied is IOException,"Node's permission model must block files Nashi did not pass");
    }
}
