using Godot;

namespace Mintrans.Game.View;

/// <summary>Отрезки для DrawMultiline: подготовка к отрисовке.</summary>
public static class Segments
{
    /// <summary>
    /// Отрезки (пары точек) → те же линии меньшим числом отрезков: цепочки отрезков на одной прямой, у стыков которых
    /// нет развилки, сливаются в один. Дорога в 100 км или контур площадки — не тысячи отрезков
    /// по половине клетки: подготовка отрисовки на процессоре растёт с числом отрезков (docs/02в, раздел 11).
    /// </summary>
    public static Vector2[] MergeStraight(Vector2[] pairs)
    {
        var count = pairs.Length / 2;
        var at = new Dictionary<Vector2, List<int>>();
        for (var i = 0; i < count; i++)
        {
            foreach (var end in new[] { pairs[2 * i], pairs[2 * i + 1] })
            {
                if (!at.TryGetValue(end, out var list))
                    at[end] = list = [];
                list.Add(i);
            }
        }
        var used = new bool[count];
        var merged = new List<Vector2>();
        Vector2 Other(int i, Vector2 end) => pairs[2 * i] == end ? pairs[2 * i + 1] : pairs[2 * i];
        // Продолжить отрезок за конец end, пока стык без развилки и следующий отрезок лежит на той же прямой.
        Vector2 Extend(Vector2 end, Vector2 direction)
        {
            while (at[end] is { Count: 2 } joint)
            {
                var next = joint.FirstOrDefault(j => !used[j], -1);
                if (next < 0)
                    break;
                var far = Other(next, end);
                if (Math.Abs(direction.Cross((far - end).Normalized())) > 1e-4f || direction.Dot(far - end) <= 0)
                    break;
                used[next] = true;
                end = far;
            }
            return end;
        }
        for (var i = 0; i < count; i++)
        {
            if (used[i])
                continue;
            used[i] = true;
            var (a, b) = (pairs[2 * i], pairs[2 * i + 1]);
            if (a == b)
                continue;
            var direction = (b - a).Normalized();
            merged.Add(Extend(a, -direction));
            merged.Add(Extend(b, direction));
        }
        return merged.ToArray();
    }
}
