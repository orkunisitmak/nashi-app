using Nashi.Core;
namespace Nashi.Windows;

public sealed record LinuxOptions
{
    public bool PreserveImageMetadata{get;init;}
    public byte[]? AnnotatedPng{get;init;}
    public int? Width{get;init;} public int? Height{get;init;} public int X{get;init;} public int Y{get;init;}
    public long? TargetBytes{get;init;}public double? ReductionPercent{get;init;}
    public int Quality{get;init;}=85;public double Rotation{get;init;} public bool FlipHorizontal{get;init;} public bool FlipVertical{get;init;}
    public PhotoAdjustments Adjustments{get;init;}=new();
    public FrameOptions Frame{get;init;}=new();public CollageOptions Collage{get;init;}=new();
    public string RedactionStyle{get;init;}="solid";public MediaRegion[] Regions{get;init;}=[];
    public int Columns{get;init;}=2; public int Padding{get;init;}=48;public string Background{get;init;}="#f3f1e9";
    public string Text{get;init;}="";public string Ink{get;init;}="#1e271a";public string Font{get;init;}="DejaVu-Sans";public int FontSize{get;init;}=32;
    public int[]? Pages{get;init;}public int[]? PageRotations{get;init;}public bool UniformWidth{get;init;}
    public int PagesPerFile{get;init;}=1;public string PageRanges{get;init;}="";public string Password{get;init;}="";
    public DocumentOptions Document{get;init;}=new();public MediaOptions Media{get;init;}=new();
    public bool RemoveAllMetadata{get;init;}=true;public Dictionary<string,string> Metadata{get;init;}=[];
}
