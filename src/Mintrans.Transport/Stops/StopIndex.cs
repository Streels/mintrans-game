using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Transport.Definitions;
using Mintrans.World;
using Mintrans.World.Networks;

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
/// </summary>
/// <param name="Slots">Сколько единиц техники пункт обслуживает одновременно; у ж/д станции — число её путей (docs/10).</param>
/// <param name="PlatformLength">Длина платформы вдоль путей; null — длинная сторона пункта.</param>
public sealed record Stop(StopRef Ref, string Name, TypeId Network, GridRect Area, SiteId Owner, int Slots = 1, int? PlatformLength = null)
{
    /// <summary>Длина пункта в клетках — по ней видно, помещается ли состав у платформы целиком.</summary>
    public int Length => PlatformLength ?? Math.Max(Area.Width, Area.Height);

    public bool Contains(GridPos cell) => Area.Contains(cell);
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
        var revision = map.Sites.Revision * 31 + map.Facilities.Revision;
        if (revision == _builtRevision)
            return;
        _stops.Clear();
        foreach (var network in catalog.Networks)
        {
            foreach (var site in map.Sites.All)
            {
                foreach (var part in site.Parts.Where(p => network.StopParts.Contains(p.Kind)))
                {
                    var reference = new StopRef($"part:{site.Key}:{part.Area.X},{part.Area.Y}");
                    var (slots, length) = network.Trains
                        ? Platform(part.Area, network.Network)
                        : (network.SlotsOf(part.Kind), Math.Max(part.Area.Width, part.Area.Height));
                    _stops.Add(new Stop(reference, site.Name, network.Network, part.Area, site.Id, slots, length));
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

    /// <summary>Ж/д станция: пути идут вдоль уложенных прямых; сколько их и какой длины платформа.</summary>
    private (int Tracks, int Length) Platform(GridRect area, TypeId network)
    {
        var alongY = map.Networks[network] is TrackNetworkLayer rails && (rails.PiecesAt(area.Origin) & TrackPieces.NorthSouth) != 0;
        return alongY ? (area.Width, area.Height) : (area.Height, area.Width);
    }
}
