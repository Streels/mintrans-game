using Mintrans.Transport.Definitions;
using Mintrans.Transport.Runtime;

namespace Mintrans.Game.UI;

/// <summary>Тексты о технике для интерфейса.</summary>
public static class VehicleText
{
    public static string Status(Vehicle vehicle, TransportState transport) => vehicle.State switch
    {
        VehicleState.InDepot => "в депо, без маршрута",
        VehicleState.Servicing => $"на обслуживании ({vehicle.HoursInState} ч)",
        VehicleState.Stuck => $"стоит: {vehicle.Reason}",
        VehicleState.Crashed => $"разбит в крушении ({vehicle.HoursInState} ч назад)",
        VehicleState.Travelling when vehicle.BlockedHours > 0 => $"{vehicle.Reason} — {vehicle.BlockedHours} ч",
        _ => vehicle.Reason,
    };

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
