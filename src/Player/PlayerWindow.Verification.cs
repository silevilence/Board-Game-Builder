using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using BoardGame.Kernel;
namespace BoardGame.Player;

public sealed partial class PlayerWindow
{
    private async Task VerifyBackground(string output)
    {
        var results=new List<object>();
        var app=Application.Current!;
        var previous=app.RequestedThemeVariant;
        try
        {
            foreach(var theme in new[] {ThemeVariant.Light,ThemeVariant.Dark})
            {
                app.RequestedThemeVariant=theme;
                foreach(var name in new[] {"quick-cards","freecell","island-settlers"})
                {
                    path.Text="samples/"+name;
                    await Load();
                    await Task.Delay(100);
                    if(module==null) throw new InvalidOperationException(message.Text);
                    var brush=Background as ISolidColorBrush;
                    results.Add(new {module=name,theme=theme.ToString(),background=brush?.Color.ToString(),opacity=Opacity});
                    if(brush==null || brush.Color.A!=255 || brush.Opacity!=1 || Opacity!=1)
                        throw new InvalidOperationException($"{name} / {theme}: Player window background is not opaque");
                    if(classic && brush.Color!=Color.Parse("#d4d0c8"))
                        throw new InvalidOperationException("FreeCell classic background changed");
                    if(!classic && RequestedThemeVariant!=ThemeVariant.Default)
                        throw new InvalidOperationException("Standard Player does not follow the application theme");
                }
            }
            File.WriteAllText(output,Wire.Json(new {success=true,results}));
        }
        catch(Exception error) {File.WriteAllText(output,Wire.Json(new {success=false,error=error.ToString(),results}));}
        finally {app.RequestedThemeVariant=previous;Close();}
    }
}
