using System.Text.Json;
namespace Nashi.Core;

/// A Library entry: a batch of files, or (Action "Desk") a space — the Home desk as it was left,
/// with where each card sat (Spots: path → x,y), what was done there (History) and when it was last touched.
public sealed record LibraryEntry(Guid Id,DateTimeOffset Created,string Action,string[] Paths,Dictionary<string,double[]>? Spots=null,SpaceEvent[]? History=null,DateTimeOffset? Touched=null)
{
    public const string Space="Desk";
    public bool IsSpace=>Action==Space;
    public string Name=>Paths.Length==1?Path.GetFileName(Paths[0]):$"{Paths.Length} files · {Path.GetFileName(Paths[0])}";
    public string[] AvailablePaths=>Paths.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
    public string[] MissingPaths=>Paths.Where(p=>!File.Exists(p)&&!Directory.Exists(p)).ToArray();
}
/// One thing done in a space: the tool or action, when, and how many files it made.
public sealed record SpaceEvent(DateTimeOffset At,string Action,int Outputs);

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
            if(loaded.Entries is null||loaded.Entries.Any(e=>e is null||e.Paths is null||e.Paths.Length==0||e.Paths.Length>10000||string.IsNullOrWhiteSpace(e.Action)||e.Paths.Any(p=>string.IsNullOrWhiteSpace(p)||!Path.IsPathFullyQualified(p))||e.Spots?.Any(s=>s.Value is not {Length:2}||!s.Value.All(double.IsFinite))==true||e.History?.Any(h=>h is null||string.IsNullOrWhiteSpace(h.Action))==true))throw new InvalidDataException("Invalid Library entry.");
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
    /// Creates or updates one living entry (the desk being worked on); an empty list removes it.
    /// It stays where it is in the list so the Library does not reshuffle while you work.
    public void Keep(Guid id,string action,IEnumerable<string> paths)
    {
        var existing=paths.Where(p=>File.Exists(p)||Directory.Exists(p)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var entries=state.Entries.ToList();var index=entries.FindIndex(e=>e.Id==id);
        if(existing.Length==0){if(index<0)return;entries.RemoveAt(index);}
        else if(index<0)entries.Insert(0,new LibraryEntry(id,DateTimeOffset.Now,action,existing));
        else{if(entries[index].Paths.SequenceEqual(existing,StringComparer.OrdinalIgnoreCase))return;entries[index]=entries[index] with{Paths=existing};}
        Save(state with{Entries=entries.Take(Limit).ToList()});
    }
    /// Saves a space as it is right now: every card (a file deleted since stays, so the space can
    /// say so), where moved cards sit, and its history. An empty desk leaves no space behind.
    public void KeepSpace(Guid id,IEnumerable<string> paths,IReadOnlyDictionary<string,double[]> spots)
    {
        var all=paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var entries=state.Entries.ToList();var index=entries.FindIndex(e=>e.Id==id);
        var kept=spots.Where(s=>all.Contains(s.Key,StringComparer.OrdinalIgnoreCase)).ToDictionary(s=>s.Key,s=>s.Value,StringComparer.OrdinalIgnoreCase);
        if(all.Length==0){if(index<0)return;entries.RemoveAt(index);}
        else if(index<0)entries.Insert(0,new LibraryEntry(id,DateTimeOffset.Now,LibraryEntry.Space,all,kept,[],DateTimeOffset.Now));
        else
        {
            var old=entries[index];
            if(old.Paths.SequenceEqual(all,StringComparer.OrdinalIgnoreCase)&&SameSpots(old.Spots,kept))return;
            entries[index]=old with{Action=LibraryEntry.Space,Paths=all,Spots=kept,Touched=DateTimeOffset.Now};
        }
        Save(state with{Entries=entries.Take(Limit).ToList()});
    }
    /// Notes an operation in a space's history (newest last, the last 100 kept).
    public void LogSpace(Guid id,string action,int outputs)
    {
        var entries=state.Entries.ToList();var index=entries.FindIndex(e=>e.Id==id);if(index<0)return;
        var history=(entries[index].History??[]).Append(new SpaceEvent(DateTimeOffset.Now,action,outputs)).TakeLast(100).ToArray();
        entries[index]=entries[index] with{History=history,Touched=DateTimeOffset.Now};Save(state with{Entries=entries});
    }
    private static bool SameSpots(Dictionary<string,double[]>? a,Dictionary<string,double[]> b)=>(a?.Count??0)==b.Count&&b.All(pair=>a!.TryGetValue(pair.Key,out var spot)&&Math.Abs(spot[0]-pair.Value[0])<.5&&Math.Abs(spot[1]-pair.Value[1])<.5);
    /// Takes files out of the Library (not off the disk); entries left empty disappear.
    public void Forget(IEnumerable<string> paths)
    {
        var gone=paths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);if(gone.Count==0)return;
        var entries=state.Entries.Select(e=>e with{Paths=e.Paths.Where(p=>!gone.Contains(p)).ToArray()}).Where(e=>e.Paths.Length>0).ToList();
        Save(state with{Entries=entries});
    }
    public void SetAppearance(string appearance)=>Save(state with{Appearance=ValidAppearance(appearance)});
    public void SetWindowManagement(bool enabled)=>Save(state with{WindowManagementEnabled=enabled});
    public IEnumerable<LibraryEntry> Search(string query)=>state.Entries.Where(e=>string.IsNullOrWhiteSpace(query)||e.Action.Contains(query,StringComparison.OrdinalIgnoreCase)||e.Paths.Any(p=>Path.GetFileName(p).Contains(query,StringComparison.OrdinalIgnoreCase)));
    private static string ValidAppearance(string? value)=>value is "light" or "dark"?value:"system";
    private void Save(State next)
    {
        Directory.CreateDirectory(DirectoryPath);var temporary=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(next));File.Move(temporary,FilePath,true);state=next;}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
