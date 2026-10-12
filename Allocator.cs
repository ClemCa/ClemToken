namespace ClemToken;

public enum AllocationStrategy
{
    Static,
    Pooled,
    Arena
}

public readonly record struct ArenaMark(int Count);

public readonly record struct AllocatorStats(long Allocated, long Released, int Free, int ArenaCapacity, int ArenaHighWater);

//! not thread-safe: hold one per parse and pass it explicitly like Optimizations/SpecStore/
//! ReferenceStore, rather than a static shared across concurrent parses
//! Allocate/Release serve Static & Pooled; Push/index/Mark/Rollback serve Arena and throw otherwise
public class Allocator<T>(AllocationStrategy strategy, int poolSize = 1024, int arenaCapacity = 1024) where T : new()
{
    private readonly Stack<T> _free = new();
    private T[] _arena = strategy == AllocationStrategy.Arena && typeof(T).IsValueType
        ? new T[Math.Max(1, arenaCapacity)]
        : [];
    private int _count;
    private int _highWater;
    private long _allocated;
    private long _released;

    public AllocationStrategy Strategy => strategy;

    public AllocatorStats Stats => new(_allocated, _released, _free.Count, _arena.Length, _highWater);

    //! Arena lands here as new T(): a struct returned by value is a copy, so a slot is only good
    //! through Push/index
    public T Allocate()
    {
        _allocated++;
        if (strategy == AllocationStrategy.Pooled && !typeof(T).IsValueType && _free.Count > 0)
        {
            return _free.Pop();
        }
        return new T();
    }

    public void Release(T item)
    {
        if (strategy != AllocationStrategy.Pooled || typeof(T).IsValueType || item is null || _free.Count >= poolSize)
        {
            return;
        }
        _free.Push(item);
        _released++;
    }

    //! write through this[index] and not the returned value, or the change lands in a copy
    public int Push(T value)
    {
        RequireArena();
        if (_count == _arena.Length)
        {
            Array.Resize(ref _arena, _arena.Length * 2);
        }
        _arena[_count] = value;
        _allocated++;
        int index = _count++;
        if (_count > _highWater)
        {
            _highWater = _count;
        }
        return index;
    }

    public ref T this[int index]
    {
        get
        {
            RequireArena();
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return ref _arena[index];
        }
    }

    public ArenaMark Mark()
    {
        RequireArena();
        return new ArenaMark(_count);
    }

    // cleared so a rolled-back slot holding references doesn't keep them alive
    public void Rollback(ArenaMark mark)
    {
        RequireArena();
        if (mark.Count >= _count)
        {
            return;
        }
        Array.Clear(_arena, mark.Count, _count - mark.Count);
        _count = mark.Count;
    }

    public void Reset()
    {
        _free.Clear();
        Array.Clear(_arena, 0, _count);
        _count = 0;
    }

    private void RequireArena()
    {
        if (strategy != AllocationStrategy.Arena || !typeof(T).IsValueType)
        {
            throw new InvalidOperationException($"Allocator<{typeof(T).Name}> is {strategy}; arena members require AllocationStrategy.Arena over a value type.");
        }
    }
}
