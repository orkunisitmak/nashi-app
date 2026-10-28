using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Nashi.Core;
namespace Nashi.Windows;

/// Extracts the official MSI as data. Does not install a product, run its custom
/// actions, register COM servers or change Windows Installer/system settings.
public sealed class OfficeRuntime(string root)
{
    // Writer's first start registers the bundled Lightproof grammar-checker extensions, which are
    // Python components. In Nashi's extracted runtime that Python start never returns on some
    // systems (seen on Windows 11, tr-TR), so every Writer conversion hangs. Nashi only runs
    // --convert-to filters, which are native code, so the private runtime runs without the
    // Python loader. A LibreOffice installed under Program Files is never modified.
    internal const string DisabledPythonLoader="pythonloaderlo.dll.disabled-by-nashi";
    internal static bool DisablePythonLoader(string application)
    {
        var loader=Path.Combine(application,"program","pythonloaderlo.dll");
        try{if(File.Exists(loader))File.Move(loader,Path.Combine(application,"program",DisabledPythonLoader),true);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){}
        return !File.Exists(loader);
    }
    public string? Executable{get{var managed=Path.Combine(root,"office-26.8.1");var safe=!Directory.Exists(managed)||DisablePythonLoader(managed);return Candidates(safe).FirstOrDefault(ToolInstallation.UsableFile);}}
    // If the loader cannot be disabled, prefer a LibreOffice under Program Files. The private runtime
    // is then the last resort, and the 5-minute conversion limit still applies.
    private string[] Candidates(bool privateRuntimeSafe)
    {
        string[] managed=[Path.Combine(root,"office-26.8.1","program","soffice.com"),Path.Combine(root,"office-26.8.1","program","soffice.exe")];
        var programFiles=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"LibreOffice","program");
        string[] installed=[Path.Combine(programFiles,"soffice.com"),Path.Combine(programFiles,"soffice.exe")];
        return privateRuntimeSafe?[..managed,..installed]:[..installed,..managed];
    }
    public async Task InstallAsync(IProgress<OperationProgress>? progress,CancellationToken token)
    {
        if(Executable is not null)return;Directory.CreateDirectory(root);var stage=Path.Combine(root,".office-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            var msi=Path.Combine(stage,"office.msi");await VerifiedDownload.FetchAsync(new("https://download.documentfoundation.org/libreoffice/stable/26.8.1/win/x86_64/LibreOffice_26.8.1_Win_x86-64.msi"),msi,600L*1024*1024,p=>progress?.Report(new(p*.75,"Downloading LibreOffice 26.8.1…")),token);
            await using(var source=File.OpenRead(msi))if(Convert.ToHexString(await SHA256.HashDataAsync(source,token)).ToLowerInvariant()!="c6298b10cfa1748bcbedad94c62ce771e240e5021bfd6d7a967cd571b04b5312")throw new InvalidDataException("Office checksum mismatch. Nothing was installed.");
            var content=Path.Combine(stage,"content");Directory.CreateDirectory(content);await Task.Run(()=>MsiImage.Extract(msi,content,progress,token),token);
            var executable=Directory.GetFiles(content,"soffice.exe",SearchOption.AllDirectories).Single(p=>Path.GetFileName(Path.GetDirectoryName(p))=="program");var application=Directory.GetParent(Path.GetDirectoryName(executable)!)!.FullName;
            var runtimes=Path.Combine(content,".native-runtime");if(Directory.Exists(runtimes))foreach(var dll in Directory.EnumerateFiles(runtimes,"*.dll"))File.Copy(dll,Path.Combine(application,"program",Path.GetFileName(dll)),true);
            DisablePythonLoader(application);
            token.ThrowIfCancellationRequested();ToolInstallation.Publish(application,Path.Combine(root,"office-26.8.1"),p=>ToolInstallation.UsableFile(Path.Combine(p,"program","soffice.exe")));progress?.Report(new(1,"Office tools ready"));
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    private static class MsiImage
    {
        [DllImport("msi.dll",CharSet=CharSet.Unicode)]private static extern uint MsiOpenDatabase(string path,IntPtr persistence,out uint database);
        [DllImport("msi.dll",CharSet=CharSet.Unicode)]private static extern uint MsiDatabaseOpenView(uint database,string query,out uint view);
        [DllImport("msi.dll")]private static extern uint MsiViewExecute(uint view,uint record);
        [DllImport("msi.dll")]private static extern uint MsiViewFetch(uint view,out uint record);
        [DllImport("msi.dll",CharSet=CharSet.Unicode)]private static extern uint MsiRecordGetString(uint record,uint field,StringBuilder buffer,ref uint length);
        [DllImport("msi.dll")]private static extern uint MsiRecordReadStream(uint record,uint field,byte[] buffer,ref uint size);
        [DllImport("msi.dll")]private static extern uint MsiCloseHandle(uint handle);
        private static void Check(uint result){if(result!=0)throw new IOException("Office package could not be extracted (Windows Installer data code "+result+").");}
        private static List<string[]> Rows(uint database,string sql,int fields)
        {
            Check(MsiDatabaseOpenView(database,sql,out var view));try{Check(MsiViewExecute(view,0));var result=new List<string[]>();uint code;
                while((code=MsiViewFetch(view,out var record))==0){try{var row=new string[fields];for(uint n=1;n<=fields;n++){var text=new StringBuilder(32768);uint length=32768;Check(MsiRecordGetString(record,n,text,ref length));row[n-1]=text.ToString();}result.Add(row);}finally{MsiCloseHandle(record);}}
                if(code!=259)Check(code);return result;
            }finally{MsiCloseHandle(view);}
        }
        private static string LongName(string name)=>name.Split(':')[0].Split('|')[^1];
        private static int CompareVersion(string first,string second){var a=System.Diagnostics.FileVersionInfo.GetVersionInfo(first);var b=System.Diagnostics.FileVersionInfo.GetVersionInfo(second);return new Version(Math.Max(0,a.FileMajorPart),Math.Max(0,a.FileMinorPart),Math.Max(0,a.FileBuildPart),Math.Max(0,a.FilePrivatePart)).CompareTo(new Version(Math.Max(0,b.FileMajorPart),Math.Max(0,b.FileMinorPart),Math.Max(0,b.FileBuildPart),Math.Max(0,b.FilePrivatePart)));}
        public static void Extract(string path,string destination,IProgress<OperationProgress>? progress,CancellationToken token)
        {
            Check(MsiOpenDatabase(path,IntPtr.Zero,out var db));try
            {
                var cabinets=Rows(db,"SELECT `Cabinet` FROM `Media`",1);var unpack=Path.Combine(Path.GetDirectoryName(destination)!,"cabinets");Directory.CreateDirectory(unpack);
                foreach(var cabinet in cabinets.Where(c=>c[0].StartsWith('#')))
                {
                    token.ThrowIfCancellationRequested();var name=cabinet[0][1..];if(name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('.' or '_' or '-')))throw new InvalidDataException("Unsafe cabinet name.");var cab=Path.Combine(unpack,name);
                    Check(MsiDatabaseOpenView(db,"SELECT `Data` FROM `_Streams` WHERE `Name`='"+name+"'",out var view));try{Check(MsiViewExecute(view,0));Check(MsiViewFetch(view,out var record));try{using var file=new FileStream(cab,FileMode.CreateNew);var buffer=new byte[65536];while(true){uint size=(uint)buffer.Length;Check(MsiRecordReadStream(record,1,buffer,ref size));if(size==0)break;token.ThrowIfCancellationRequested();file.Write(buffer,0,(int)size);}}finally{MsiCloseHandle(record);}}finally{MsiCloseHandle(view);}
                    ProcessRunner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"expand.exe"),[cab,"-F:*",unpack],token).GetAwaiter().GetResult();File.Delete(cab);
                }
                var dirs=Rows(db,"SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`",3).ToDictionary(r=>r[0]);var components=Rows(db,"SELECT `Component`, `Directory_` FROM `Component`",2).ToDictionary(r=>r[0],r=>r[1]);
                string Relative(string id,int depth=0){if(depth>64)throw new InvalidDataException("Office directory cycle.");if(id=="TARGETDIR"||!dirs.TryGetValue(id,out var row))return "";var parent=Relative(row[1],depth+1);var name=LongName(row[2]);return name is "." or ""?parent:Path.Combine(parent,name);}
                var files=Rows(db,"SELECT `File`, `Component_`, `FileName` FROM `File`",3);var index=0;
                foreach(var row in files)
                {
                    token.ThrowIfCancellationRequested();var source=SafePaths.ArchiveEntry(unpack,row[0]);if(!File.Exists(source))throw new InvalidDataException("Office cabinet is missing a file.");var relative=Path.Combine(Relative(components[row[1]]),LongName(row[2]));
                    // The MSI contains both x86 and x64 VC runtime components in
                    // system directories. Keep only verified package x64 DLLs
                    // app-local; never write or install anything in Windows.
                    var external=relative.Split(Path.DirectorySeparatorChar)[0];if(external is "System64" or "System32" or "System")
                    {
                        if(Path.GetExtension(source).Equals(".dll",StringComparison.OrdinalIgnoreCase)||LongName(row[2]).EndsWith(".dll",StringComparison.OrdinalIgnoreCase))
                        {
                            using var pe=File.OpenRead(source);using var reader=new BinaryReader(pe);if(pe.Length>=64&&reader.ReadUInt16()==0x5a4d){pe.Position=60;var offset=reader.ReadUInt32();if(offset<=pe.Length-6){pe.Position=offset;if(reader.ReadUInt32()==0x4550&&reader.ReadUInt16()==0x8664){var runtime=Path.Combine(destination,".native-runtime");Directory.CreateDirectory(runtime);var dll=SafePaths.ArchiveEntry(runtime,LongName(row[2]));var newer=!File.Exists(dll)||CompareVersion(source,dll)>0;if(newer)File.Copy(source,dll,true);}}}
                        }
                        continue;
                    }
                    var target=SafePaths.ArchiveEntry(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);if(File.Exists(target)){using var existing=File.OpenRead(target);using var incoming=File.OpenRead(source);if(!SHA256.HashData(existing).SequenceEqual(SHA256.HashData(incoming)))throw new InvalidDataException("Office package contains conflicting files: "+relative);}else File.Move(source,target);progress?.Report(new(.75+.24*++index/files.Count,"Preparing office tools…"));
                }

            }finally{MsiCloseHandle(db);}
        }
    }
}
