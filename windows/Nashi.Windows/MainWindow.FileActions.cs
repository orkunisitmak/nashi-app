using System.Windows;
using Microsoft.Win32;
namespace Nashi.Windows;
public sealed partial class MainWindow
{
    private readonly List<FloatShelf> shelves=[];
    private void ShowFileActions()
    {
        if(busy||files.Count==0)return;var choices=new[]{new ToolChoice("share","Share","share-network"),new("float","Float","push-pin"),new("copy","Copy","copy"),new("move","Move","folder-open"),new("copyPath","Copy Path","path"),new("terminal","Terminal","terminal-window")};new WheelWindow(this,choices,async c=>await PerformFileAction(c.Id,files.ToArray())).Show();
    }
    internal async Task PerformFileAction(string action,IReadOnlyList<string> paths)
    {
        if(busy||paths.Count==0)return;try
        {
            switch(action)
            {
                case "copy":FileActions.Copy(paths);L10n.SetText(status,"Files copied. Paste them wherever you need.");break;
                case "copyPath":FileActions.CopyPaths(paths);L10n.SetText(status,"File paths copied.");break;
                case "terminal":FileActions.Terminal(paths[0]);break;
                case "share":ShowMain();FileActions.Share(this,paths);break;
                case "float":
                    if(preferences.Value.FloatBehavior=="addToExisting"&&shelves.LastOrDefault(s=>s.IsVisible) is {} existing){existing.Add(paths);existing.Activate();break;}
                    var shelf=new FloatShelf(paths,async(p,a)=>await PerformFileAction(a,p),p=>{if(busy)return;deskSession=Guid.NewGuid();files.Clear();Add(p);ShowLibrary(false);ShowMain();});shelves.Add(shelf);shelf.Closed+=(_,_)=>shelves.Remove(shelf);shelf.Show();break;
                case "move":
                {
                    // Cancelling the folder picker (or the job) leaves the desk as it was. After a move,
                    // each card follows its file to the new folder and keeps its spot on the desk.
                    IReadOnlyList<string>? moved=null;var desk=ActiveDesk;var list=InWorkspace?workspaceFiles:files;
                    await RunAsync(async(destination,p,t)=>moved=await FileActions.MoveAsync(paths,destination,p,t),"Move");
                    if(moved is null)break;
                    for(var n=0;n<paths.Count&&n<moved.Count;n++){var index=list.IndexOf(paths[n]);if(index<0||File.Exists(paths[n])||Directory.Exists(paths[n]))continue;desk.Carry(paths[n],moved[n]);if(list.Contains(moved[n]))list.RemoveAt(index);else list[index]=moved[n];}
                    Refresh();break;
                }
            }
        }catch(Exception error){MessageBox.Show(this,L10n.Text(error.Message),L10n.Text("File action could not finish"));}
    }
}
