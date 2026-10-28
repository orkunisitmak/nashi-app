namespace Nashi.Windows;

/// Explorer can broadcast TaskbarCreated before it accepts tray icons again.
/// Keep retries bounded and invalidate queued work on restart or disposal.
internal sealed class TrayIconRecovery(Func<bool> publish,Action<Action> schedule,Action failed):IDisposable
{
    private int generation;
    private bool disposed;
    internal void Begin(){if(!disposed)Attempt(++generation,0);}
    private void Attempt(int request,int attempt)
    {
        if(disposed||request!=generation)return;
        if(publish())return;
        if(attempt==19){failed();return;}
        schedule(()=>Attempt(request,attempt+1));
    }
    public void Dispose(){disposed=true;generation++;}
}
