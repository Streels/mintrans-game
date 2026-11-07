using Mintrans.Core.Grid;
using Mintrans.Core.Spatial;

namespace Mintrans.World.Layers;

/// <summary>
/// Одно значение на каждую клетку карты, хранится разреженно (docs/02в): чанками по
/// <see cref="SpatialScale.ChunkCells"/>×<see cref="SpatialScale.ChunkCells"/> клеток, только там, где значение
/// отличается от значения по умолчанию. Пустая карта 40×40 км (8 000×8 000 клеток) не выделяет памяти под клетки.
/// Основа слоёв мира: сетей, занятости площадок и сооружений.
/// </summary>
public sealed class CellLayer<T> where T : unmanaged
{
    private const int Size = SpatialScale.ChunkCells;
    private readonly Dictionary<(int X, int Y), T[]> _chunks = new();
    private T _default;

    public CellLayer(int width, int height, T initial = default)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Размер слоя должен быть положительным: {width}x{height}.");
        Width = width;
        Height = height;
        _default = initial;
    }

    public int Width { get; }
    public int Height { get; }
    public GridRect Bounds => new(0, 0, Width, Height);

    /// <summary>Растёт при каждом изменении. Отображение по нему понимает, что слой пора перерисовать.</summary>
    public int Revision { get; private set; }

    /// <summary>Значение клеток, о которых ничего не записано.</summary>
    public T Default => _default;

    /// <summary>Сколько чанков хранит записи (для проверки разреженности).</summary>
    public int ChunkCount => _chunks.Count;

    public bool Contains(GridPos pos) => pos.X >= 0 && pos.X < Width && pos.Y >= 0 && pos.Y < Height;

    public T this[GridPos pos]
    {
        get
        {
            Check(pos);
            return _chunks.TryGetValue((pos.X / Size, pos.Y / Size), out var chunk) ? chunk[Local(pos)] : _default;
        }
        set
        {
            Check(pos);
            var key = (pos.X / Size, pos.Y / Size);
            if (!_chunks.TryGetValue(key, out var chunk))
            {
                if (EqualityComparer<T>.Default.Equals(value, _default))
                    return;
                _chunks[key] = chunk = new T[Size * Size];
                if (!EqualityComparer<T>.Default.Equals(_default, default))
                    Array.Fill(chunk, _default);
            }
            chunk[Local(pos)] = value;
            Revision++;
        }
    }

    public T this[int x, int y]
    {
        get => this[new GridPos(x, y)];
        set => this[new GridPos(x, y)] = value;
    }

    /// <summary>Записать во все клетки одно значение: записи удаляются, оно становится значением по умолчанию.</summary>
    public void Fill(T value)
    {
        _chunks.Clear();
        _default = value;
        Revision++;
    }

    /// <summary>
    /// Клетки со значением, отличным от значения по умолчанию, в постоянном порядке: чанки по строкам, внутри — по строкам.
    /// </summary>
    public IEnumerable<(GridPos Cell, T Value)> NonDefault()
    {
        foreach (var key in _chunks.Keys.OrderBy(k => k.Y).ThenBy(k => k.X).ToList())
        {
            var chunk = _chunks[key];
            for (var i = 0; i < chunk.Length; i++)
            {
                if (EqualityComparer<T>.Default.Equals(chunk[i], _default))
                    continue;
                var cell = new GridPos(key.X * Size + i % Size, key.Y * Size + i / Size);
                if (Contains(cell))
                    yield return (cell, chunk[i]);
            }
        }
    }

    /// <summary>Совпадают ли значения всех клеток двух слоёв одного размера.</summary>
    public bool SameAs(CellLayer<T> other) =>
        Width == other.Width && Height == other.Height &&
        EqualityComparer<T>.Default.Equals(_default, other._default) &&
        NonDefault().SequenceEqual(other.NonDefault());

    private static int Local(GridPos pos) => pos.Y % Size * Size + pos.X % Size;

    private void Check(GridPos pos)
    {
        if (!Contains(pos))
            throw new ArgumentOutOfRangeException(nameof(pos), $"Клетка {pos} вне карты {Width}x{Height}.");
    }
}
