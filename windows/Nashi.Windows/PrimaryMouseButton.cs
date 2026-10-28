using System.Runtime.InteropServices;
namespace Nashi.Windows;

internal static class PrimaryMouseButton
{
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int metric);
    internal static int VirtualKey(bool swapped)=>swapped?0x02:0x01;
    internal static bool IsPressed=>(GetAsyncKeyState(VirtualKey(GetSystemMetrics(23)!=0))&0x8000)!=0;
}
