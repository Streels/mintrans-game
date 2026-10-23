using Godot;
using Mintrans.Core.Ids;
using Mintrans.Data.Json;

namespace Mintrans.Game.View;

/// <summary>Как раскрасить одну природную категорию.</summary>
/// <param name="Mode">"base" — основной цвет клетки, "blend" — подмешать, "replace" — заменить.</param>
public sealed record TerrainLayerStyle(TypeId Category, string Mode, float Amount, IReadOnlyDictionary<TypeId, Color> Colors);

public sealed record LineStyle(Color Color, float Width);

public sealed record SiteStyle(Color Fill, Color Outline);

/// <summary>Мосты, сигналы, переезды и обломки (docs/10а, docs/10б). <see cref="LiftLevels"/> — насколько настил моста выше рельефа.</summary>
public sealed record RailStyle(
    Color BridgeDeck, Color BridgeEdge, Color BridgePier, int LiftLevels,
    Color SignalPost, Color SignalGreen, Color SignalRed,
    Color CrossingOpen, Color CrossingClosed, Color Wreck);

/// <summary>
/// Внешний вид карты из game/styles/map_style.json. Только оформление: на правила игры не влияет.
/// Неизвестный вид рисуется ярко-розовым, чтобы пропуск в стиле сразу бросался в глаза.
/// </summary>
public sealed class MapStyle
{
    public static readonly Color Missing = new(1f, 0f, 1f);

    public required IReadOnlyList<TerrainLayerStyle> TerrainLayers { get; init; }
    public required float ShadePerLevel { get; init; }
    public required float SlopeShade { get; init; }
    public required float Checker { get; init; }
    public required Color CliffRight { get; init; }
    public required Color CliffLeft { get; init; }
    public required IReadOnlyDictionary<TypeId, LineStyle> Networks { get; init; }
    public required IReadOnlyDictionary<TypeId, SiteStyle> Sites { get; init; }
    public required SiteStyle DefaultSite { get; init; }

    /// <summary>Отдельный вид для частей площадки (станций, модулей); если нет — цвет объекта.</summary>
    public required IReadOnlyDictionary<TypeId, SiteStyle> Parts { get; init; }
    public required Color Highlight { get; init; }
    public required RailStyle Rail { get; init; }

    public SiteStyle SiteFor(TypeId type) => Sites.TryGetValue(type, out var style) ? style : DefaultSite;

    public SiteStyle PartFor(TypeId kind, SiteStyle site) => Parts.TryGetValue(kind, out var style) ? style : site;

    public LineStyle NetworkFor(TypeId type) => Networks.TryGetValue(type, out var style) ? style : new LineStyle(Missing, 2);

    public static MapStyle Load(string path)
    {
        var json = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(json))
            throw new ContentException(path, "файл стиля не найден или пуст");
        var root = JsonNode.Parse(json, path);

        var height = root.Get("height");
        var cliffs = root.Get("cliffs");
        var rail = root.Get("rail");
        return new MapStyle
        {
            TerrainLayers = root.Get("terrain").Items().Select(node => new TerrainLayerStyle(
                node.String("category"),
                node.String("mode"),
                (float)(node.Find("amount")?.AsDouble() ?? 1.0),
                ColorMap(node.Get("colors")))).ToList(),
            ShadePerLevel = (float)height.Double("shadePerLevel"),
            SlopeShade = (float)height.Double("slopeShade"),
            Checker = (float)height.Double("checker"),
            CliffRight = Parse(cliffs.Get("right")),
            CliffLeft = Parse(cliffs.Get("left")),
            Networks = Properties(root.Get("networks")).ToDictionary(
                p => new TypeId(p.Name),
                p => new LineStyle(Parse(p.Node.Get("color")), (float)p.Node.Double("width"))),
            Sites = Properties(root.Get("sites")).ToDictionary(p => new TypeId(p.Name), p => Site(p.Node)),
            DefaultSite = Site(root.Get("defaultSite")),
            Parts = root.Find("parts") is { } parts
                ? Properties(parts).ToDictionary(p => new TypeId(p.Name), p => Site(p.Node))
                : new Dictionary<TypeId, SiteStyle>(),
            Highlight = Parse(root.Get("highlight")),
            Rail = new RailStyle(
                Parse(rail.Get("bridgeDeck")), Parse(rail.Get("bridgeEdge")), Parse(rail.Get("bridgePier")), rail.Int("liftLevels"),
                Parse(rail.Get("signalPost")), Parse(rail.Get("signalGreen")), Parse(rail.Get("signalRed")),
                Parse(rail.Get("crossingOpen")), Parse(rail.Get("crossingClosed")), Parse(rail.Get("wreck"))),
        };
    }

    private static SiteStyle Site(JsonNode node) => new(Parse(node.Get("fill")), Parse(node.Get("outline")));

    private static Dictionary<TypeId, Color> ColorMap(JsonNode node) =>
        Properties(node).ToDictionary(p => new TypeId(p.Name), p => Parse(p.Node));

    private static IEnumerable<(string Name, JsonNode Node)> Properties(JsonNode node)
    {
        if (node.Element.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw node.Error("ожидался объект { ... }");
        return node.Element.EnumerateObject()
            .Select(p => (p.Name, new JsonNode(p.Value, $"{node.Location}.{p.Name}")))
            .ToList();
    }

    private static Color Parse(JsonNode node)
    {
        var text = node.AsString();
        return Color.HtmlIsValid(text) ? Color.FromHtml(text) : throw node.Error($"некорректный цвет «{text}»");
    }
}
