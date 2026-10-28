namespace Nashi.Windows;

internal static class TrayIconRecoveryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var pending=new Queue<Action>();var calls=0;var failures=0;
        using(var recovery=new TrayIconRecovery(()=>++calls>=3,pending.Enqueue,()=>failures++))
        {
            recovery.Begin();check(calls==1&&pending.Count==1,"Tray restore did not schedule after a temporary shell rejection");
            while(pending.TryDequeue(out var next))next();
            check(calls==3&&failures==0,"Tray restore failed to stop after the shell accepted the icon");
        }
        calls=0;failures=0;
        using(var recovery=new TrayIconRecovery(()=>{calls++;return false;},pending.Enqueue,()=>failures++))
        {
            recovery.Begin();while(pending.TryDequeue(out var next))next();
            check(calls==20&&failures==1,"Tray restore must stop retrying and reveal the app after persistent shell rejection");
        }
        calls=0;
        var disposed=new TrayIconRecovery(()=>{calls++;return false;},pending.Enqueue,()=>failures++);
        disposed.Begin();disposed.Dispose();while(pending.TryDequeue(out var next))next();
        check(calls==1,"A disposed tray restore still touched the shell");
        calls=0;failures=0;
        using(var recovery=new TrayIconRecovery(()=>++calls>=3,pending.Enqueue,()=>failures++))
        {
            recovery.Begin();recovery.Begin();while(pending.TryDequeue(out var next))next();
            check(calls==3&&failures==0,"A new Explorer restart did not invalidate an older pending tray retry");
        }
    }
}
