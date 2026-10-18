using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World.Layers;

namespace Mintrans.World.Facilities;

/// <summary>Идентификатор транспортного сооружения.</summary>
public readonly record struct FacilityId(int Value)
{
    public override string ToString() => $"facility#{Value}";
}

/// <summary>
/// Отдельное транспортное сооружение (docs/10): ж/д депо, автогараж, городской грузовой пункт.
/// В отличие от станции предприятия, не входит в площадку объекта.
/// </summary>
/// <param name="Entrance">Сторона въезда (для депо и гаража) или null.</param>
/// <param name="Owner">Объект, которому служит сооружение (город для городского пункта) или <see cref="SiteId.None"/>.</param>
public sealed record Facility(FacilityId Id, TypeId Kind, string Name, GridRect Area, Direction? Entrance, SiteId Owner);

/// <summary>
/// Все сооружения карты и их слой занятости. Сооружения не пересекаются между собой;
/// можно ли ставить их на площадки объектов и на сети, решают правила строительства.
/// </summary>
public sealed class FacilityRegistry
{
    private readonly Dictionary<FacilityId, Facility> _byId = new();
    private readonly CellLayer<int> _occupancy;
    private readonly Dictionary<TypeId, int> _counters = new();
    private int _nextId = 1;

    public FacilityRegistry(int width, int height)
    {
        _occupancy = new CellLayer<int>(width, height);
    }

    public IEnumerable<Facility> All => _byId.Values.OrderBy(f => f.Id.Value);
    public int Revision { get; private set; }

    public Facility this[FacilityId id] => _byId[id];

    public bool TryGet(FacilityId id, out Facility facility) => _byId.TryGetValue(id, out facility!);

    public Facility? At(GridPos pos)
    {
        var id = _occupancy[pos];
        return id == 0 ? null : _byId[new FacilityId(id)];
    }

    public bool IsFree(GridRect area) =>
        _occupancy.Bounds.Contains(area) && area.Cells().All(cell => _occupancy[cell] == 0);

    /// <summary>Добавить сооружение. Имя получает порядковый номер в своём виде: «Ж/д депо №2».</summary>
    public Facility Add(TypeId kind, string title, GridRect area, Direction? entrance, SiteId owner)
    {
        if (!IsFree(area))
            throw new InvalidOperationException($"Клетки {area} заняты другим сооружением или вне карты.");
        var number = _counters[kind] = _counters.GetValueOrDefault(kind) + 1;
        var facility = new Facility(new FacilityId(_nextId++), kind, $"{title} №{number}", area, entrance, owner);
        _byId.Add(facility.Id, facility);
        foreach (var cell in area.Cells())
            _occupancy[cell] = facility.Id.Value;
        Revision++;
        return facility;
    }

    public void Remove(FacilityId id)
    {
        if (!_byId.Remove(id, out var facility))
            return;
        foreach (var cell in facility.Area.Cells())
            _occupancy[cell] = 0;
        Revision++;
    }
}
