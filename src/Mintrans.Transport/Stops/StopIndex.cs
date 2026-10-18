using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Transport.Definitions;
using Mintrans.World;

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
public sealed record Stop(StopRef Ref, string Name, TypeId Network, GridRect Area, SiteId Owner)
{
    /// <summary>Длина пункта в клетках — по ней проверяется, помещается ли состав.</summary>
    public int Length => Math.Max(Area.Width, Area.Height);

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
                    _stops.Add(new Stop(reference, site.Name, network.Network, part.Area, site.Id));
                }
            }
            foreach (var facility in map.Facilities.All.Where(f => network.StopFacilities.Contains(f.Kind)))
            {
                var owner = map.Sites.All.FirstOrDefault(s => s.Id == facility.Owner);
                var name = owner is null ? facility.Name : $"{owner.Name} ({facility.Name})";
                _stops.Add(new Stop(new StopRef($"facility:{facility.Id.Value}"), name, network.Network, facility.Area, facility.Owner));
            }
        }
        _builtRevision = revision;
    }
}
