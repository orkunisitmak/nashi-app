using Nashi.Core;
internal static class ToolSetupChecks
{
    [System.Runtime.InteropServices.DllImport("libc")]private static extern uint geteuid();
    internal static void Run(Action<bool,string> check,string root)
    {
        check(!new ToolSetupStatus(false,false).Ready&&new ToolSetupStatus(false,false).Missing.SequenceEqual(new[]{ToolGroup.Media,ToolGroup.Office}),"Clean profile must offer both tool groups");
        check(new ToolSetupStatus(true,false).Missing.SequenceEqual(new[]{ToolGroup.Office}),"Installed media must survive retrying Office setup");
        check(new ToolSetupStatus(true,true).Ready&&new ToolSetupStatus(true,true).Missing.Length==0,"Existing tools must not be downloaded again");
        var directory=Path.Combine(root,"Setup checks");Directory.CreateDirectory(directory);
        try
        {
            var target=Path.Combine(directory,"tools");var stage=Path.Combine(directory,"stage");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"partial"),"keep");Directory.CreateDirectory(stage);
            bool Complete(string path)=>ToolInstallation.UsableFile(Path.Combine(path,"ready"));
            try{ToolInstallation.Publish(stage,target,Complete);throw new Exception("Incomplete download published");}catch(InvalidDataException){check(File.ReadAllText(Path.Combine(target,"partial"))=="keep","Incomplete download damaged installed tools");}
            File.WriteAllText(Path.Combine(stage,"ready"),"new");ToolInstallation.Publish(stage,target,Complete);
            check(Complete(target)&&!File.Exists(Path.Combine(target,"partial")),"Retry must repair an incomplete tool directory");
            var second=Path.Combine(directory,"second");Directory.CreateDirectory(second);File.WriteAllText(Path.Combine(second,"ready"),"other");ToolInstallation.Publish(second,target,Complete);
            check(File.ReadAllText(Path.Combine(target,"ready"))=="new","Concurrent completed installation must be retained");
            File.Delete(Path.Combine(target,"ready"));File.WriteAllText(Path.Combine(target,"partial"),"rollback");
            try{ToolInstallation.Publish(second,target,p=>{if(p==second){Directory.Delete(second,true);return true;}return false;});throw new Exception("Vanished stage published");}catch(IOException){check(File.ReadAllText(Path.Combine(target,"partial"))=="rollback","Failed replacement did not restore installed tools");}
            File.WriteAllText(Path.Combine(target,"zero.exe"),"");check(!ToolInstallation.UsableFile(Path.Combine(target,"zero.exe")),"Empty placeholder must not count as installed tool");
            var suffix=OperatingSystem.IsWindows()?".exe":"";
            void Write(string path,string text){File.WriteAllText(path,text);ToolInstallation.MakeExecutable(path);}
            var platform=Path.Combine(directory,"platform tools");Directory.CreateDirectory(platform);
            void Tool(string name)=>Write(Path.Combine(platform,name),"x");
            foreach(var name in new[]{"ffmpeg","ffprobe","yt-dlp"})Tool(name+suffix);Tool(OperatingSystem.IsWindows()?"deno":"deno.exe");
            check(!new Toolchain(platform).Ready,"A tool named for another platform must not count as installed");
            Tool("deno"+suffix);check(new Toolchain(platform).Ready&&new Toolchain(platform).Executable("deno")==Path.Combine(platform,"deno"+suffix),"Tools must use this platform's executable names");
            if(!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path.Combine(platform,"deno"),UnixFileMode.UserRead|UnixFileMode.UserWrite);check(!new Toolchain(platform).Ready,"A tool without the executable bit must not count as installed");
                var metadata=Path.Combine(directory,"metadata");var exiftool=Path.Combine(metadata,"metadata-13.59","exiftool");Directory.CreateDirectory(Path.GetDirectoryName(exiftool)!);File.WriteAllText(exiftool,"x");
                // The perl search path is a test directory so the real system perl is never touched.
                var perlBin=Path.Combine(directory,"perl-bin");Directory.CreateDirectory(perlBin);var perl=Path.Combine(perlBin,"perl");File.WriteAllText(perl,"x");ToolInstallation.MakeExecutable(perl);
                var library=Path.Combine(Path.GetDirectoryName(exiftool)!,"lib","Image","ExifTool.pm");Directory.CreateDirectory(Path.GetDirectoryName(library)!);File.WriteAllText(library,"x");
                check(new MetadataService(metadata,perlBin).Executable is null,"Metadata tools without the executable bit must not count as installed");
                ToolInstallation.MakeExecutable(exiftool);check(new MetadataService(metadata,perlBin).Executable==exiftool,"Executable metadata tools must count as installed");
                File.Delete(library);check(new MetadataService(metadata,perlBin).Executable is null,"ExifTool without lib/Image/ExifTool.pm must not count as installed");File.WriteAllText(library,"x");
                var noPerl=Path.Combine(directory,"no-perl");Directory.CreateDirectory(noPerl);
                check(new MetadataService(metadata,noPerl).Executable is null,"ExifTool must not count as installed without perl");
                var noPerlInstall=false;try{new MetadataService(Path.Combine(directory,"fresh-metadata"),noPerl).InstallAsync(null,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException){noPerlInstall=true;}check(noPerlInstall,"Metadata setup must stop before downloading when perl is missing");
                File.SetUnixFileMode(perl,UnixFileMode.UserRead|UnixFileMode.UserWrite);check(new MetadataService(metadata,perlBin).Executable is null,"A perl without the executable bit must not count as usable");
                ToolInstallation.MakeExecutable(perl);var perlLink=Path.Combine(directory,"perl-link");Directory.CreateDirectory(perlLink);File.CreateSymbolicLink(Path.Combine(perlLink,"perl"),perl);check(new MetadataService(metadata,perlLink).Executable==exiftool,"A perl symlink to an executable must count as usable");
                var mode=Path.Combine(directory,"mode");File.WriteAllText(mode,"x");
                File.SetUnixFileMode(mode,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead|UnixFileMode.OtherRead);check(!ToolInstallation.CanExecute(mode),"A 0644 file must not be executable");
                ToolInstallation.MakeExecutable(mode);check(ToolInstallation.CanExecute(mode),"A 0755 file must be executable");
                File.SetUnixFileMode(mode,UnixFileMode.OtherExecute);if(geteuid()!=0)check(!ToolInstallation.CanExecute(mode),"Only the other execute bit must not make the owner's file executable");
                check(ToolInstallation.CanExecute("/bin/sh"),"System executables owned by root must be executable");
                check(!ToolInstallation.CanExecute(Path.Combine(directory,"missing")),"A missing file must not be executable");
            }
            var bundled=Path.Combine(directory,"bundled");var installed=Path.Combine(directory,"installed");Directory.CreateDirectory(installed);
            foreach(var name in new[]{"ffmpeg","ffprobe","yt-dlp","deno"})Write(Path.Combine(installed,name+suffix),"tool");
            check(!new Toolchain(bundled).Ready,"Tools on PATH must stay unused without a search path");
            var found=new Toolchain(bundled,string.Join(Path.PathSeparator,"","relative",'"'+installed+'"'));
            check(found.Ready&&found.Executable("yt-dlp")==Path.Combine(installed,"yt-dlp"+suffix),"Installed tools on the search path must be reused");
            check(new WebDownload(found).FfmpegLocation==installed,"yt-dlp must use the FFmpeg found on the search path");
            check(found.InstallAsync().IsCompletedSuccessfully&&!Directory.Exists(bundled),"Tools on the search path must not be downloaded again");
            var previous=Environment.CurrentDirectory;Environment.CurrentDirectory=installed;
            try{check(!new Toolchain(bundled,".").Ready,"Relative search path entries must be ignored");}finally{Environment.CurrentDirectory=previous;}
            Directory.CreateDirectory(bundled);Write(Path.Combine(bundled,"ffmpeg"+suffix),"pinned");
            check(found.Executable("ffmpeg")==Path.Combine(bundled,"ffmpeg"+suffix)&&found.Executable("ffprobe")==Path.Combine(installed,"ffprobe"+suffix),"Downloaded tools must take priority over the search path");
            Write(Path.Combine(installed,"deno"+suffix),"");check(!found.Ready,"Empty search path placeholder must not count as installed tool");
            if(!OperatingSystem.IsWindows())
            {
                var snapBin=Path.Combine(directory,"snap bin");Directory.CreateDirectory(snapBin);
                Write(Path.Combine(snapBin,"launcher"),"launcher");File.CreateSymbolicLink(Path.Combine(snapBin,"ffmpeg"),Path.Combine(snapBin,"launcher"));
                check(new Toolchain(Path.Combine(directory,"none"),snapBin).Executable("ffmpeg").EndsWith("/ffmpeg",StringComparison.Ordinal),"A PATH tool symlinked to a launcher must run under its PATH name");
            }
        }
        finally{Directory.Delete(directory,true);}
    }
}
