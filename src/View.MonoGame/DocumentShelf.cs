using BoardGame.Kernel;
using BoardGame.Presentation;

namespace BoardGame.View;

// Seat bindings are resolved before the generic layout sees the page content.
public sealed class DocumentShelf : PageShelf
{
    private readonly DocumentLibrary? library;
    public DocumentShelf(ResourcePool resources) : base(resources,DocumentProjection.Pages(resources.Documents,null))=>library=resources.Documents;
    public void Show(SeatView value)=>base.Show(DocumentProjection.Pages(library,value));
}
