namespace DeepClean.Core.Utilities;

/// <summary>
/// Fixed capacity buffer that keeps the most recent values in insertion order.
/// Used for the rolling history behind the dashboard charts.
/// </summary>
public sealed class CircularBuffer<T>
{
    private readonly T[] _items;
    private int _start;
    private int _count;

    public CircularBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count => _count;

    public void Add(T item)
    {
        var index = (_start + _count) % _items.Length;

        if (_count == _items.Length)
        {
            _items[_start] = item;
            _start = (_start + 1) % _items.Length;
        }
        else
        {
            _items[index] = item;
            _count++;
        }
    }

    public void Clear()
    {
        Array.Clear(_items);
        _start = 0;
        _count = 0;
    }

    /// <summary>Copies the buffered values into a new array, oldest first.</summary>
    public T[] ToArray()
    {
        var result = new T[_count];

        for (var i = 0; i < _count; i++)
        {
            result[i] = _items[(_start + i) % _items.Length];
        }

        return result;
    }
}
