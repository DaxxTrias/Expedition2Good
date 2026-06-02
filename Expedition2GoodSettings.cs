using System.Collections.Generic;
using System.Drawing;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace Expedition2Good;

public class Expedition2GoodSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public ToggleNode ShowOnMinimap { get; set; } = new ToggleNode(true);

    public ColorNode TextColor { get; set; } = new ColorNode(Color.LightBlue);
    public ColorNode TopPickColor { get; set; } = new ColorNode(Color.LightGreen);
    public RangeNode<float> ValuableColorThreshold { get; set; } = new RangeNode<float>(50, 0, 10000);
    public ColorNode ValuableTextColor { get; set; } = new ColorNode(Color.Pink);

    [Menu("Minimum value to show", "When greater than 0, recipes with a value below this are hidden. Set to 0 to list every item and value.")]
    public RangeNode<float> MinimumValueToShow { get; set; } = new RangeNode<float>(0, 0, 500);

    [Menu("Max items to show", "Maximum number of recipes listed per label (highest value first). Set to 0 to show all.")]
    public RangeNode<int> MaxItemsToShow { get; set; } = new RangeNode<int>(0, 0, 20);

    [Menu("Display only non activated", "Hides expedition encounters whose StateMachine 'activated' state equals 6.")]
    public ToggleNode DisplayOnlyNonActivated { get; set; } = new ToggleNode(false);

    [Menu("Render offset X", "Horizontal offset (pixels) of the text relative to the on-ground object label. 0 keeps the original position.")]
    public RangeNode<int> RenderOffsetX { get; set; } = new RangeNode<int>(0, -2000, 2000);

    [Menu("Render offset Y", "Vertical offset (pixels) of the text relative to the on-ground object label. 0 keeps the original position.")]
    public RangeNode<int> RenderOffsetY { get; set; } = new RangeNode<int>(0, -2000, 2000);

    public ContentNode<PriceOverride> PriceOverrides { get; set; } = new ContentNode<PriceOverride>
        { Content = [], EnableControls = true, EnableItemCollapsing = true, ItemFactory = () => new PriceOverride(), };
    public HashSet<string> KnownRecipes = [];
}

[Submenu]
public class PriceOverride
{
    public ListNode Type { get; set; } = new ListNode();
    public RangeNode<float> Value { get; set; } = new RangeNode<float>(0, 0, 10000);
    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Type.Value) ? base.ToString() : $"{Type.Value} -> {Value}";
    }
}