namespace BoardGame.Kernel;

// Square uses column/row; hex uses axial q/r. Neither defines movement cost or game rules.
public sealed record BoardLayout(string Kind="free",double Size=70,double OriginX=100,double OriginY=100)
{
    public void Validate()
    {
        if(Kind is not ("free" or "square" or "hex") || !double.IsFinite(Size) || Size<8 || Size>1000 || !double.IsFinite(OriginX) || !double.IsFinite(OriginY)) throw new InvalidDataException("layout: invalid kind, size or origin");
    }
    public (double X,double Y) World(double x,double y) => Kind switch {
        "square"=>(OriginX+x*Size,OriginY+y*Size),
        "hex"=>(OriginX+Size*Math.Sqrt(3)*(x+y/2),OriginY+Size*1.5*y),
        _=>(OriginX+x,OriginY+y) };
    public (double X,double Y) Logical(double x,double y) => Kind switch {
        "square"=>((x-OriginX)/Size,(y-OriginY)/Size),
        "hex"=>((x-OriginX)/(Size*Math.Sqrt(3))-(y-OriginY)/(3*Size),(y-OriginY)/(Size*1.5)),
        _=>(x-OriginX,y-OriginY) };
    public (double X,double Y) Snap(double x,double y)
    {
        if(!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x)>1e7 || Math.Abs(y)>1e7) throw new InvalidDataException("position: outside supported coordinates");
        if(Kind=="free") return (x,y);
        var q=Math.Round(x,MidpointRounding.AwayFromZero); var r=Math.Round(y,MidpointRounding.AwayFromZero);
        if(Kind=="hex")
        {
            var s=Math.Round(-x-y,MidpointRounding.AwayFromZero); var dq=Math.Abs(q-x); var dr=Math.Abs(r-y); var ds=Math.Abs(s+x+y);
            if(dq>dr && dq>ds) q=-r-s; else if(dr>ds) r=-q-s;
        }
        return (q,r);
    }
    public IReadOnlyList<(double X,double Y)> Neighbors(double x,double y) => Kind switch {
        "square"=>[(x+1,y),(x-1,y),(x,y+1),(x,y-1)],
        "hex"=>[(x+1,y),(x-1,y),(x,y+1),(x,y-1),(x+1,y-1),(x-1,y+1)], _=>[] };
}
