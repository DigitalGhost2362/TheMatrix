using System;
using System.Collections.Generic;
using System.Threading;

namespace Feeder.Reflect.Utils
{
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
	private readonly struct CacheItem
	{
		public readonly TKey Key;

		public readonly TValue Value;

		public CacheItem(TKey key, TValue value)
		{
			Key = key;
			Value = value;
		}
	}

	private readonly int _capacity;

	private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;

	private readonly LinkedList<CacheItem> _lruList;

	private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

	public int Count
	{
		get
		{
			_lock.EnterReadLock();
			try
			{
				return _cache.Count;
			}
			finally
			{
				_lock.ExitReadLock();
			}
		}
	}

	public int Capacity => _capacity;

	public TValue this[TKey key]
	{
		get
		{
			if (TryGetValue(key, out var value))
			{
				return value;
			}
			throw new KeyNotFoundException($"The key '{key}' was not found in the cache.");
		}
		set
		{
			_lock.EnterWriteLock();
			try
			{
				if (_cache.TryGetValue(key, out LinkedListNode<CacheItem> value2))
				{
					_lruList.Remove(value2);
					_cache.Remove(key);
				}
				while (_cache.Count >= _capacity && _lruList.Last != null)
				{
					LinkedListNode<CacheItem> last = _lruList.Last;
					_cache.Remove(last.Value.Key);
					_lruList.RemoveLast();
				}
				LinkedListNode<CacheItem> linkedListNode = new LinkedListNode<CacheItem>(new CacheItem(key, value));
				_lruList.AddFirst(linkedListNode);
				_cache[key] = linkedListNode;
			}
			finally
			{
				_lock.ExitWriteLock();
			}
		}
	}

	public LruCache(int capacity)
	{
		if (capacity < 1)
		{
			throw new ArgumentOutOfRangeException("capacity", "Capacity must be at least 1.");
		}
		_capacity = capacity;
		_cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
		_lruList = new LinkedList<CacheItem>();
	}

	public bool TryGetValue(TKey key, out TValue value)
	{
		_lock.EnterUpgradeableReadLock();
		try
		{
			if (_cache.TryGetValue(key, out LinkedListNode<CacheItem> value2))
			{
				_lock.EnterWriteLock();
				try
				{
					_lruList.Remove(value2);
					_lruList.AddFirst(value2);
				}
				finally
				{
					_lock.ExitWriteLock();
				}
				value = value2.Value.Value;
				return true;
			}
			value = default(TValue);
			return false;
		}
		finally
		{
			_lock.ExitUpgradeableReadLock();
		}
	}

	public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
	{
		_lock.EnterUpgradeableReadLock();
		try
		{
			if (_cache.TryGetValue(key, out LinkedListNode<CacheItem> value))
			{
				_lock.EnterWriteLock();
				try
				{
					_lruList.Remove(value);
					_lruList.AddFirst(value);
				}
				finally
				{
					_lock.ExitWriteLock();
				}
				return value.Value.Value;
			}
			TValue val = valueFactory(key);
			_lock.EnterWriteLock();
			try
			{
				if (_cache.TryGetValue(key, out value))
				{
					_lruList.Remove(value);
					_lruList.AddFirst(value);
					return value.Value.Value;
				}
				while (_cache.Count >= _capacity && _lruList.Last != null)
				{
					LinkedListNode<CacheItem> last = _lruList.Last;
					_cache.Remove(last.Value.Key);
					_lruList.RemoveLast();
				}
				LinkedListNode<CacheItem> linkedListNode = new LinkedListNode<CacheItem>(new CacheItem(key, val));
				_lruList.AddFirst(linkedListNode);
				_cache[key] = linkedListNode;
				return val;
			}
			finally
			{
				_lock.ExitWriteLock();
			}
		}
		finally
		{
			_lock.ExitUpgradeableReadLock();
		}
	}

	public void Clear()
	{
		_lock.EnterWriteLock();
		try
		{
			_cache.Clear();
			_lruList.Clear();
		}
		finally
		{
			_lock.ExitWriteLock();
		}
	}

	public bool ContainsKey(TKey key)
	{
		_lock.EnterReadLock();
		try
		{
			return _cache.ContainsKey(key);
		}
		finally
		{
			_lock.ExitReadLock();
		}
	}
}
}
