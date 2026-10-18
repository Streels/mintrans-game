using System.Globalization;

namespace Mintrans.Economy;

/// <summary>
/// Масса груза. Внутри — целые килограммы: учёт точный, груз не «утекает» на округлениях
/// (docs/04: ресурс не появляется и не исчезает вне явных операций). Игроку показываются тонны.
/// </summary>
public readonly record struct Mass(long Kg) : IComparable<Mass>
{
    public static readonly Mass Zero = new(0);

    public static Mass FromTonnes(double tonnes)
    {
        if (double.IsNaN(tonnes) || double.IsInfinity(tonnes))
            throw new ArgumentException($"Некорректная масса: {tonnes}.");
        return new Mass((long)Math.Round(tonnes * 1000.0));
    }

    public double Tonnes => Kg / 1000.0;
    public bool IsZero => Kg == 0;

    public static Mass Min(Mass a, Mass b) => a.Kg <= b.Kg ? a : b;
    public static Mass Max(Mass a, Mass b) => a.Kg >= b.Kg ? a : b;

    public static Mass operator +(Mass a, Mass b) => new(checked(a.Kg + b.Kg));
    public static Mass operator -(Mass a, Mass b) => new(checked(a.Kg - b.Kg));
    public static Mass operator *(Mass a, long times) => new(checked(a.Kg * times));
    public static bool operator <(Mass a, Mass b) => a.Kg < b.Kg;
    public static bool operator >(Mass a, Mass b) => a.Kg > b.Kg;
    public static bool operator <=(Mass a, Mass b) => a.Kg <= b.Kg;
    public static bool operator >=(Mass a, Mass b) => a.Kg >= b.Kg;

    /// <summary>Сколько целых порций <paramref name="portion"/> помещается в эту массу.</summary>
    public long Portions(Mass portion) => portion.Kg <= 0 ? long.MaxValue : Kg / portion.Kg;

    public int CompareTo(Mass other) => Kg.CompareTo(other.Kg);

    /// <summary>«1 234 т» или «12,5 т».</summary>
    public override string ToString()
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var text = Kg % 1000 == 0 ? (Kg / 1000).ToString("N0", culture) : Tonnes.ToString("N1", culture);
        return text + " т";
    }
}
