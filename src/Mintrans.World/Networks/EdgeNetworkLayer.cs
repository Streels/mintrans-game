using Mintrans.Core.Grid;
using Mintrans.Core.Ids;
using Mintrans.World.Layers;

namespace Mintrans.World.Networks;

/// <summary>
/// Раскладка «по сторонам» — для дорог. В клетке хранится, к каким сторонам подходит дорога;
/// внутри клетки все они соединены (поворот, Т-образный перекрёсток, крест). Одна сторона — тупик.
/// </summary>
public sealed class EdgeNetworkLayer : NetworkLayer
{
    public const string LayoutName = "edges";

    private readonly CellLayer<DirectionMask> _edges;

    public EdgeNetworkLayer(TypeId type, int width, int height) : base(type, width, height)
    {
        _edges = new CellLayer<DirectionMask>(width, height);
    }

    protected override int CellRevision => _edges.Revision;

    public override DirectionMask EdgesAt(GridPos pos) => _edges[pos];

    public override IEnumerable<GridPos> OccupiedCells() => _edges.NonDefault().Select(c => c.Cell);

    public override bool Connects(GridPos pos, Direction from, Direction to) =>
        from != to && _edges[pos].Has(from) && _edges[pos].Has(to);

    /// <summary>Довести дорогу в клетке до стороны. Связь с соседом появится, когда и он выйдет на эту сторону.</summary>
    public void AddEdge(GridPos pos, Direction side) => _edges[pos] |= side.ToMask();

    public void RemoveEdge(GridPos pos, Direction side) => _edges[pos] &= ~side.ToMask();

    /// <summary>Какие стороны будут у дороги в клетке после шага прокладки (для правил строительства).</summary>
    public DirectionMask EdgesAfter(PathStep step) =>
        _edges[step.Cell] | (step.InOpen ? DirectionMask.None : step.In.ToMask()) | (step.OutOpen ? DirectionMask.None : step.Out.ToMask());

    public override void LayThrough(GridPos pos, Direction a, Direction b)
    {
        AddEdge(pos, a);
        AddEdge(pos, b);
    }

    public override void LayEnd(GridPos pos, Direction side)
    {
        AddEdge(pos, side);
        JoinNeighbor(pos, side, open: false);
    }

    protected override void ClearCellCore(GridPos pos)
    {
        foreach (var side in _edges[pos].Enumerate())
        {
            var neighbor = pos.Step(side);
            if (Contains(neighbor))
                RemoveEdge(neighbor, side.Opposite());
        }
        _edges[pos] = DirectionMask.None;
    }

    public override void LayPath(IReadOnlyList<GridPos> path)
    {
        // Края «в чистом поле» — тупики; края, пристыкованные к сети, выходят к ней.
        var steps = PlanSteps(path);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!step.InOpen)
                AddEdge(step.Cell, step.In);
            if (!step.OutOpen)
                AddEdge(step.Cell, step.Out);
        }
        // Пристыкованные края: соседняя дорога тоже выходит навстречу.
        JoinNeighbor(steps[0].Cell, steps[0].In, steps[0].InOpen);
        JoinNeighbor(steps[^1].Cell, steps[^1].Out, steps[^1].OutOpen);
    }

    protected override bool AcceptsJoinCore(GridPos neighbor, Direction side) => HasAny(neighbor);

    private void JoinNeighbor(GridPos cell, Direction side, bool open)
    {
        var neighbor = cell.Step(side);
        if (!open && Contains(neighbor) && AcceptsJoin(neighbor, side.Opposite()))
            AddEdge(neighbor, side.Opposite());
    }

    public override IEnumerable<NetworkStroke> StrokesAt(GridPos pos) =>
        _edges[pos].Enumerate().Select(side => new NetworkStroke(pos, null, side));
}
