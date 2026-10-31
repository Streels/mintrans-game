using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.Core.Spatial;

namespace Mintrans.World.Spatial;

/// <summary>
/// Разреженный индекс занятости клеток (docs/02в, раздел 3): чанк → клетка → номера объектов.
/// Записи есть только у клеток, куда заходит чей-то габарит; пустые клетки и чанки удаляются.
/// Индекс производный: его можно целиком пересоздать из геометрии объектов.
/// </summary>
public sealed class SparseCellIndex
{
    private readonly Dictionary<ChunkPos, Dictionary<LocalCell, SortedSet<ObjectId>>> _chunks = new();
    private readonly Dictionary<ObjectId, IReadOnlyCollection<GridPos>> _cellsOf = new();

    /// <summary>Сколько чанков содержат хоть одну запись.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>Сколько клеток содержат хоть одну запись.</summary>
    public int CellCount => _chunks.Values.Sum(c => c.Count);

    public IEnumerable<ChunkPos> Chunks => _chunks.Keys;

    public bool Contains(ObjectId id) => _cellsOf.ContainsKey(id);

    public IReadOnlyCollection<GridPos> CellsOf(ObjectId id) =>
        _cellsOf.TryGetValue(id, out var cells) ? cells : [];

    /// <summary>Объекты, габарит которых заходит в клетку, по возрастанию номера.</summary>
    public IReadOnlyCollection<ObjectId> At(GridPos cell) =>
        _chunks.TryGetValue(SpatialScale.ToChunk(cell), out var chunk) &&
        chunk.TryGetValue(SpatialScale.ToLocal(cell), out var ids)
            ? ids
            : [];

    /// <summary>Кандидаты на пересечение: все объекты в перечисленных клетках, без повторов, по возрастанию номера.</summary>
    public SortedSet<ObjectId> Candidates(IEnumerable<GridPos> cells)
    {
        var result = new SortedSet<ObjectId>();
        foreach (var cell in cells)
            result.UnionWith(At(cell));
        return result;
    }

    public void Add(ObjectId id, IReadOnlyCollection<GridPos> cells)
    {
        if (_cellsOf.ContainsKey(id))
            throw new InvalidOperationException($"Объект {id} уже есть в индексе.");
        _cellsOf[id] = cells;
        foreach (var cell in cells)
        {
            var chunkPos = SpatialScale.ToChunk(cell);
            if (!_chunks.TryGetValue(chunkPos, out var chunk))
                _chunks[chunkPos] = chunk = new Dictionary<LocalCell, SortedSet<ObjectId>>();
            var local = SpatialScale.ToLocal(cell);
            if (!chunk.TryGetValue(local, out var ids))
                chunk[local] = ids = [];
            ids.Add(id);
        }
    }

    public void Remove(ObjectId id)
    {
        if (!_cellsOf.Remove(id, out var cells))
            return;
        foreach (var cell in cells)
        {
            var chunkPos = SpatialScale.ToChunk(cell);
            var chunk = _chunks[chunkPos];
            var local = SpatialScale.ToLocal(cell);
            var ids = chunk[local];
            ids.Remove(id);
            if (ids.Count == 0)
                chunk.Remove(local);
            if (chunk.Count == 0)
                _chunks.Remove(chunkPos);
        }
    }

    public void Clear()
    {
        _chunks.Clear();
        _cellsOf.Clear();
    }
}
