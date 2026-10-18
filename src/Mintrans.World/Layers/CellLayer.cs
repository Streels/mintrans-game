using Mintrans.Core.Grid;

namespace Mintrans.World.Layers;

/// <summary>
/// Одно значение на каждую клетку карты, хранится плотным массивом.
/// Основа всех слоёв мира: высоты, поверхности, сетей, занятости.
/// </summary>
public sealed class CellLayer<T> where T : unmanaged
{
    private readonly T[] _cells;

    public CellLayer(int width, int height, T initial = default)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Размер слоя должен быть положительным: {width}x{height}.");
        Width = width;
        Height = height;
        _cells = new T[width * height];
        if (!EqualityComparer<T>.Default.Equals(initial, default))
            Array.Fill(_cells, initial);
    }

    public int Width { get; }
    public int Height { get; }
    public GridRect Bounds => new(0, 0, Width, Height);

    /// <summary>Растёт при каждом изменении. Отображение по нему понимает, что слой пора перерисовать.</summary>
    public int Revision { get; private set; }

    public bool Contains(GridPos pos) => pos.X >= 0 && pos.X < Width && pos.Y >= 0 && pos.Y < Height;

    public T this[GridPos pos]
    {
        get => _cells[IndexOf(pos)];
        set
        {
            _cells[IndexOf(pos)] = value;
            Revision++;
        }
    }

    public T this[int x, int y]
    {
        get => this[new GridPos(x, y)];
        set => this[new GridPos(x, y)] = value;
    }

    public void Fill(T value)
    {
        Array.Fill(_cells, value);
        Revision++;
    }

    /// <summary>Только чтение всего слоя — для быстрой отрисовки и сравнения.</summary>
    public ReadOnlySpan<T> AsSpan() => _cells;

    private int IndexOf(GridPos pos)
    {
        if (!Contains(pos))
            throw new ArgumentOutOfRangeException(nameof(pos), $"Клетка {pos} вне карты {Width}x{Height}.");
        return pos.Y * Width + pos.X;
    }
}
