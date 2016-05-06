namespace CSharpTest.Net.Values
{
	using System;
	using System.Collections.Generic;
	using Collections;

	internal struct InsertionInfo<TKey, TValue> : ICreateOrUpdateValue<TKey, TValue>
	{
		private readonly Converter<TKey, TValue> _factory;
		private readonly KeyValueUpdate<TKey, TValue> _updater;
		private readonly bool _canUpdate;
		public TValue Value { get; private set; }

		public bool CreateValue(TKey key, out TValue value)
		{
			if (_factory != null)
			{
				value = Value = _factory(key);
				return true;
			}
			value = Value;
			return true;
		}

		public bool UpdateValue(TKey key, ref TValue value)
		{
			if (!_canUpdate)
			{
				throw new DuplicateKeyException();
			}

			if (_updater != null)
			{
				Value = _updater(key, value);
			}

			var areEqual = EqualityComparer<TValue>.Default.Equals(value, Value);
			if (areEqual)
			{
				return false;
			}

			value = Value;
			return true;
		}

		public InsertionInfo(Converter<TKey, TValue> factory, KeyValueUpdate<TKey, TValue> updater)
			: this()
		{
			_factory = Check.NotNull(factory);
			_updater = updater;
			_canUpdate = updater != null;
		}

		public InsertionInfo(TValue addValue, KeyValueUpdate<TKey, TValue> updater)
			: this()
		{
			Value = addValue;
			_updater = updater;
			_canUpdate = updater != null;
		}
	}
}
