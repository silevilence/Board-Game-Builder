namespace BoardGame.Presentation;

// All text is already resolved by the caller. No expressions or domain bindings are evaluated here.
public sealed record PageText(double X,double Y,double Size=20,string Text="",string Color="#30271F");
public sealed record PageContent(string Title,string Image,double Width,double Height,IReadOnlyList<PageText> Fields);
public sealed record PagePanel(string Id,string Title,IReadOnlyList<PageContent> Pages,bool Permanent=false,int Width=280);

public sealed class PageNavigator
{
    private readonly int[] counts,pages;
    public int Selected {get;private set;}
    public int CurrentPage=>pages.Length==0?0:pages[Selected];
    public PageNavigator(IEnumerable<int> pageCounts,int selected=0)
    {
        counts=pageCounts.ToArray();pages=new int[counts.Length];
        if(counts.Any(c=>c<1))throw new ArgumentException("Every panel must have a page");
        if(counts.Length>0 && (selected<0 || selected>=counts.Length))throw new ArgumentOutOfRangeException(nameof(selected));
        Selected=selected;
    }
    public bool Select(int index)
    {if(index<0 || index>=pages.Length)return false;Selected=index;return true;}
    public void Turn(int delta)
    {if(pages.Length>0)pages[Selected]=(int)Math.Clamp((long)pages[Selected]+delta,0,counts[Selected]-1);}
}
