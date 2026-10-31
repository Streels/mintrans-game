using Mintrans.Core.Geometry;
using Mintrans.Core.Ids;

namespace Mintrans.World.Spatial;

/// <summary>
/// Объект мира с геометрией (docs/02в): вид, высотный уровень и физический габарит.
/// Уровень 0 — земля, выше — мост, ниже — тоннель. Что это за объект для транспорта и экономики, знают их модули по номеру.
/// </summary>
public sealed record SpatialObject(ObjectId Id, TypeId Kind, int Level, SpatialShape Shape);

/// <summary>Решение правила о паре объектов, чьи клетки совпали.</summary>
public enum ConflictRuling
{
    /// <summary>Правило не знает эту пару: решает следующее правило или точная геометрия.</summary>
    Undecided,

    /// <summary>Сочетание явно разрешено (разные уровни, переезд) — геометрию не проверяем.</summary>
    Allowed,

    /// <summary>Сочетание запрещено, даже если контуры не пересеклись.</summary>
    Forbidden,
}

/// <summary>
/// Явное правило инфраструктуры о паре объектов (docs/02в, раздел 3, пункт 5): например, переезд дороги с рельсами
/// или пересечение на разных уровнях. Правила спрашиваются по порядку; если никто не решил — проверяется геометрия.
/// </summary>
public interface IConflictRule
{
    ConflictRuling Judge(SpatialObject candidate, SpatialObject existing);
}

/// <summary>
/// Объекты на разных уровнях не мешают друг другу в плане. Просвет моста и его тип уточняются отдельно (docs/02в);
/// разрешить уровень — забота правил моста и тоннеля при их постройке.
/// </summary>
public sealed class LevelSeparationRule : IConflictRule
{
    public ConflictRuling Judge(SpatialObject candidate, SpatialObject existing) =>
        candidate.Level != existing.Level ? ConflictRuling.Allowed : ConflictRuling.Undecided;
}

/// <summary>Правило местности: что под габаритом объекта мешает его построить. Пусто — можно.</summary>
public interface ITerrainRule
{
    string? Check(SpatialObject candidate, ITerrainField terrain);
}

/// <summary>
/// Запрещённые виды природы под объектами земли (уровень 0): например, река и озеро. Над ними строят мостом.
/// </summary>
/// <param name="Category">Природная категория, например <c>water</c>.</param>
/// <param name="Kinds">Запрещённые виды этой категории.</param>
/// <param name="StepM">Шаг проверочных точек вдоль габарита.</param>
public sealed record ForbiddenTerrainRule(TypeId Category, IReadOnlyCollection<TypeId> Kinds, double StepM = 5) : ITerrainRule
{
    public string? Check(SpatialObject candidate, ITerrainField terrain)
    {
        if (candidate.Level != 0)
            return null;
        foreach (var point in TerrainProbe.Points(candidate.Shape, StepM))
            if (terrain.Sample(point).KindOf(Category) is { } kind && Kinds.Contains(kind))
                return $"в точке {point} — {kind}";
        return null;
    }
}

/// <summary>
/// Наибольший уклон местности вдоль оси коридора на земле, доли: 0,012 — 12 ‰. Численное значение для железной дороги —
/// отдельный вопрос баланса (docs/02в, раздел 5); насыпи и выемки пока не моделируются.
/// </summary>
public sealed record MaxGradeRule(TypeId Kind, double MaxGrade, double StepM = 10) : ITerrainRule
{
    public string? Check(SpatialObject candidate, ITerrainField terrain)
    {
        if (candidate.Level != 0 || candidate.Kind != Kind || candidate.Shape is not CorridorShape corridor)
            return null;
        var axis = corridor.Axis;
        var count = Math.Max(1, (int)Math.Ceiling(axis.Length / StepM));
        var previous = terrain.Sample(axis.PointAt(0)).HeightM;
        for (var i = 1; i <= count; i++)
        {
            var height = terrain.Sample(axis.PointAt(axis.Length * i / count)).HeightM;
            if (Math.Abs(height - previous) / (axis.Length / count) > MaxGrade + 1e-12)
                return $"уклон круче {MaxGrade * 1000:0.#} ‰ около {axis.PointAt(axis.Length * i / count)}";
            previous = height;
        }
        return null;
    }
}
