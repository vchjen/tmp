using System;
using System.Collections.Generic;

public static class MaxMinHelper
{
    /// <summary>
    /// Streaming min/max for any IEnumerable&lt;T&gt;.
    /// Works with infinite sequences and does not buffer.
    /// If comparer is null, Comparer&lt;T&gt;.Default is used.
    /// </summary>
    public static (T min, T max) GetMinMax<T>(
        IEnumerable<T> source,
        IComparer<T>? comparer = null)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        comparer ??= Comparer<T>.Default;

        using IEnumerator<T> e = source.GetEnumerator();

        // Need at least one element
        if (!e.MoveNext())
            throw new InvalidOperationException("Sequence contains no elements.");

        T min = e.Current;
        T max = e.Current;

        // Stream element-by-element
        while (e.MoveNext())
        {
            T cur = e.Current;

            if (comparer.Compare(cur, min) < 0)
                min = cur;

            if (comparer.Compare(cur, max) > 0)
                max = cur;
        }

        return (min, max);
    }
}

// ===================
// Example program
// ===================
public static class Program
{
    static void Main()
    {
        int[] data = { 9, 1, 200, -5, 30, 17 };

        var (min1, max1) = MaxMinHelper.GetMinMax(data);
        Console.WriteLine($"Default comparer → Min: {min1}, Max: {max1}");

        // Reverse comparer example
        var reverseComparer = Comparer<int>.Create((a, b) => b.CompareTo(a));

        var (min2, max2) = MaxMinHelper.GetMinMax(data, reverseComparer);
        Console.WriteLine($"Custom (reverse) → Min: {min2}, Max: {max2}");
    }
}
