using System.Text.Json;
namespace Nashi.Core;

public sealed record LibraryEntry(Guid Id,DateTimeOffset Created,string Action,string[] Paths)
{
    public string Name=>Paths.Length==1?Path.GetFileName(Paths[0]):$"{Paths.Length} files · {Path.GetFileName(Paths[0])}";
    public string[] AvailablePaths=>Paths.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
}

// Lives outside the portable installation, so replacing the app never replaces
// a user's imported clipboard files, history or appearance preference.
public sealed class LibraryStore
{
    public const int Limit=50;
    public string DirectoryPath{get;}
    public string? RecoveryNotice{get;private set;}
    public IReadOnlyList<LibraryEntry> Entries=>state.Entries;
    public string Appearance=>state.Appearance;
    public bool WindowManagementEnabled=>state.WindowManagementEnabled;
    private State state=new("system",[]);
    private string FilePath=>Path.Combine(DirectoryPath,"library.json");
    private sealed record State(string Appearance,List<LibraryEntry> Entries,bool WindowManagementEnabled=true);
    public LibraryStore(string? directory=null)
    {
        DirectoryPath=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi");
        if(!File.Exists(FilePath))return;
        try
        {
            if(new FileInfo(FilePath).Length>4*1024*1024)throw new InvalidDataException("Library is too large.");
            var loaded=JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath))??throw new InvalidDataException("Empty Library.");
            if(loaded.Entries is null||loaded.Entries.Any(e=>e is null||e.Paths is null||e.Paths.Length==0||e.Paths.Length>10000||string.IsNullOrWhiteSpace(e.Action)||e.Paths.Any(p=>string.IsNullOrWhiteSpace(p)||!Path.IsPathFullyQualified(p))))throw new InvalidDataException("Invalid Library entry.");
            state=new(ValidAppearance(loaded.Appearance),loaded.Entries.OrderByDescending(e=>e.Created).Take(Limit).ToList(),loaded.WindowManagementEnabled);
        }
        catch(Exception error)when(error is JsonException or InvalidDataException)
        {
            try{File.Copy(FilePath,FilePath+".recovered-"+Guid.NewGuid().ToString("N"));}catch(Exception backupError)when(backupError is IOException or UnauthorizedAccessException){RecoveryNotice="Library recovery needs access to your Nashi data folder. Your files are safe.";return;}
            RecoveryNotice="Your previous Library could not be read. A recovery copy was kept; your files are safe.";
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException)
        {RecoveryNotice="Library is unavailable. Check access to your Nashi data folder.";}
    }
    public void Record(string action,IEnumerable<string> paths)
    {
        var existing=paths.Where(p=>File.Exists(p)||Directory.Exists(p)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(existing.Length==0)return;
        Save(state with{Entries=new[]{new LibraryEntry(Guid.NewGuid(),DateTimeOffset.Now,action,existing)}.Concat(state.Entries).Take(Limit).ToList()});
    }
    public void SetAppearance(string appearance)=>Save(state with{Appearance=ValidAppearance(appearance)});
    public void SetWindowManagement(bool enabled)=>Save(state with{WindowManagementEnabled=enabled});
    // Actions match in English and the app language; file names match the way macOS finds them,
    // ignoring case and accents in the app language ("izmir" finds "İzmir.png", "cafe" finds "Café.pdf").
    public IEnumerable<LibraryEntry> Search(string query,string language="en")
    {
        query=query.Trim();
        return state.Entries.Where(e=>query.Length==0||AppLocalization.Matches(e.Action,query,language)||e.Paths.Any(p=>AppLocalization.Contains(Path.GetFileName(p),query,language)));
    }
    private static string ValidAppearance(string? value)=>value is "light" or "dark"?value:"system";
    private void Save(State next)
    {
        Directory.CreateDirectory(DirectoryPath);var temporary=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(next));File.Move(temporary,FilePath,true);state=next;}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
