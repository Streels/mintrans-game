using Mintrans.Transport.Definitions;
using Mintrans.Transport.Reports;
using Mintrans.Transport.Runtime;

namespace Mintrans.Game.UI;

/// <summary>Тексты о технике для интерфейса.</summary>
public static class VehicleText
{
    public static string Status(Vehicle vehicle, TransportState transport) => vehicle.State switch
    {
        VehicleState.InDepot => vehicle.A is null ? "в депо, без маршрута" : "в депо, маршрут сохранён",
        VehicleState.Servicing => $"на обслуживании ({vehicle.HoursInState} ч)",
        VehicleState.Stuck => $"стоит: {vehicle.Reason}",
        VehicleState.Crashed => $"разбит в крушении ({vehicle.HoursInState} ч назад)",
        VehicleState.Stopped => $"остановлен ({vehicle.HoursInState} ч)",
        VehicleState.Travelling when vehicle.BlockedHours > 0 => $"{vehicle.Reason} — {vehicle.BlockedHours} ч",
        VehicleState.Travelling when vehicle.HoursToArrival is { } hours => $"{vehicle.Reason} · прибытие через {Duration(hours)}",
        _ => vehicle.Reason,
    };

    /// <summary>«≈5 ч» или «≈2,5 сут».</summary>
    public static string Duration(double hours) =>
        hours < 48 ? $"≈{Math.Max(1, Math.Round(hours)):0} ч" : $"≈{hours / 24:0.#} сут";

    /// <summary>
    /// Сводка направления (docs/12): «Направление: 3 ед. (работают 2), вместимость 40 т, путь 120 кл.;
    /// может везти ≈150 т/сут, доставлено за 7 сут: 600 т».
    /// </summary>
    public static string Direction(DirectionSummary summary)
    {
        var distance = summary.DistanceCells is { } cells ? $", путь {cells} кл." : ", пути нет";
        var working = summary.Working == summary.Vehicles ? "" : $" (работают {summary.Working})";
        return $"Направление: {summary.Vehicles} ед.{working}, вместимость {summary.Capacity}{distance}; " +
               $"может везти до {summary.PerDay}/сут, доставлено за {summary.Days} сут: {summary.Delivered}";
    }

    /// <summary>«Грузовой локомотив + 11 × Полувагон · 6 кл.»</summary>
    public static string Composition(Vehicle vehicle)
    {
        var groups = vehicle.Units
            .Where(u => u.Model.Role == UnitRole.Wagon)
            .GroupBy(u => u.Model.Title)
            .Select(g => $"{g.Count()} × {g.Key}");
        var head = vehicle.Units.First().Model.Title;
        var wagons = string.Join(", ", groups);
        var length = vehicle.Network.Trains ? $" · длина {vehicle.LengthCells} кл." : "";
        return (wagons.Length > 0 ? $"{head} + {wagons}" : head) + $" · вмещает {vehicle.Capacity}{length}";
    }

    public static string Details(Vehicle vehicle, DisplayNames names)
    {
        var cargo = vehicle.Cargo.IsZero
            ? "пустой"
            : string.Join(", ", vehicle.Units.Where(u => u.CargoType is not null)
                .GroupBy(u => u.CargoType!.Value)
                .Select(g => $"{names[g.Key.Value]} {vehicle.CargoOf(g.Key)}"));
        return $"Груз: {cargo} · износ {vehicle.Wear:P0}";
    }
}
