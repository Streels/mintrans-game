using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.World.Networks;

namespace Mintrans.Data.Tests;

/// <summary>
/// Сквозная проверка docs/01: на карте прототипа игрок может поставить станции у шахт и завода
/// и связать их железной дорогой, а грузовики — довезти до города.
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

        // Станции: полосы вплотную к площадкам (шахты 8×8, завод 14×12).
        Built(builder.BuildStation("rail_station", P(200, 48), P(207, 48)));   // под угольной шахтой
        Built(builder.BuildStation("rail_station", P(62, 50), P(69, 50)));     // под железорудной
        Built(builder.BuildStation("rail_station", P(150, 149), P(163, 149))); // над заводом
        Built(builder.BuildStation("truck_station", P(149, 158), P(149, 161))); // слева от завода

        // Уголь: от восточного торца станции на юг, затем к восточному торцу заводской станции.
        Built(builder.BuildPath("rail", P(208, 48), P(208, 140)));
        Built(builder.BuildPath("rail", P(208, 140), P(200, 149)));
        Built(builder.BuildPath("rail", P(200, 149), P(164, 149)));

        // Руда: от западного торца станции на юг и к западному торцу заводской станции.
        Built(builder.BuildPath("rail", P(61, 50), P(61, 100)));
        Built(builder.BuildPath("rail", P(61, 100), P(110, 149)));
        Built(builder.BuildPath("rail", P(110, 149), P(149, 149)));

        // Сталь: дорога от автостанции завода к улицам города.
        Built(builder.BuildPath("road", P(148, 160), P(80, 160)));
        Built(builder.BuildPath("road", P(80, 160), P(54, 186)));
        Built(builder.BuildPath("road", P(54, 186), P(53, 186)));

        var rail = map.Networks["rail"];
        var road = map.Networks["road"];
        var plantStation = map.Sites.GetByKey("steel_plant").Parts.First(p => p.Kind.Value == "rail_station").Area.Cells();
        Assert.True(NetworkQueries.AreConnected(rail, [P(200, 48)], plantStation), "уголь не доезжает до завода");
        Assert.True(NetworkQueries.AreConnected(rail, [P(62, 50)], plantStation), "руда не доезжает до завода");
        Assert.True(NetworkQueries.AreConnected(road, [P(149, 158)], map.Sites.GetByKey("city").Cells()),
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
