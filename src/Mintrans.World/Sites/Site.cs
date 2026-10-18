using Mintrans.Core.Grid;
using Mintrans.Core.Ids;

namespace Mintrans.World.Sites;

/// <summary>
/// Прямоугольная часть площадки объекта. Первая часть — базовая область,
/// остальные присоединены позже (модули предприятия, грузовые станции).
/// Что означает <see cref="Kind"/>, решают экономика и транспорт; карта хранит только занятые клетки.
/// </summary>
public sealed record SitePart(TypeId Kind, GridRect Area);

/// <summary>
/// Объект на карте: город, шахта, завод. Площадка — объединение частей и не обязана быть прямоугольной.
/// Экономику, склады и спрос объекту назначают другие модули по его <see cref="Id"/>.
/// </summary>
public sealed class Site
{
    public static readonly TypeId BasePart = "base";

    private readonly List<SitePart> _parts;

    internal Site(SiteId id, string key, TypeId type, string name, GridRect baseArea)
    {
        Id = id;
        Key = key;
        Type = type;
        Name = name;
        _parts = [new SitePart(BasePart, baseArea)];
    }

    public SiteId Id { get; }

    /// <summary>Постоянное имя из данных карты, например "coal_mine". Не меняется между сохранениями.</summary>
    public string Key { get; }

    public TypeId Type { get; }
    public string Name { get; }
    public IReadOnlyList<SitePart> Parts => _parts;

    /// <summary>Базовая область, с которой объект появился на карте.</summary>
    public GridRect Base => _parts[0].Area;

    /// <summary>Наименьший прямоугольник, содержащий всю площадку.</summary>
    public GridRect Bounds
    {
        get
        {
            var (minX, minY, maxX, maxY) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
            foreach (var part in _parts)
            {
                minX = Math.Min(minX, part.Area.X);
                minY = Math.Min(minY, part.Area.Y);
                maxX = Math.Max(maxX, part.Area.Right);
                maxY = Math.Max(maxY, part.Area.Bottom);
            }
            return new GridRect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public bool Contains(GridPos pos) => _parts.Any(p => p.Area.Contains(pos));

    /// <summary>Часть площадки в клетке или null.</summary>
    public SitePart? PartAt(GridPos pos) => _parts.FirstOrDefault(p => p.Area.Contains(pos));

    public IEnumerable<GridPos> Cells() => _parts.SelectMany(p => p.Area.Cells());

    /// <summary>Примыкает ли прямоугольник к площадке хотя бы одной общей стороной клетки.</summary>
    public bool Touches(GridRect area) =>
        area.Cells().Any(cell => Directions.All.Any(side => Contains(cell.Step(side))));

    internal void Attach(SitePart part) => _parts.Add(part);

    internal void Detach(SitePart part) => _parts.Remove(part);

    /// <summary>
    /// Можно ли убрать часть: базовую область убрать нельзя, а остальные части после этого
    /// должны по-прежнему примыкать к базовой (напрямую или через другие части).
    /// </summary>
    public bool CanDetach(SitePart part)
    {
        if (part == _parts[0] || !_parts.Contains(part))
            return false;
        var remaining = _parts.Where(p => p != part).ToList();
        var connected = new List<SitePart> { remaining[0] };
        var pending = remaining.Skip(1).ToList();
        bool progress;
        do
        {
            progress = false;
            foreach (var candidate in pending.ToList())
            {
                if (connected.Any(c => AreAdjacent(c.Area, candidate.Area)))
                {
                    connected.Add(candidate);
                    pending.Remove(candidate);
                    progress = true;
                }
            }
        } while (progress);
        return pending.Count == 0;
    }

    private static bool AreAdjacent(GridRect a, GridRect b) =>
        a.Cells().Any(cell => Directions.All.Any(side => b.Contains(cell.Step(side))));

    public override string ToString() => $"{Key} ({Type}) {Id}";
}
