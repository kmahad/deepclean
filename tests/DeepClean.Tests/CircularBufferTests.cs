using DeepClean.Core.Utilities;

namespace DeepClean.Tests;

public class CircularBufferTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<int>(-1));
    }

    [Fact]
    public void NewBuffer_IsEmpty()
    {
        var buffer = new CircularBuffer<int>(4);

        Assert.Equal(4, buffer.Capacity);
        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.ToArray());
    }

    [Fact]
    public void Add_KeepsInsertionOrderUntilFull()
    {
        var buffer = new CircularBuffer<int>(4);

        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        Assert.Equal(3, buffer.Count);
        Assert.Equal([1, 2, 3], buffer.ToArray());
    }

    [Fact]
    public void Add_OverwritesOldestValueWhenFull()
    {
        var buffer = new CircularBuffer<int>(3);

        foreach (var value in new[] { 1, 2, 3, 4, 5 })
        {
            buffer.Add(value);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal([3, 4, 5], buffer.ToArray());
    }

    [Fact]
    public void Add_HandlesFullBufferWrapAround()
    {
        var buffer = new CircularBuffer<int>(3);

        foreach (var value in Enumerable.Range(1, 10))
        {
            buffer.Add(value);
        }

        Assert.Equal([8, 9, 10], buffer.ToArray());
    }

    [Fact]
    public void Clear_EmptiesBufferButKeepsCapacity()
    {
        var buffer = new CircularBuffer<int>(2);
        buffer.Add(7);
        buffer.Add(8);

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Equal(2, buffer.Capacity);
        Assert.Empty(buffer.ToArray());

        buffer.Add(9);
        Assert.Equal([9], buffer.ToArray());
    }

    [Fact]
    public void ToArray_ReturnsIndependentCopy()
    {
        var buffer = new CircularBuffer<int>(2);
        buffer.Add(1);

        var copy = buffer.ToArray();
        buffer.Add(2);

        Assert.Equal([1], copy);
    }
}
