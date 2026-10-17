namespace BoardGame.Kernel;

public sealed record DocumentBinding(string Entity,string Property,bool HideZero=false,string? NonzeroText=null,IReadOnlyDictionary<string,string>? Values=null);
public sealed record DocumentField(double X,double Y,double Size=20,string Text="",DocumentBinding? Binding=null,string Color="#30271F");
public sealed record DocumentPage(string Title,string Image,double Width,double Height,IReadOnlyList<DocumentField> Fields);
public sealed record DocumentPanel(string Id,string Title,IReadOnlyList<DocumentPage> Pages,bool Permanent=false,int Width=280);
public sealed record DocumentLibrary(int Format,IReadOnlyList<DocumentPanel> Panels);
