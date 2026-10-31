using Mintrans.Core.Ids;
using Mintrans.World.Rail;

namespace Mintrans.Construction.Rail;

/// <summary>
/// Параметры железнодорожного редактора v1 (docs/10в, раздел 2): черновая конфигурация, а не строительные нормы.
/// Меняются в content/construction.json (раздел <c>railEditor</c>).
/// </summary>
/// <param name="MinRadiusM">Наименьший радиус кривой — один для всего подвижного состава.</param>
/// <param name="RadiusCandidatesM">Радиусы, которые перебирает колесо мыши, по возрастанию.</param>
/// <param name="MaxArcSweepDeg">Наибольший угол одной дуги соединения.</param>
/// <param name="MaxDefaultDetourRatio">Насколько вариант по умолчанию может быть длиннее кратчайшего: ограничивает нелепые объезды.</param>
/// <param name="CorridorHalfWidthM">Половина ширины строительного коридора пути: оси двух путей через 5 м не конфликтуют.</param>
public sealed record RailEditorSettings(
    double MinRadiusM,
    IReadOnlyList<double> RadiusCandidatesM,
    double MaxArcSweepDeg,
    double MaxDefaultDetourRatio,
    double CorridorHalfWidthM)
{
    public static readonly RailEditorSettings Default = new(200, [200, 250, 300, 400, 500, 750, 1000], 90, 1.35, 2.0);

    /// <summary>Радиусы, допустимые при минимуме <see cref="MinRadiusM"/>, по возрастанию.</summary>
    public IEnumerable<double> AllowedRadii => RadiusCandidatesM.Where(r => r >= MinRadiusM).Order();

    /// <summary>Свойства рельсовой сети вида <paramref name="kind"/>, которые следуют из этих параметров.</summary>
    public RailNetworkSettings NetworkSettings(TypeId kind) => new(kind, CorridorHalfWidthM, MinRadiusM);
}
