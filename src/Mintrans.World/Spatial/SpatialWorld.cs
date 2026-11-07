using Mintrans.Core.Geometry;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;

namespace Mintrans.World.Spatial;

/// <summary>Почему изменение нельзя применить.</summary>
/// <param name="Subject">Объект изменения, к которому относится причина.</param>
/// <param name="Other">Существующий объект, с которым конфликт, если есть.</param>
public sealed record SpatialIssue(ObjectId Subject, ObjectId? Other, string Reason)
{
    public override string ToString() => Other is { } other ? $"{Subject}: {Reason} ({other})" : $"{Subject}: {Reason}";
}

/// <summary>
/// Изменение геометрии мира (docs/02в, раздел 4): что добавить, убрать, заменить. Само по себе ничего не меняет:
/// мир проверяет его предпросмотром и применяет целиком или не применяет вовсе.
/// Номера новых объектов известны сразу — по порядку от следующего свободного номера мира.
/// </summary>
public sealed class SpatialChange
{
    private readonly SpatialWorld _world;
    private readonly List<SpatialObject> _added = [];
    private readonly SortedSet<ObjectId> _removed = [];
    private readonly List<(ObjectId A, ObjectId B, IReadOnlyList<PathInterval> IgnoreA, IReadOnlyList<PathInterval> IgnoreB)> _joints = [];

    internal SpatialChange(SpatialWorld world)
    {
        _world = world;
        BaseRevision = world.Revision;
    }

    /// <summary>Ревизия мира, для которой собрано изменение: если мир успел измениться, применять нельзя.</summary>
    public int BaseRevision { get; }

    public IReadOnlyList<SpatialObject> Added => _added;
    public IReadOnlySet<ObjectId> Removed => _removed;

    public bool IsEmpty => _added.Count == 0 && _removed.Count == 0;

    public ObjectId Add(TypeId kind, int level, SpatialShape shape)
    {
        var id = new ObjectId(_world.NextId.Value + _added.Count(a => !_world.Contains(a.Id)));
        _added.Add(new SpatialObject(id, kind, level, shape));
        return id;
    }

    public void Remove(ObjectId id)
    {
        if (!_world.Contains(id))
            throw new ArgumentException($"В мире нет объекта {id}.", nameof(id));
        _removed.Add(id);
    }

    /// <summary>Заменить геометрию объекта, сохранив номер: старые клетки индекса уйдут, новые вычислятся.</summary>
    public void Replace(ObjectId id, SpatialShape shape)
    {
        var existing = _world.Get(id);
        Remove(id);
        _added.Add(existing with { Shape = shape });
    }

    /// <summary>
    /// Явно разрешить пересечение двух коридоров на участках <paramref name="ignoreA"/> и <paramref name="ignoreB"/> их осей —
    /// общая зона стрелки или стыка путей. Вне этих участков действует обычная точная проверка.
    /// </summary>
    public void AllowJoint(ObjectId a, ObjectId b, IReadOnlyList<PathInterval> ignoreA, IReadOnlyList<PathInterval> ignoreB) =>
        _joints.Add((a, b, ignoreA, ignoreB));

    private readonly HashSet<ObjectId> _verified = [];

    /// <summary>
    /// Объект повторяет уже проверенную геометрию (часть разделённого пути): его не сверяют с построенным,
    /// местностью и другими такими же частями — только с действительно новыми объектами изменения.
    /// </summary>
    public void MarkVerified(ObjectId id) => _verified.Add(id);

    public bool IsVerified(ObjectId id) => _verified.Contains(id);

    internal (IReadOnlyList<PathInterval>? A, IReadOnlyList<PathInterval>? B)? JointOf(ObjectId a, ObjectId b)
    {
        foreach (var joint in _joints)
        {
            if (joint.A == a && joint.B == b)
                return (joint.IgnoreA, joint.IgnoreB);
            if (joint.A == b && joint.B == a)
                return (joint.IgnoreB, joint.IgnoreA);
        }
        return null;
    }
}

/// <summary>Результат проверки или применения изменения.</summary>
public sealed class SpatialPreview
{
    internal SpatialPreview(IReadOnlyDictionary<ObjectId, IReadOnlyCollection<GridPos>> cells, IReadOnlyList<SpatialIssue> issues)
    {
        Cells = cells;
        Issues = issues;
    }

    /// <summary>Клетки, которые займут новые и изменённые объекты.</summary>
    public IReadOnlyDictionary<ObjectId, IReadOnlyCollection<GridPos>> Cells { get; }

    public IReadOnlyList<SpatialIssue> Issues { get; }

    public bool IsValid => Issues.Count == 0;

    /// <summary>Применено ли изменение к миру (только у результата <see cref="SpatialWorld.Commit"/>).</summary>
    public bool Committed { get; internal set; }

    /// <summary>Чанки, где изменилась занятость: их и только их перерисовывает вид и перепроверяет стройка.</summary>
    public IReadOnlySet<ChunkPos> TouchedChunks { get; internal set; } = new HashSet<ChunkPos>();
}

/// <summary>
/// Пространственный мир (docs/02в): исходная геометрия объектов, производный разреженный индекс клеток, местность
/// и правила пересечений. Любое изменение проходит путь «предпросмотр → клетки → местность → точные конфликты → применение»
/// и применяется атомарно. Границы мира — в метрах, из настроек сценария.
/// </summary>
public sealed class SpatialWorld
{
    private readonly SortedDictionary<ObjectId, SpatialObject> _objects = new();

    public SpatialWorld(WorldBounds bounds, ITerrainField terrain,
        IEnumerable<IConflictRule>? conflictRules = null, IEnumerable<ITerrainRule>? terrainRules = null)
    {
        Bounds = bounds;
        Terrain = terrain;
        ConflictRules = conflictRules?.ToList() ?? [new LevelSeparationRule()];
        TerrainRules = terrainRules?.ToList() ?? [];
    }

    /// <summary>Границы мира в метрах — параметр сценария (прототип 40×40 км, проверочный вариант 100×100 км, docs/02в).</summary>
    public WorldBounds Bounds { get; private set; }

    public ITerrainField Terrain { get; }
    public IReadOnlyList<IConflictRule> ConflictRules { get; }
    public IReadOnlyList<ITerrainRule> TerrainRules { get; }
    public SparseCellIndex Index { get; } = new();

    /// <summary>Растёт с каждым применённым изменением.</summary>
    public int Revision { get; private set; }

    public ObjectId NextId { get; private set; } = new(1);

    public IEnumerable<SpatialObject> Objects => _objects.Values;
    public int Count => _objects.Count;

    public bool Contains(ObjectId id) => _objects.ContainsKey(id);

    public SpatialObject Get(ObjectId id) =>
        _objects.TryGetValue(id, out var obj) ? obj : throw new KeyNotFoundException($"В мире нет объекта {id}.");

    /// <summary>Объекты в клетке на уровне <paramref name="level"/> (или на всех уровнях).</summary>
    public IEnumerable<SpatialObject> At(GridPos cell, int? level = null) =>
        Index.At(cell).Select(Get).Where(o => level is null || o.Level == level);

    /// <summary>Объекты, чей габарит действительно пересекает <paramref name="shape"/> на уровне <paramref name="level"/>.</summary>
    public IEnumerable<SpatialObject> Overlapping(SpatialShape shape, int level) =>
        Index.Candidates(shape.TouchedCells()).Select(Get)
            .Where(o => o.Level == level && ShapeConflicts.Conflict(shape, o.Shape));

    public SpatialChange Begin() => new(this);

    /// <summary>
    /// Расширить границы мира (docs/02в): начало координат не сдвигается, номера и геометрия объектов не меняются,
    /// новые клетки появятся только под будущими постройками. Сузить границы нельзя.
    /// </summary>
    public void ExpandBounds(WorldBounds bounds)
    {
        if (!bounds.Contains(Bounds))
            throw new ArgumentException($"Новые границы {bounds} должны включать прежние {Bounds}.", nameof(bounds));
        Bounds = bounds;
        Revision++;
    }

    /// <summary>Проверить изменение, ничего не меняя в мире.</summary>
    public SpatialPreview Preview(SpatialChange change)
    {
        var issues = new List<SpatialIssue>();
        if (change.BaseRevision != Revision)
            issues.Add(new SpatialIssue(default, null, "мир изменился после начала постройки — повторите предпросмотр"));
        var cells = new Dictionary<ObjectId, IReadOnlyCollection<GridPos>>();
        var added = change.Added;
        for (var i = 0; i < added.Count; i++)
        {
            var candidate = added[i];
            cells[candidate.Id] = candidate.Shape.TouchedCells();
            if (!Bounds.Contains(candidate.Shape.Bounds, SpatialScale.EpsilonM))
            {
                issues.Add(new SpatialIssue(candidate.Id, null, "выходит за границу карты"));
                continue;
            }
            var verified = change.IsVerified(candidate.Id);
            if (!verified)
            {
                foreach (var rule in TerrainRules)
                    if (rule.Check(candidate, Terrain) is { } reason)
                        issues.Add(new SpatialIssue(candidate.Id, null, reason));
                foreach (var otherId in Index.Candidates(cells[candidate.Id]))
                {
                    if (change.Removed.Contains(otherId))
                        continue;
                    if (Conflicts(change, candidate, Get(otherId)))
                        issues.Add(new SpatialIssue(candidate.Id, otherId, "пересекается с построенным"));
                }
            }
            for (var k = 0; k < i; k++)
                if (!(verified && change.IsVerified(added[k].Id)) && Conflicts(change, candidate, added[k]))
                    issues.Add(new SpatialIssue(candidate.Id, added[k].Id, "пересекается с другой частью постройки"));
        }
        return new SpatialPreview(cells, issues);
    }

    private bool Conflicts(SpatialChange change, SpatialObject candidate, SpatialObject existing)
    {
        foreach (var rule in ConflictRules)
        {
            switch (rule.Judge(candidate, existing))
            {
                case ConflictRuling.Allowed:
                    return false;
                case ConflictRuling.Forbidden:
                    return true;
            }
        }
        var joint = change.JointOf(candidate.Id, existing.Id);
        return ShapeConflicts.Conflict(candidate.Shape, existing.Shape, joint?.A, joint?.B);
    }

    /// <summary>
    /// Применить изменение целиком: сначала полная проверка, затем удаление старых ссылок индекса и вставка новых.
    /// Если проверка не прошла, мир не меняется.
    /// </summary>
    public SpatialPreview Commit(SpatialChange change)
    {
        var preview = Preview(change);
        if (!preview.IsValid)
            return preview;
        var touched = new HashSet<ChunkPos>();
        foreach (var id in change.Removed)
        {
            touched.UnionWith(Index.CellsOf(id).Select(SpatialScale.ToChunk));
            Index.Remove(id);
            _objects.Remove(id);
        }
        foreach (var obj in change.Added)
        {
            _objects[obj.Id] = obj;
            Index.Add(obj.Id, preview.Cells[obj.Id]);
            touched.UnionWith(preview.Cells[obj.Id].Select(SpatialScale.ToChunk));
            if (obj.Id.Value >= NextId.Value)
                NextId = new ObjectId(obj.Id.Value + 1);
        }
        Revision++;
        preview.Committed = true;
        preview.TouchedChunks = touched;
        return preview;
    }

    /// <summary>Пересоздать индекс из исходной геометрии: индекс производный и не обязателен в сохранении.</summary>
    public void RebuildIndex()
    {
        Index.Clear();
        foreach (var obj in _objects.Values)
            Index.Add(obj.Id, obj.Shape.TouchedCells());
    }
}
