using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using BoardGame.Kernel;

namespace BoardGame.View;

// Each game surface owns a unique collection; closing one never unregisters another game's font.
public sealed class PackagedControlFont : IDisposable
{
    private sealed class Collection : FontCollectionBase
    {public override Uri Key {get;}=new("fonts:bgb-"+Guid.NewGuid().ToString("N"));}
    private readonly Control target;
    private Collection? collection;
    public PackagedControlFont(Control target,ResourcePool? resources)
    {
        this.target=target;
        if(resources?.Font is not {} font)return;
        var next=new Collection();
        try
        {
            using var stream=new MemoryStream(resources.GetFile(font.Resource).ToArray());
            if(!next.TryAddGlyphTypeface(stream,out var face))throw new InvalidDataException(font.Resource+": UI font could not be decoded");
            FontManager.Current.AddFontCollection(next);collection=next;
            target.SetValue(TextBlock.FontFamilyProperty,new FontFamily(next.Key+"#"+face.FamilyName));
        }
        catch {if(collection!=null)FontManager.Current.RemoveFontCollection(next.Key);else ((IDisposable)next).Dispose();collection=null;throw;}
    }
    public void Dispose()
    {
        if(collection==null)return;
        target.ClearValue(TextBlock.FontFamilyProperty);
        FontManager.Current.RemoveFontCollection(collection.Key);collection=null;
    }
}
