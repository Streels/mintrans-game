using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.World.Networks;
using Mintrans.World.Rail;

namespace Mintrans.Data.Tests;

/// <summary>
/// Сквозная проверка docs/01: на карте прототипа игрок может поставить платформы у шахт и завода
/// и связать их железной дорогой с кривыми допустимого радиуса, а грузовики — довезти до города.
/// </summary>
public class PrototypeConstructionTests
{
    private static GridPos P(int x, int y) => new(x, y);

    [Fact]
    public void Player_can_connect_mines_and_plant_by_rail_and_plant_to_city_by_road()
    {
        var content = ContentLocator.Load();
        var map = content.LoadMap("prototype").Map;
        var builder = new Builder(map, content.Construction);

        PrototypeRails.Build(builder);
        Built(builder.BuildStation("truck_station", P(149, 158), P(149, 161))); // слева от завода

        // Сталь: дорога от автостанции завода к улицам города.
        Built(builder.BuildPath("road", P(148, 160), P(80, 160)));
        Built(builder.BuildPath("road", P(80, 160), P(54, 186)));
        Built(builder.BuildPath("road", P(54, 186), P(53, 186)));

        var rails = map.RequireRails();
        RailBerth Platform(string key) => rails.Berths.Single(b => b.Kind == BerthKind.Platform && b.Site == map.Sites.GetByKey(key).Id);
        Assert.True(PrototypeRails.Connected(rails, Platform("coal_mine"), Platform("steel_plant")), "уголь не доезжает до завода");
        Assert.True(PrototypeRails.Connected(rails, Platform("iron_mine"), Platform("steel_plant")), "руда не доезжает до завода");
        Assert.True(NetworkQueries.AreConnected(map.Networks["road"], [P(149, 158)], map.Sites.GetByKey("city").Cells()),
            "грузовики не доезжают до города");
    }

    private static void Built(BuildPlan plan) =>
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => $"{p.Cell}: {p.Message}")));

    private static void Built(StationPlan plan) =>
        Assert.True(plan.CanBuild, string.Join("; ", plan.Problems.Select(p => p.Message)));
}

/// <summary>Параметры железнодорожного редактора v1 читаются из content/construction.json (docs/10в).</summary>
public class RailEditorContentTests
{
    [Fact]
    public void Rail_editor_settings_come_from_content()
    {
        var settings = ContentLocator.Load().Construction.RailEditor;
        Assert.Equal(200, settings.MinRadiusM);
        Assert.Equal([200, 250, 300, 400, 500, 750, 1000], settings.AllowedRadii);
        Assert.Equal(90, settings.MaxArcSweepDeg);
        Assert.Equal(1.35, settings.MaxDefaultDetourRatio);
        Assert.Equal(2.0, settings.CorridorHalfWidthM);
    }
}
