using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.FilesInMemory;
using ExileCore2.PoEMemory.Models;
using ExileCore2.Shared.Cache;
using ExileCore2.Shared.Helpers;
using RectangleF = ExileCore2.Shared.RectangleF;
using Vector2 = System.Numerics.Vector2;

namespace Expedition2Good;

public class Expedition2Good : BaseSettingsPlugin<Expedition2GoodSettings>
{
    private const string ExpeditionEncounterMetadataPrefix = "Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter";
    private const float RuneTooltipTextOffsetLines = 1.75f;
    private readonly TimeCache<Dictionary<Expedition2Recipe, (double, bool)>> _price;
    private static readonly (double, bool) NoPrice = (0, false);

    public Expedition2Good()
    {
        _price = new TimeCache<Dictionary<Expedition2Recipe, (double, bool)>>(() =>
        {
            var getCurrencyValue = GameController.PluginBridge.GetMethod<Func<BaseItemType, double>>("NinjaPrice.GetBaseItemTypeValue") ?? (_ => 0);
            return GameController.Files.Expedition2Recipes.EntriesList.ToDictionary(x => x, x =>
            {
                if (Settings.PriceOverrides.Content.FirstOrDefault(priceOverride => priceOverride.Type.Value == x.Id) is { } over)
                {
                    return (over.Value, true);
                }

                return ((x.Reward == null ? 0 : getCurrencyValue(x.Reward)) * x.RewardCount, false);
            });
        }, 1000);
    }

    public override bool Initialise()
    {
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
    }

    public override void DrawSettings()
    {
        var knownRecipes = Settings.KnownRecipes.OrderBy(x => x).ToList();
        foreach (var priceOverride in Settings.PriceOverrides.Content)
        {
            priceOverride.Type.SetListValues(knownRecipes);
        }

        base.DrawSettings();
    }

    public override void Tick()
    {
        Settings.KnownRecipes.UnionWith(_price.Value.Keys.Select(x => x.Id));
    }

    public override void Render()
    {
        var labels = GetVisibleExpeditionLabels();
        if (labels.Count > 0)
        {
            var allRecipes = GameController.Files.Expedition2Recipes.EntriesList.ToLookup(x => x.RuneCountRequired);
            if (allRecipes.Count > 0)
            {
                var windowRect = GameController.Window.GetWindowRectangle() with { Location = Vector2.Zero };
                var textBounds = windowRect.Inflated(-200, -100);
                foreach (var (labelOnGround, label) in labels)
                {
                    if (!TryGetDrawableLabelRect(labelOnGround, label, windowRect, out var labelRect))
                    {
                        continue;
                    }

                    var recipes = allRecipes.Where(x => x.Key <= label.RuneCount)
                        .SelectMany(x => x)
                        .Where(x => x.Runes.ElementAtOrDefault(label.FixedRunePosition)?.Equals(label.FixedRune) == true)
                        .Select(x => (x, value: GetPriceOrDefault(x))).OrderByDescending(x => x.value.Item1).ToList();
                    var bottomLeft = textBounds.ClampVector(labelRect.BottomLeft);
                    var y = bottomLeft.Y + Graphics.MeasureText("0.00").Y * RuneTooltipTextOffsetLines;

                    var first = true;
                    foreach (var (recipe, (value, overridden)) in recipes)
                    {
                        var textColor = first ? Settings.TopPickColor : value >= Settings.ValuableColorThreshold ? Settings.ValuableTextColor : Settings.TextColor;
                        var size = Graphics.DrawTextWithBackground(
                            $"{(overridden ? "~" : "")}{value,7:F2} {(string.IsNullOrWhiteSpace(recipe.Description) ? recipe.Reward?.BaseName : recipe.Description)} x{recipe.RewardCount}",
                            bottomLeft with { Y = y },
                            textColor, Color.Black);
                        y += size.Y;
                        first = false;
                    }

                    //GameController.InspectObject(recipes, "Recipes");
                }
            }

            //GameController.InspectObject(labels, "Labels");
        }

        if (GameController.IngameState.IngameUi.Expedition2Window is { IsVisible: true } expedition2Window)
        {
            var windowRect = expedition2Window.GetClientRectCache;
            if (!IsDrawableRect(windowRect))
            {
                return;
            }

            var options = expedition2Window.Options
                .Where(x => x is { IsValid: true, IsVisible: true, IsVisibleLocal: true, Recipe: not null })
                .Select(x => (x, GetPriceOrDefault(x.Recipe)))
                .OrderByDescending(x => x.Item2.Item1)
                .ToList();
            var first = true;
            foreach (var (option, (value, overridden)) in options)
            {
                var optionRect = option.GetClientRectCache;
                var bounds = GetVisibleBounds(option, windowRect);
                if (!Intersects(bounds, optionRect) || !Contains(bounds, optionRect.TopLeft))
                {
                    continue;
                }

                var text = $"{(overridden ? "~" : "")}{value,7:F2}";
                var textSize = Graphics.MeasureText(text);
                var position = ClampTextPosition(optionRect.TopLeft, textSize, bounds);
                var textColor = first ? Settings.TopPickColor : value >= Settings.ValuableColorThreshold ? Settings.ValuableTextColor : Settings.TextColor;
                Graphics.DrawTextWithBackground(text, position, textColor, Color.Black);
                first = false;
            }
        }
    }

    private List<(LabelOnGround LabelOnGround, Expedition2EncounterLabel Label)> GetVisibleExpeditionLabels()
    {
        var result = new List<(LabelOnGround, Expedition2EncounterLabel)>();
        foreach (var labelOnGround in GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible)
        {
            if (labelOnGround == null)
            {
                continue;
            }

            var itemOnGround = labelOnGround.ItemOnGround;
            if (itemOnGround?.Metadata?.StartsWith(ExpeditionEncounterMetadataPrefix, StringComparison.Ordinal) != true)
            {
                continue;
            }

            var labelElement = labelOnGround.Label;
            if (labelElement?.IsVisible != true)
            {
                continue;
            }

            var encounterLabel = labelElement.AsObject<Expedition2EncounterLabel>();
            if (encounterLabel?.IsVisible == true)
            {
                result.Add((labelOnGround, encounterLabel));
            }
        }

        return result;
    }

    private (double, bool) GetPriceOrDefault(Expedition2Recipe recipe)
    {
        return recipe != null && _price.Value.TryGetValue(recipe, out var price) ? price : NoPrice;
    }

    private static bool TryGetDrawableLabelRect(LabelOnGround labelOnGround, Expedition2EncounterLabel label, RectangleF bounds, out RectangleF labelRect)
    {
        labelRect = default;
        if (!labelOnGround.IsVisible || !label.IsVisible)
        {
            return false;
        }

        labelRect = label.GetClientRect();
        return IsDrawableRect(labelRect) && Intersects(bounds, labelRect);
    }

    private static bool IsDrawableRect(RectangleF rect)
    {
        return rect.Width > 1 && rect.Height > 1;
    }

    private static bool Intersects(RectangleF a, RectangleF b)
    {
        return IsDrawableRect(b) && a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
    }

    private static bool Contains(RectangleF rect, Vector2 point)
    {
        return point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;
    }

    private static RectangleF GetVisibleBounds(Element element, RectangleF fallbackBounds)
    {
        var bounds = fallbackBounds;
        for (var parent = element.Parent; parent is { IsValid: true }; parent = parent.Parent)
        {
            var parentRect = parent.GetClientRectCache;
            if (IsDrawableRect(parentRect) && Intersects(bounds, parentRect))
            {
                bounds = Intersect(bounds, parentRect);
            }
        }

        return bounds;
    }

    private static RectangleF Intersect(RectangleF a, RectangleF b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return new RectangleF(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static Vector2 ClampTextPosition(Vector2 position, Vector2 textSize, RectangleF bounds)
    {
        var maxX = Math.Max(bounds.Left, bounds.Right - textSize.X);
        var maxY = Math.Max(bounds.Top, bounds.Bottom - textSize.Y);
        return new Vector2(Math.Clamp(position.X, bounds.Left, maxX), Math.Clamp(position.Y, bounds.Top, maxY));
    }
}