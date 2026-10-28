using BoardGame.Presentation;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var curve=new TimelineCurve([new(100,-20,10,90),new(200,20,-10,0),new(300,0,0,-90)]);
Check(curve.Sample(150)==new TimelineSample(0,0,45,.75f),"Interpolation changed");
Check(curve.Sample(-100)==new TimelineSample(-20,10,90,1),"First key clamp failed");
Check(curve.Sample(1000)==new TimelineSample(0,0,-90,0),"Last key clamp failed");
foreach(var cadence in new[]{7,16,33,100})
{
    var playback=new TimelinePlayback(500,300);
    for(var time=500;time<800;time+=cadence)Check(!playback.Complete(time),"Playback ended early");
    Check(playback.Complete(800) && playback.Elapsed(400)==0,"Playback lifetime differs across cadence");
}
foreach(var keys in new TimelineKey[][]{[new(0),new(0)],[new(0),new(double.NaN)],[new(0),new(1,float.PositiveInfinity)]})
{
    try{_=new TimelineCurve(keys);throw new Exception("Invalid curve accepted");}catch(ArgumentException){}
}
var pages=new PageNavigator([3,2],0);pages.Turn(1);Check(pages.CurrentPage==1,"Page turn failed");
pages.Select(1);pages.Turn(int.MaxValue);Check(pages.CurrentPage==1,"Page bounds overflow");
pages.Select(0);Check(pages.CurrentPage==1,"Panel switch lost page");pages.Turn(int.MinValue);Check(pages.CurrentPage==0,"Negative page bounds");
Check(!pages.Select(-1) && !pages.Select(99),"Invalid panel selected");
var empty=new PageNavigator([]);empty.Turn(1);Check(empty.CurrentPage==0,"Empty navigation failed");
var page=new PageContent("Instructions","diagram",640,480,[new(10,20,Text:"Continuous movement")]);
Check(page.Fields[0].Text=="Continuous movement","Generic page requires domain fields");
Check(typeof(TimelineCurve).Assembly.GetReferencedAssemblies().All(a=>a.Name!.StartsWith("System")),"Generic presentation depends on a game or UI library");
Console.WriteLine("PASS generic interpolation, cadence, page layout/navigation without board state or UI");
