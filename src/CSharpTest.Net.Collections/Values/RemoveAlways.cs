namespace CSharpTest.Net.Values
{
	using Collections;

	struct RemoveAlways<TKey, TValue> : IRemoveValue<TKey, TValue>
	{
		private bool _removed;
		private TValue _value;

		public bool TryGetValue(out TValue value)
		{
			value = _value;
			return _removed;
		}

		bool IRemoveValue<TKey, TValue>.RemoveValue(TKey key, TValue value)
		{
			_value = value;
			_removed = true;
			return _removed;
		}
	}
}
