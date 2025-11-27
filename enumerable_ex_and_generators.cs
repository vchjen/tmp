using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

        if (!e.MoveNext())
            throw new InvalidOperationException("Sequence contains no elements.");

        T min = e.Current;
        T max = e.Current;

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

    /// <summary>
    /// Streaming min/max for any IAsyncEnumerable&lt;T&gt;.
    /// Does not buffer; processes items as they arrive.
    /// If comparer is null, Comparer&lt;T&gt;.Default is used.
    /// </summary>
    public static async ValueTask<(T min, T max)> GetMinMaxAsync<T>(
        IAsyncEnumerable<T> source,
        IComparer<T>? comparer = null,
        CancellationToken cancellationToken = default)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        comparer ??= Comparer<T>.Default;

        await using var e = source.GetAsyncEnumerator(cancellationToken);

        if (!await e.MoveNextAsync())
            throw new InvalidOperationException("Sequence contains no elements.");

        T min = e.Current;
        T max = e.Current;

        while (await e.MoveNextAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            T cur = e.Current;

            if (comparer.Compare(cur, min) < 0)
                min = cur;

            if (comparer.Compare(cur, max) > 0)
                max = cur;
        }

        return (min, max);
    }
}

/// <summary>
/// Incremental min/max accumulator. 
/// Feed values via Add(), then read Min/Max or Result.
/// </summary>
public struct MinMaxAccumulator<T>
{
    private T _min;
    private T _max;
    private bool _hasValue;
    private readonly IComparer<T> _comparer;

    public bool HasValue => _hasValue;

    public T Min
    {
        get
        {
            if (!_hasValue) throw new InvalidOperationException("No values have been added.");
            return _min;
        }
    }

    public T Max
    {
        get
        {
            if (!_hasValue) throw new InvalidOperationException("No values have been added.");
            return _max;
        }
    }

    public (T min, T max) Result
    {
        get
        {
            if (!_hasValue) throw new InvalidOperationException("No values have been added.");
            return (_min, _max);
        }
    }

    public MinMaxAccumulator(IComparer<T>? comparer = null)
    {
        _min = default!;
        _max = default!;
        _hasValue = false;
        _comparer = comparer ?? Comparer<T>.Default;
    }

    public void Add(T value)
    {
        if (!_hasValue)
        {
            _min = value;
            _max = value;
            _hasValue = true;
            return;
        }

        if (_comparer.Compare(value, _min) < 0)
            _min = value;

        if (_comparer.Compare(value, _max) > 0)
            _max = value;
    }

    public void Reset()
    {
        _min = default!;
        _max = default!;
        _hasValue = false;
    }
}

// ===================
// Example program
// ===================
public static class Program
{
    public static async Task Main()
    {
        int[] data = { 9, 1, 200, -5, 30, 17 };

        // --- Sync helper ---
        var (min1, max1) = MaxMinHelper.GetMinMax(data);
        Console.WriteLine($"Sync helper → Min: {min1}, Max: {max1}");

        // --- Async helper ---
        var (min2, max2) = await MaxMinHelper.GetMinMaxAsync(GenerateAsync());
        Console.WriteLine($"Async helper → Min: {min2}, Max: {max2}");

        // --- Incremental accumulator (sync) ---
        var acc = new MinMaxAccumulator<int>();
        foreach (var v in data)
            acc.Add(v);

        var (min3, max3) = acc.Result;
        Console.WriteLine($"Accumulator (sync) → Min: {min3}, Max: {max3}");

        // --- Incremental accumulator over async stream ---
        var accAsync = new MinMaxAccumulator<int>();
        await foreach (var v in GenerateAsync())
        {
            accAsync.Add(v);
        }

        var (min4, max4) = accAsync.Result;
        Console.WriteLine($"Accumulator over async stream → Min: {min4}, Max: {max4}");
    }

    // Example async stream
    private static async IAsyncEnumerable<int> GenerateAsync()
    {
        int[] values = { 10, 3, 99, -4, 50 };

        foreach (var v in values)
        {
            await Task.Delay(10); // simulate async work
            yield return v;
        }
    }
}
