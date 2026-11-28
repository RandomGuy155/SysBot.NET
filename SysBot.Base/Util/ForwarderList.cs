using System;
using System.Threading;

namespace SysBot.Base;

/// <summary>
/// Thread-safe copy-on-write list for log, echo, and notification forwarders.
/// </summary>
public sealed class ForwarderList<T>
{
    private readonly Lock _sync = new();
    private T[] _items = [];

    public T[] Snapshot => _items;

    public void Add(T item)
    {
        lock (_sync)
            _items = [.. _items, item];
    }

    public bool Remove(T item)
    {
        lock (_sync)
        {
            var idx = Array.IndexOf(_items, item);
            if (idx < 0)
                return false;

            var next = new T[_items.Length - 1];
            Array.Copy(_items, 0, next, 0, idx);
            Array.Copy(_items, idx + 1, next, idx, _items.Length - idx - 1);
            _items = next;
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
            _items = [];
    }
}

