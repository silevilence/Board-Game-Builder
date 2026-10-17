using System.Globalization;
using BoardGame.Kernel;

namespace BoardGame.View;

public static class DocumentProjection
{
    // The caller supplies only its seat projection. Missing or hidden bindings yield no text.
    public static string Text(DocumentField field,SeatView view)
    {
        if(field.Binding is not {} binding)return field.Text;
        var entity=view.Entities.FirstOrDefault(e=>e.Id==binding.Entity);
        if(entity==null || !entity.Properties.TryGetValue(binding.Property,out var value) || binding.HideZero && value==0)return "";
        var number=value.ToString(CultureInfo.InvariantCulture);
        var text=value!=0 && binding.NonzeroText!=null?binding.NonzeroText:binding.Values?.GetValueOrDefault(number) ?? number;
        return field.Text.Length==0?text:field.Text.Replace("{value}",text,StringComparison.Ordinal);
    }
}
