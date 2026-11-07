using System.Diagnostics;
using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;
using Mintrans.Economy.Systems;
using Mintrans.Simulation;
using Mintrans.Transport.Runtime;
using Mintrans.Transport.Systems;
using Mintrans.World;
using Mintrans.World.Rail;
using Mintrans.World.Sites;
using Xunit.Abstractions;

namespace Mintrans.Data.Tests;

/// <summary>
/// Нагрузочный тест: запускается только с переменной окружения <c>MINTRANS_LOAD=1</c>, в обычный прогон не входит —
/// он долгий и меряет время, а не правила.
/// </summary>
public sealed class LoadFactAttribute : FactAttribute
{
    public LoadFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MINTRANS_LOAD") != "1")
            Skip = "нагрузочный тест: запустите с MINTRANS_LOAD=1";
    }
}

/// <summary>
/// Проверка производительности на карте 100×100 км (docs/02, docs/02в, раздел 9): 20 000×20 000 клеток, 100 линий
/// по 20 км — 2 000 км пути с платформами, депо и маршрутными сигналами через 2 км, 100 поездов и 200 предприятий.
/// Меряет постройку, память, предпросмотр соединения на заполненной карте и шаг симуляции.
/// </summary>
public class LoadTests(ITestOutputHelper output)
{
    private const int Size = 20_000;
    private const int Rows = 25, PerRow = 4;
    private const int LineCells = 4_000;

    private static WorldPoint C(int x, int y) => SpatialScale.CellCenter(new GridPos(x, y));

    [LoadFact]
    public void Hundred_kilometres_with_two_thousand_kilometres_of_track_and_a_hundred_trains()
    {
        var content = ContentLocator.Load();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(true);
        var watch = Stopwatch.StartNew();

        var map = new WorldMap(Size, Size, 0, content.Terrain, content.Networks, content.RailSettings, terrainStep: 50);
        // Линии: шахта у западного конца, завод у восточного, путь по y между ними. Площадки — до подключения экономики.
        var placement = SitePlacement.Basic();
        var layout = new List<(int X0, int X1, int Y, Site Mine, Site Plant)>();
        for (var row = 0; row < Rows; row++)
        for (var col = 0; col < PerRow; col++)
        {
            var y = 600 + row * 760;
            var x0 = 400 + col * (LineCells + 900);
            var x1 = x0 + LineCells;
            var key = $"{row}_{col}";
            var mine = placement.Place(map, new NewSiteRequest($"mine_{key}", "coal_mine", $"Шахта {key}", new GridRect(x0 + 20, y - 43, 40, 40)));
            var plant = placement.Place(map, new NewSiteRequest($"plant_{key}", "steel_plant", $"Завод {key}", new GridRect(x1 - 60, y - 43, 40, 40)));
            layout.Add((x0, x1, y, mine, plant));
        }
        var simulation = new GameSimulation(new SimulationState(map));
        var economy = EconomyModule.Install(simulation, content.Economy);
        var transport = TransportModule.Install(simulation, content.Transport);
        var builder = new Builder(map, content.Construction);
        map.RequireRails().Rules.Add(TransportModule.RailGuard(transport));
        Report($"карта 100×100 км и {layout.Count * 2} предприятий", watch, memoryBefore);

        var lines = new List<(Site Mine, Site Plant, World.Facilities.Facility Depot)>();
        watch.Restart();
        var build = new List<double>();
        foreach (var (x0, x1, y, mine, plant) in layout)
        {
            economy.Seed(mine.Id, "coal", Economy.Mass.FromTonnes(1000));
            var one = Stopwatch.StartNew();
            Built(builder.BuildRail(C(x0, y), C(x1, y)));
            build.Add(one.Elapsed.TotalMilliseconds);
            Built(builder.BuildPlatform("rail_station", C(x0 + 25, y), C(x0 + 34, y)));
            Built(builder.BuildPlatform("rail_station", C(x1 - 55, y), C(x1 - 46, y)));
            var depot = builder.BuildRailDepot("rail_depot", C(x0 + 5, y), C(x0 + 8, y));
            Built(depot);
            for (var x = x0 + 400; x < x1 - 300; x += 400)
                Built(builder.BuildRailSignal("path_signal", C(x, y), C(x, y)));
            lines.Add((mine, plant, depot.Built!));
        }
        var rails = map.RequireRails();
        var trackKm = rails.Edges.Sum(e => e.LengthM) / 1000;
        Report($"постройка: {lines.Count} линий, {trackKm:0} км пути, {rails.Edges.Count()} путей, {rails.Signals.Count()} сигналов, " +
               $"{rails.World.Index.ChunkCount} чанков индекса, {rails.World.Index.CellCount} клеток индекса; " +
               $"одна линия 20 км — {build.Average():0} мс в среднем", watch, memoryBefore);

        // Предпросмотр соединения на заполненной карте: от конца первой линии к свободной точке вдали.
        watch.Restart();
        var preview = builder.PlanRail(C(400 + LineCells, 600), C(400 + LineCells + 600, 600 + 300));
        Report($"предпросмотр соединения А→Б (вариантов: {preview.Radii.Sum(r => r.Variants.Count)})", watch, memoryBefore);

        // По поезду на линию: паровоз Л с 12 полувагонами, уголь с шахты на завод.
        foreach (var (mine, plant, depot) in lines)
        {
            var train = transport.Buy(depot.Id, "steam_l");
            for (var i = 0; i < 12; i++)
                transport.AddWagon(train.Id, "gondola");
            var a = transport.Stops.All.First(s => s.Owner == mine.Id && s.Network == "rail").Ref;
            var b = transport.Stops.All.First(s => s.Owner == plant.Id && s.Network == "rail").Ref;
            transport.AssignRoute(train.Id, a, b);
        }

        // Месяц игрового времени, по часам: 20 км поезд проходит примерно за 18 суток.
        watch.Restart();
        var hours = new List<double>();
        for (var hour = 0; hour < 24 * 30; hour++)
        {
            var one = Stopwatch.StartNew();
            simulation.Run(1);
            hours.Add(one.Elapsed.TotalMilliseconds);
        }
        var sorted = hours.Order().ToList();
        Report($"симуляция: 720 ч, шаг в среднем {hours.Average():0.0} мс, медиана {sorted[sorted.Count / 2]:0.0} мс, " +
               $"худший {sorted[^1]:0} мс", watch, memoryBefore);

        var states = transport.Vehicles.GroupBy(v => v.State).Select(g => $"{g.Key}: {g.Count()}");
        output.WriteLine($"Поезда через месяц: {string.Join(", ", states)}");
        var delivered = economy.Sites.Where(s => s.Definition.SiteType.Value == "steel_plant")
            .Sum(s => s.History.Append(s.Today).Sum(d => d.Received.GetValueOrDefault("coal").Kg)) / 1000.0;
        output.WriteLine($"Угля доставлено на заводы: {delivered:0} т");

        Assert.DoesNotContain(transport.Vehicles, v => v.State is VehicleState.Stuck or VehicleState.Crashed);
        Assert.True(delivered > 0, "поезда не довезли уголь");
        // Границы — с запасом: тест ловит деградацию на порядок, а не мерит точное время.
        Assert.True(hours.Average() < 100, $"шаг симуляции {hours.Average():0} мс");
        Assert.True(build.Average() < 2_000, $"постройка линии {build.Average():0} мс");
    }

    private void Report(string what, Stopwatch watch, long memoryBefore)
    {
        var memory = (GC.GetTotalMemory(false) - memoryBefore) / 1024.0 / 1024.0;
        output.WriteLine($"{what}: {watch.Elapsed.TotalSeconds:0.00} с, память +{memory:0} МБ");
    }

    private static void Built(RailEditPreview? result) =>
        Assert.True(result is { Committed: true }, result is null ? "нет соединения" : string.Join("; ", result.Issues));

    private static void Built(RailPlan plan) =>
        Assert.True(plan.Result is { Committed: true }, string.Join("; ", plan.Problems.Select(p => p.Message)));
}
