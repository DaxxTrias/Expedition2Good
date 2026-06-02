using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
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
    private readonly TimeCache<List<(LabelOnGround LabelOnGround, Expedition2EncounterLabel Label)>> _labels;
    private readonly TimeCache<Dictionary<Expedition2Recipe, (double, bool)>> _price;
    private static readonly (double, bool) NoPrice = (0, false);

    public Expedition2Good()
    {
        _labels = new TimeCache<List<(LabelOnGround LabelOnGround, Expedition2EncounterLabel Label)>>(GetVisibleExpeditionLabels, 1000);
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
        if (_labels.Value is { Count: > 0 } labels)
        {
            var allRecipes = GameController.Files.Expedition2Recipes.EntriesList.ToLookup(x => x.RuneCountRequired);
            if (allRecipes.Count > 0)
            {
                var windowRect = GameController.Window.GetWindowRectangle() with { Location = Vector2.Zero };
                var textBounds = windowRect.Inflated(-200, -100);
                var areaLevel = GameController.IngameState.Data.CurrentAreaLevel;
                var expedition2RunesWeights = GameController.Files.Expedition2RunesWeights.EntriesList;
                foreach (var (labelOnGround, label) in labels)
                {
                    var entity = labelOnGround.ItemOnGround;
                    if (entity == null ||
                        !TryGetDrawableLabelRect(labelOnGround, label, windowRect, out var labelRect) ||
                        Settings.DisplayOnlyNonActivated &&
                        entity.GetComponent<StateMachine>()?.States is { } states &&
                        states.Any(s => s.Name == "activated" && ((int)s.Value == 6)))
                    {
                        continue;
                    }

                    var allowedRuneCounts = expedition2RunesWeights.Where(x => x.RuneSlot - 1 == label.FixedRunePosition)
                        .Where(x => x.Rune.Equals(label.FixedRune))
                        .Where(x => x.Level <= areaLevel)
                        .Select(x => x.SlotCount)
                        .ToHashSet();
                    var recipes = allRecipes.Where(x => x.Key <= label.RuneCount)
                        .SelectMany(x => x)
                        .Where(x => allowedRuneCounts.Contains(x.RuneCountRequired))
                        .Where(x => x.MinLevelReq <= areaLevel && x.MaxLevelReq >= areaLevel)
                        .Where(x => x.Runes.ElementAtOrDefault(label.FixedRunePosition)?.Equals(label.FixedRune) == true)
                        .Select(x => (x, value: GetPriceOrDefault(x)))
                        .OrderByDescending(x => x.value.Item1)
                        .ToList();
                    if (Settings.MinimumValueToShow > 0)
                    {
                        recipes = recipes.Where(x => x.value.Item1 >= Settings.MinimumValueToShow).ToList();
                    }

                    if (Settings.MaxItemsToShow > 0)
                    {
                        recipes = recipes.Take(Settings.MaxItemsToShow).ToList();
                    }

                    var bottomLeft = textBounds.ClampVector(labelRect.BottomLeft + new Vector2(Settings.RenderOffsetX, Settings.RenderOffsetY));
                    var y = bottomLeft.Y;

                    var first = true;
                    foreach (var (recipe, (value, overridden)) in recipes)
                    {
                        if (first && Settings.ShowOnMinimap)
                        {
                            Graphics.DrawTextWithBackground($"Rune {(overridden ? "~" : "")}{value:F1}", Graphics.GridToMap(entity.GridPos, entity.GridPos), Color.Black);
                        }

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
                if (!IsDrawableRect(optionRect) ||
                    !bounds.Intersects(optionRect) ||
                    !bounds.Contains(optionRect.TopLeft))
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
        if (!GameController.EntityListWrapper.Entities.Any(x => x.Metadata?.StartsWith(ExpeditionEncounterMetadataPrefix, StringComparison.Ordinal) == true))
        {
            return [];
        }

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
        return IsDrawableRect(labelRect) && bounds.Intersects(labelRect);
    }

    private static bool IsDrawableRect(RectangleF rect)
    {
        return rect.Width > 1 && rect.Height > 1;
    }

    private static RectangleF GetVisibleBounds(Element element, RectangleF fallbackBounds)
    {
        var bounds = fallbackBounds;
        for (var parent = element.Parent; parent is { IsValid: true }; parent = parent.Parent)
        {
            var parentRect = parent.GetClientRectCache;
            if (IsDrawableRect(parentRect) && bounds.Intersects(parentRect))
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