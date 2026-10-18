namespace Mintrans.Core.Ids;

/// <summary>
/// Строковый идентификатор типа из данных игры: "grass", "road", "coal_mine".
/// Новые типы добавляются в файлы content/, а не в код.
/// </summary>
public readonly record struct TypeId
{
    public TypeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Идентификатор типа не может быть пустым.", nameof(value));
        Value = value;
    }

    public string Value { get; }

    public static implicit operator TypeId(string value) => new(value);

    public override string ToString() => Value;
}
