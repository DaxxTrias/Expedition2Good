using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.FilesInMemory;
using ExileCore2.PoEMemory.Models;
using ExileCore2.Shared.Cache;
using ExileCore2.Shared.Helpers;
using Vector2 = System.Numerics.Vector2;

namespace Expedition2Good;

public class Expedition2Good : BaseSettingsPlugin<Expedition2GoodSettings>
{
    private readonly TimeCache<List<(LabelOnGround, Expedition2EncounterLabel)>> _labels;
    private readonly TimeCache<Dictionary<Expedition2Recipe, (double, bool)>> _price;
    private static readonly (double, bool) NoPrice = (0, false);

    public Expedition2Good()
    {
        _labels = new TimeCache<List<(LabelOnGround, Expedition2EncounterLabel)>>(() =>
                GameController.EntityListWrapper.Entities
                    .Any(x => x.Metadata.StartsWith("Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter", StringComparison.Ordinal))
                    ? GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible.Where(x =>
                            x?.ItemOnGround?.Metadata?.StartsWith("Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter", StringComparison.Ordinal) == true)
                        .Select(x => (x, x.Label.AsObject<Expedition2EncounterLabel>())).ToList()
                    : []
            , 1000);
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
                var renderRect = (GameController.Window.GetWindowRectangle() with { Location = Vector2.Zero }).Inflated(-200, -100);
                foreach (var (_, label) in labels)
                {
                    var recipes = allRecipes.Where(x => x.Key <= label.RuneCount)
                        .SelectMany(x => x)
                        .Where(x => x.Runes.ElementAtOrDefault(label.FixedRunePosition)?.Equals(label.FixedRune) == true)
                        .Select(x => (x, value: GetPriceOrDefault(x))).OrderByDescending(x => x.value.Item1).ToList();
                    var bottomLeft = label.GetClientRect().BottomLeft;
                    bottomLeft = renderRect.ClampVector(bottomLeft);
                    var y = bottomLeft.Y;

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
            var options = expedition2Window.Options
                .Where(x => x?.Recipe != null)
                .Select(x => (x, GetPriceOrDefault(x.Recipe)))
                .OrderByDescending(x => x.Item2.Item1)
                .ToList();
            var first = true;
            foreach (var (option, (value, overridden)) in options)
            {
                var textColor = first ? Settings.TopPickColor : value >= Settings.ValuableColorThreshold ? Settings.ValuableTextColor : Settings.TextColor;
                Graphics.DrawTextWithBackground($"{(overridden ? "~" : "")}{value,7:F2}", option.GetClientRectCache.TopLeft, textColor, Color.Black);
                first = false;
            }
        }
    }

    private (double, bool) GetPriceOrDefault(Expedition2Recipe recipe)
    {
        return recipe != null && _price.Value.TryGetValue(recipe, out var price) ? price : NoPrice;
    }
}