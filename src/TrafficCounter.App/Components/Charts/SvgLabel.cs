using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace TrafficCounter.App.Components.Charts;

/// <summary>An SVG &lt;text&gt; element; Razor reserves the tag name inside code blocks.</summary>
public sealed class SvgLabel : ComponentBase
{
    [Parameter] public double X { get; set; }

    [Parameter] public double Y { get; set; }

    [Parameter] public string Anchor { get; set; } = "middle";

    [Parameter] public string CssClass { get; set; } = "axis";

    [Parameter] public string Text { get; set; } = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "x", X.ToString("0.#", CultureInfo.InvariantCulture));
        builder.AddAttribute(2, "y", Y.ToString("0.#", CultureInfo.InvariantCulture));
        builder.AddAttribute(3, "class", CssClass);
        builder.AddAttribute(4, "text-anchor", Anchor);
        builder.AddContent(5, Text);
        builder.CloseElement();
    }
}
