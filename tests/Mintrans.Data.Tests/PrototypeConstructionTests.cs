using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.World.Networks;
using Mintrans.World.Rail;

namespace Mintrans.Data.Tests;

/// <summary>
/// Сквозная проверка docs/01: на карте прототипа 40×40 км игрок может поставить платформы у шахт и завода
/// и связать их железной дорогой с кривыми допустимого радиуса, а грузовики — довезти до города.
/// </summary>
public class PrototypeConstructionTests
{
    [Fact]
    public void Player_can_connect_mines_and_plant_by_rail_and_plant_to_city_by_road()
    {
        var content = ContentLocator.Load();
        var map = content.LoadMap("prototype").Map;
        var builder = new Builder(map, content.Construction);

        PrototypeRails.Build(builder);
        PrototypeRails.BuildRoads(builder);

        var rails = map.RequireRails();
        RailBerth Platform(string key) => rails.Berths.Single(b => b.Kind == BerthKind.Platform && b.Site == map.Sites.GetByKey(key).Id);
        Assert.True(PrototypeRails.Connected(rails, Platform("coal_mine"), Platform("steel_plant")), "уголь не доезжает до завода");
        Assert.True(PrototypeRails.Connected(rails, Platform("iron_mine"), Platform("steel_plant")), "руда не доезжает до завода");
        Assert.True(NetworkQueries.AreConnected(map.Networks["road"], [PrototypeRails.TruckStation], map.Sites.GetByKey("city").Cells()),
            "грузовики не доезжают до города");
    }
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
