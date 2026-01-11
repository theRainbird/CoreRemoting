using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

public partial class NeoBinarySerializer
{
	/// <summary>
	/// Simple object pool for performance optimization.
	/// </summary>
	private class ObjectPool<T> where T : class
	{
		private readonly Func<T> _factory;
		private readonly Action<T> _reset;
		private readonly object _lock = new();
		private readonly List<T> _pool = new();

		public ObjectPool(Func<T> factory, Action<T> reset)
		{
			_factory = factory;
			_reset = reset;
		}

		public T Get()
		{
			lock (_lock)
			{
				if (_pool.Count > 0)
				{
					var item = _pool[_pool.Count - 1];
					_pool.RemoveAt(_pool.Count - 1);
					return item;
				}
			}

			return _factory();
		}

		public void Return(T item)
		{
			if (item == null) return;
			_reset(item);
			lock (_lock)
			{
				if (_pool.Count < 10) // Limit pool size
					_pool.Add(item);
			}
		}
	}
}