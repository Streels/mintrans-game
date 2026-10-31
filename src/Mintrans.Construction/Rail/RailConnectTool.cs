using Mintrans.Core.Geometry;
using Mintrans.Core.Spatial;
using Mintrans.World.Rail;

namespace Mintrans.Construction.Rail;

/// <summary>Точка подключения инструмента «Соединить А→Б» (docs/10в, разделы 2–3).</summary>
public abstract record RailAnchor;

/// <summary>Свободный конец построенного пути.</summary>
public sealed record FreeEndAnchor(TrackNodeId Node) : RailAnchor;

/// <summary>
/// Точка на прямом участке построенного пути — здесь встанет стрелка. <paramref name="AlongEdge"/> — движение А→Б
/// идёт по направлению пути (от его начала к концу).
/// </summary>
public sealed record EdgePointAnchor(TrackEdgeId Edge, double DistanceM, bool AlongEdge) : RailAnchor;

/// <summary>Новый конец пути в свободном месте с заданным направлением движения.</summary>
public sealed record NewEndAnchor(Pose Pose, int Level = 0) : RailAnchor;

/// <summary>Один вариант соединения: кривая, изменение сети и результат его проверки.</summary>
public sealed record RailConnectOption(CscCandidate Candidate, RailEdit Edit, RailEditPreview Check)
{
    public double RadiusM => Candidate.RadiusM;
    public double LengthM => Candidate.LengthM;
}

/// <summary>Допустимые варианты одного радиуса: <c>Tab</c> переключает их по порядку длины.</summary>
public sealed record RailRadiusOption(double RadiusM, IReadOnlyList<RailConnectOption> Variants);

/// <summary>
/// Предпросмотр соединения А→Б (docs/10в, раздел 2). Мир не меняется, пока игрок не подтвердит.
/// Колесо мыши перебирает допустимые радиусы, <c>Tab</c> — варианты одного радиуса. Нет варианта — отказ с причиной,
/// подтвердить нельзя: отсутствующую кривую не заменяют резким поворотом.
/// </summary>
public sealed class RailConnection
{
    private readonly RailNetwork _network;

    internal RailConnection(RailNetwork network, IReadOnlyList<RailRadiusOption> radii, int radius, int variant,
        IReadOnlyList<string> rejections)
    {
        _network = network;
        Radii = radii;
        RadiusIndex = radius;
        VariantIndex = variant;
        Rejections = rejections;
    }

    /// <summary>Допустимые радиусы по возрастанию, у каждого — свои варианты.</summary>
    public IReadOnlyList<RailRadiusOption> Radii { get; }

    public int RadiusIndex { get; private set; }
    public int VariantIndex { get; private set; }

    /// <summary>Почему отброшены остальные кандидаты; если вариантов нет — почему соединить нельзя.</summary>
    public IReadOnlyList<string> Rejections { get; }

    public bool CanConfirm => Current is not null;

    public RailConnectOption? Current => Radii.Count > 0 ? Radii[RadiusIndex].Variants[VariantIndex] : null;

    /// <summary>Колесо мыши: следующий (+1) или предыдущий (−1) допустимый радиус. Мир не меняется.</summary>
    public void ScrollRadius(int step)
    {
        if (Radii.Count == 0)
            return;
        RadiusIndex = Math.Clamp(RadiusIndex + step, 0, Radii.Count - 1);
        VariantIndex = 0;
    }

    /// <summary><c>Tab</c>: следующий вариант того же радиуса.</summary>
    public void NextVariant()
    {
        if (Radii.Count > 0)
            VariantIndex = (VariantIndex + 1) % Radii[RadiusIndex].Variants.Count;
    }

    /// <summary>Левый клик: построить выбранный вариант одной транзакцией.</summary>
    public RailEditPreview Confirm() =>
        Current is { } option
            ? _network.Commit(option.Edit)
            : throw new InvalidOperationException("Нет допустимого варианта соединения — подтвердить нельзя.");
}

/// <summary>
/// Инструмент «Соединить А→Б» (docs/10в): строит наиболее плавное допустимое соединение двух концов пути,
/// в том числе со стрелкой на прямом участке существующего пути. Тот же инструмент кладёт прямую между двумя новыми концами.
/// </summary>
public sealed class RailConnectTool(RailNetwork network, RailEditorSettings settings)
{
    public RailNetwork Network { get; } = network;
    public RailEditorSettings Settings { get; } = settings;

    /// <summary>Найти допустимые варианты соединения и выбрать вариант по умолчанию. Мир не меняется.</summary>
    public RailConnection Preview(RailAnchor a, RailAnchor b)
    {
        var rejections = new List<string>();
        if (Resolve(a, outgoing: true, rejections) is not { } start || Resolve(b, outgoing: false, rejections) is not { } end)
            return new RailConnection(Network, [], 0, 0, rejections);
        if (a is EdgePointAnchor ea && b is EdgePointAnchor eb && ea.Edge == eb.Edge)
            return new RailConnection(Network, [], 0, 0, ["А и Б на одном пути — соедините через другой путь"]);

        var maxSweep = Angles.ToRadians(Settings.MaxArcSweepDeg) + 1e-9;
        var options = new List<RailConnectOption>();
        foreach (var radius in Settings.AllowedRadii.OrderDescending())
        foreach (var candidate in CscPlanner.Plan(start, end, radius))
        {
            if (candidate.FirstSweep > maxSweep || candidate.SecondSweep > maxSweep)
            {
                rejections.Add($"{candidate.Family}, R {radius:0} м: дуга круче {Settings.MaxArcSweepDeg:0}°");
                continue;
            }
            // Прямая получается при любом радиусе — оставляем одну.
            if (options.Any(o => SameGeometry(o.Candidate.Geometry, candidate.Geometry)))
                continue;
            var edit = BuildEdit(a, b, candidate.Geometry);
            var check = Network.Preview(edit);
            if (!check.IsValid)
            {
                rejections.Add($"{candidate.Family}, R {radius:0} м: {check.Issues[0]}");
                continue;
            }
            options.Add(new RailConnectOption(candidate, edit, check));
        }
        if (options.Count == 0)
        {
            if (rejections.Count == 0)
                rejections.Add($"нет соединения с радиусом не меньше {Settings.MinRadiusM:0} м — перенесите точку или добавьте прямой участок");
            return new RailConnection(Network, [], 0, 0, rejections);
        }

        var radii = options
            .GroupBy(o => o.RadiusM)
            .OrderBy(g => g.Key)
            .Select(g => new RailRadiusOption(g.Key, g.OrderBy(o => o.LengthM).ThenBy(o => o.Candidate.Family).ToList()))
            .ToList();
        var best = Default(options);
        var radiusIndex = radii.FindIndex(r => r.RadiusM == best.RadiusM);
        var variantIndex = radii[radiusIndex].Variants.ToList().IndexOf(best);
        return new RailConnection(Network, radii, radiusIndex, variantIndex, rejections);
    }

    /// <summary>
    /// Выбор по умолчанию (docs/10в): из вариантов не длиннее <see cref="RailEditorSettings.MaxDefaultDetourRatio"/> кратчайшего —
    /// наибольший радиус, затем кратчайший, затем порядок семейств.
    /// </summary>
    private RailConnectOption Default(List<RailConnectOption> options)
    {
        var shortest = options.Min(o => o.LengthM);
        return options
            .Where(o => o.LengthM <= shortest * Settings.MaxDefaultDetourRatio + SpatialScale.EpsilonM)
            .OrderByDescending(o => o.RadiusM)
            .ThenBy(o => o.LengthM)
            .ThenBy(o => o.Candidate.Family)
            .First();
    }

    private static bool SameGeometry(PathGeometry x, PathGeometry y) =>
        Math.Abs(x.Length - y.Length) <= SpatialScale.EpsilonM &&
        x.Primitives.Count == y.Primitives.Count &&
        x.Primitives.Zip(y.Primitives).All(p => p.First.End.ApproxEquals(p.Second.End, SpatialScale.EpsilonM));

    /// <summary>
    /// Точка и направление движения у якоря. Для А — направление выезда из А, для Б — направление въезда в существующий путь.
    /// </summary>
    private Pose? Resolve(RailAnchor anchor, bool outgoing, List<string> rejections)
    {
        switch (anchor)
        {
            case FreeEndAnchor free:
            {
                var end = Network.FreeEnds.FirstOrDefault(e => e.Node == free.Node);
                if (end is null)
                {
                    rejections.Add($"{free.Node} — не свободный конец пути");
                    return null;
                }
                return new Pose(end.At, outgoing ? end.Heading : Angles.Reverse(end.Heading));
            }
            case EdgePointAnchor point:
            {
                if (!Network.ContainsEdge(point.Edge))
                {
                    rejections.Add($"нет пути {point.Edge}");
                    return null;
                }
                var edge = Network.Edge(point.Edge);
                if (SwitchProblem(edge, point.DistanceM) is { } problem)
                {
                    rejections.Add(problem);
                    return null;
                }
                var heading = edge.Geometry.HeadingAt(point.DistanceM);
                return new Pose(edge.Geometry.PointAt(point.DistanceM), point.AlongEdge ? heading : Angles.Reverse(heading));
            }
            case NewEndAnchor fresh:
                return fresh.Pose;
            default:
                throw new NotSupportedException($"Неизвестная точка подключения {anchor.GetType().Name}.");
        }
    }

    /// <summary>Можно ли врезать стрелку в точке пути: только на прямой, не у концов и не в зоне другой стрелки (docs/10в, раздел 3).</summary>
    private string? SwitchProblem(TrackEdge edge, double distanceM)
    {
        if (distanceM <= SpatialScale.EpsilonM || distanceM >= edge.LengthM - SpatialScale.EpsilonM)
            return $"точка не на середине {edge.Id} — подключайтесь к концу пути";
        var (index, local) = edge.Geometry.Locate(distanceM);
        var primitive = edge.Geometry.Primitives[index];
        if (primitive is not StraightPrimitive || local <= SpatialScale.EpsilonM && index > 0 &&
            edge.Geometry.Primitives[index - 1] is not StraightPrimitive)
            return $"стрелку нельзя врезать в кривую ({edge.Id})";
        foreach (var node in new[] { edge.From, edge.To })
            if (Network.JunctionAt(node) is { } junction &&
                junction.ConflictZone.Any(z => z.Edge == edge.Id && distanceM >= z.FromM - SpatialScale.EpsilonM && distanceM <= z.ToM + SpatialScale.EpsilonM))
                return $"слишком близко к стрелке в {node} — перенесите точку";
        return null;
    }

    private RailEdit BuildEdit(RailAnchor a, RailAnchor b, PathGeometry geometry)
    {
        var edit = Network.Begin();
        var from = NodeFor(edit, a);
        var to = NodeFor(edit, b);
        edit.AddEdge(from, to, geometry);
        return edit;
    }

    private static TrackNodeId NodeFor(RailEdit edit, RailAnchor anchor) => anchor switch
    {
        FreeEndAnchor free => free.Node,
        EdgePointAnchor point => edit.SplitEdge(point.Edge, point.DistanceM),
        NewEndAnchor fresh => edit.NewNode(fresh.Pose.At, fresh.Level),
        _ => throw new NotSupportedException($"Неизвестная точка подключения {anchor.GetType().Name}."),
    };
}
