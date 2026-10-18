using Mintrans.Construction;
using Mintrans.Core.Grid;
using Mintrans.Core.Ids;

namespace Mintrans.Game.Tools;

/// <summary>Что показать при протягивании: клетки, проблемные клетки, можно ли строить и пояснение.</summary>
public sealed record ToolPreview(IReadOnlyList<GridPos> Cells, IReadOnlySet<GridPos> BadCells, bool CanBuild, string Message);

/// <summary>
/// Инструмент строительства: план по протяжке мышью от клетки до клетки и постройка этого плана.
/// Новый инструмент — новый класс и одна строка в <see cref="BuildTools.Create"/>.
/// </summary>
public interface IBuildTool
{
    /// <summary>Идентификатор для названия на кнопке (из names.ru.json).</summary>
    string Id { get; }

    ToolPreview Preview(GridPos from, GridPos to);

    ToolPreview Apply(GridPos from, GridPos to);
}

/// <summary>Прокладка сети: рельсы или дорога.</summary>
public sealed class PathTool(Builder builder, TypeId network) : IBuildTool
{
    public string Id => $"tool.{network}";

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanPath(network, from, to), from);

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.BuildPath(network, from, to), from);

    private static ToolPreview Describe(BuildPlan plan, GridPos from)
    {
        var cells = plan.Cells.Count > 0 ? plan.Cells : [from];
        var message = plan.Problems.Count > 0 ? plan.Problems[0].Message
            : plan.Cells.Count > 0 ? $"Клеток: {plan.Cells.Count}"
            : "Протяните мышью от клетки к клетке";
        return new ToolPreview(cells, BadCells(plan.Problems), plan.CanBuild, message);
    }

    internal static HashSet<GridPos> BadCells(IEnumerable<BuildProblem> problems) =>
        problems.Select(p => p.Cell).OfType<GridPos>().ToHashSet();
}

/// <summary>Грузовая станция предприятия.</summary>
public sealed class StationTool(Builder builder, TypeId station) : IBuildTool
{
    public string Id => station.Value;

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanStation(station, from, to));

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.BuildStation(station, from, to));

    private static ToolPreview Describe(StationPlan plan)
    {
        var message = plan.Problems.Count > 0 ? plan.Problems[0].Message
            : $"Станция у «{plan.Target!.Name}», клеток: {plan.Cells.Count}";
        return new ToolPreview(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild, message);
    }
}

/// <summary>Депо, автогараж или городской пункт.</summary>
public sealed class FacilityTool(Builder builder, TypeId facility) : IBuildTool
{
    public string Id => facility.Value;

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanFacility(facility, from, to));

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.BuildFacility(facility, from, to));

    private static ToolPreview Describe(FacilityPlan plan)
    {
        var hint = plan.Definition.Placement == FacilityPlacement.Depot
            ? "протяните мышью в сторону въезда"
            : "по улице города";
        var message = plan.Problems.Count > 0 ? plan.Problems[0].Message
            : plan.Built is { } built ? built.Name
            : $"{plan.Definition.Title}: {hint}";
        return new ToolPreview(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild, message);
    }
}

/// <summary>Снос прямоугольником: сети и станции.</summary>
public sealed class DemolishTool(Builder builder) : IBuildTool
{
    public string Id => "tool.demolish";

    public ToolPreview Preview(GridPos from, GridPos to) => Describe(builder.PlanDemolish(from, to));

    public ToolPreview Apply(GridPos from, GridPos to) => Describe(builder.Demolish(from, to));

    private static ToolPreview Describe(BuildPlan plan) =>
        new(plan.Cells, PathTool.BadCells(plan.Problems), plan.CanBuild,
            plan.Problems.Count > 0 ? plan.Problems[0].Message : $"Снести клеток: {plan.Cells.Count}");
}

public static class BuildTools
{
    /// <summary>Инструменты по правилам строительства: по одному на каждую сеть и станцию, плюс снос.</summary>
    public static IReadOnlyList<IBuildTool> Create(Builder builder) =>
    [
        .. builder.Catalog.Networks.Select(n => (IBuildTool)new PathTool(builder, n.Network)),
        .. builder.Catalog.Stations.Select(s => (IBuildTool)new StationTool(builder, s.Id)),
        .. builder.Catalog.Facilities.Select(f => (IBuildTool)new FacilityTool(builder, f.Id)),
        new DemolishTool(builder),
    ];
}
