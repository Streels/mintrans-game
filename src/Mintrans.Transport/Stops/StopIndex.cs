using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Transport.Definitions;
using Mintrans.World;
using Mintrans.Core.Geometry;
using Mintrans.Core.Spatial;
using Mintrans.World.Rail;

namespace Mintrans.Transport.Stops;

/// <summary>
/// Постоянная ссылка на пункт погрузки: станция предприятия (по ключу объекта и углу станции)
/// или отдельное сооружение (по номеру). Переживает перестройку индекса.
/// </summary>
public readonly record struct StopRef(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Пункт погрузки и выгрузки: клетки на сети и объект, чей склад он обслуживает
/// (шахта, завод — для своих станций; город — для городского пункта).
/// Ж/д станция — платформы предприятия на путях (docs/10в, раздел 4): клетки под ними и их номера.
/// </summary>
/// <param name="Area">Охватывающий прямоугольник клеток пункта.</param>
/// <param name="Slots">Сколько единиц техники пункт обслуживает одновременно; у ж/д станции — число её платформ (docs/10).</param>
/// <param name="PlatformLength">Длина платформы вдоль путей в клетках; null — длинная сторона пункта.</param>
public sealed record Stop(StopRef Ref, string Name, TypeId Network, GridRect Area, SiteId Owner, int Slots = 1, int? PlatformLength = null)
{
    /// <summary>Длина пункта в клетках — по ней видно, помещается ли состав у платформы целиком.</summary>
    public int Length => PlatformLength ?? Math.Max(Area.Width, Area.Height);

    /// <summary>Клетки пункта, если он не заполняет весь прямоугольник (платформы на путях).</summary>
    public IReadOnlySet<GridPos>? Footprint { get; init; }

    /// <summary>Платформы ж/д станции.</summary>
    public IReadOnlyList<RailBerthId> Berths { get; init; } = [];

    public bool Contains(GridPos cell) => Footprint?.Contains(cell) ?? Area.Contains(cell);

    public IEnumerable<GridPos> Cells => Footprint ?? (IEnumerable<GridPos>)Area.Cells();
}

/// <summary>
/// Все пункты на карте. Не хранит своих данных: собирается из станций предприятий и сооружений-пунктов
/// и пересобирается, когда они меняются.
/// </summary>
public sealed class StopIndex(WorldMap map, TransportCatalog catalog)
{
    private readonly List<Stop> _stops = new();
    private int _builtRevision = -1;

    public IReadOnlyList<Stop> All
    {
        get
        {
            Refresh();
            return _stops;
        }
    }

    public Stop? Find(StopRef reference) => All.FirstOrDefault(s => s.Ref == reference);

    public Stop? At(GridPos cell, TypeId network) => All.FirstOrDefault(s => s.Network == network && s.Contains(cell));

    private void Refresh()
    {
        var revision = (map.Sites.Revision * 31 + map.Facilities.Revision) * 31 + (map.Rails?.Revision ?? 0);
        if (revision == _builtRevision)
            return;
        _stops.Clear();
        foreach (var network in catalog.Networks)
        {
            if (network.Trains)
            {
                AddStations(network);
                continue;
            }
            foreach (var site in map.Sites.All)
            {
                foreach (var part in site.Parts.Where(p => network.StopParts.Contains(p.Kind)))
                {
                    var reference = new StopRef($"part:{site.Key}:{part.Area.X},{part.Area.Y}");
                    _stops.Add(new Stop(reference, site.Name, network.Network, part.Area, site.Id,
                        network.SlotsOf(part.Kind), Math.Max(part.Area.Width, part.Area.Height)));
                }
            }
            foreach (var facility in map.Facilities.All.Where(f => network.StopFacilities.Contains(f.Kind)))
            {
                var owner = map.Sites.All.FirstOrDefault(s => s.Id == facility.Owner);
                var name = owner is null ? facility.Name : $"{owner.Name} ({facility.Name})";
                _stops.Add(new Stop(new StopRef($"facility:{facility.Id.Value}"), name, network.Network, facility.Area, facility.Owner,
                    network.SlotsOf(facility.Kind)));
            }
        }
        _builtRevision = revision;
    }

    /// <summary>
    /// Ж/д станции (docs/10в, раздел 4): все платформы предприятия — одна станция, по составу на платформу.
    /// Длина станции — самая короткая платформа: по ней видно, поместится ли состав.
    /// </summary>
    private void AddStations(NetworkTransport network)
    {
        if (map.Rails is not { } rails || rails.Settings.Kind != network.Network)
            return;
        foreach (var group in rails.Berths.Where(b => b.Kind == BerthKind.Platform && !b.Site.IsNone).GroupBy(b => b.Site))
        {
            if (map.Sites.All.FirstOrDefault(s => s.Id == group.Key) is not { } site)
                continue;
            var berths = group.ToList();
            var cells = new HashSet<GridPos>();
            foreach (var berth in berths)
            {
                var geometry = rails.Edge(berth.Edge).Geometry;
                var segment = new PathGeometry(new StraightPrimitive(geometry.PointAt(berth.FromM), geometry.PointAt(berth.ToM)));
                cells.UnionWith(new CorridorShape(segment, rails.Settings.HalfWidthM).TouchedCells().Where(map.Contains));
            }
            var (x0, y0) = (cells.Min(c => c.X), cells.Min(c => c.Y));
            var area = new GridRect(x0, y0, cells.Max(c => c.X) - x0 + 1, cells.Max(c => c.Y) - y0 + 1);
            var length = (int)Math.Round(berths.Min(b => b.LengthM) / SpatialScale.CellM);
            _stops.Add(new Stop(new StopRef($"rail:{site.Key}"), site.Name, network.Network, area, site.Id, berths.Count, length)
            {
                Footprint = cells,
                Berths = berths.Select(b => b.Id).ToList(),
            });
        }
    }
}
