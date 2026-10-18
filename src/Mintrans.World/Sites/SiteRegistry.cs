using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World.Layers;

namespace Mintrans.World.Sites;

/// <summary>
/// Хранилище объектов карты и слой занятости клеток.
/// Гарантирует: площадка целиком внутри карты и клетки не делятся между объектами.
/// Прочие правила размещения проверяет <see cref="SitePlacement"/> до изменения.
/// </summary>
public sealed class SiteRegistry
{
    private readonly Dictionary<SiteId, Site> _byId = new();
    private readonly Dictionary<string, Site> _byKey = new();
    private readonly List<Site> _ordered = new();
    private readonly CellLayer<int> _occupancy;
    private int _nextId = 1;

    public SiteRegistry(int width, int height)
    {
        _occupancy = new CellLayer<int>(width, height);
    }

    public IReadOnlyList<Site> All => _ordered;
    public int Revision => _occupancy.Revision;

    public Site this[SiteId id] => _byId[id];

    public bool TryGetByKey(string key, out Site site) => _byKey.TryGetValue(key, out site!);

    public Site GetByKey(string key) =>
        TryGetByKey(key, out var site) ? site : throw new KeyNotFoundException($"Нет объекта «{key}».");

    /// <summary>Объект в клетке или null.</summary>
    public Site? At(GridPos pos)
    {
        var id = _occupancy[pos];
        return id == 0 ? null : _byId[new SiteId(id)];
    }

    public bool IsFree(GridRect rect) =>
        _occupancy.Bounds.Contains(rect) && rect.Cells().All(cell => _occupancy[cell] == 0);

    internal Site Add(string key, TypeId type, string name, GridRect baseArea)
    {
        if (_byKey.ContainsKey(key))
            throw new InvalidOperationException($"Объект «{key}» уже есть на карте.");
        RequireFree(key, baseArea);

        var site = new Site(new SiteId(_nextId++), key, type, name, baseArea);
        Occupy(site, baseArea);
        _byId.Add(site.Id, site);
        _byKey.Add(key, site);
        _ordered.Add(site);
        return site;
    }

    internal void Attach(Site site, SitePart part)
    {
        RequireFree(site.Key, part.Area);
        site.Attach(part);
        Occupy(site, part.Area);
    }

    /// <summary>Отсоединить часть площадки (например, снести станцию) и освободить её клетки.</summary>
    public void Detach(Site site, SitePart part)
    {
        if (!site.CanDetach(part))
            throw new InvalidOperationException(
                $"Часть {part.Area} нельзя убрать из «{site.Name}»: это базовая область или на ней держатся другие части.");
        site.Detach(part);
        foreach (var cell in part.Area.Cells())
            _occupancy[cell] = 0;
    }

    private void RequireFree(string key, GridRect area)
    {
        if (!_occupancy.Bounds.Contains(area))
            throw new InvalidOperationException($"Площадка «{key}» {area} выходит за карту.");
        if (!IsFree(area))
            throw new InvalidOperationException($"Клетки {area} для «{key}» уже заняты.");
    }

    private void Occupy(Site site, GridRect area)
    {
        foreach (var cell in area.Cells())
            _occupancy[cell] = site.Id.Value;
    }
}
