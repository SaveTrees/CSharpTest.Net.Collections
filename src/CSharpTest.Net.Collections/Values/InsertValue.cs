namespace CSharpTest.Net.Values
{
	using System.Collections.Generic;
	using Collections;

	internal struct InsertValue<TKey, TValue> : ICreateOrUpdateValue<TKey, TValue>
	{
		private readonly TValue _value;
		private readonly bool _canUpdate;

		public InsertValue(TValue value, bool canUpdate)
		{
			_value = value;
			_canUpdate = canUpdate;
		}

		public bool CreateValue(TKey key, out TValue value)
		{
			value = _value;
			return true;
		}

		public bool UpdateValue(TKey key, ref TValue value)
		{
			if (!_canUpdate)
			{
				throw new DuplicateKeyException();
			}

			var areEqual = EqualityComparer<TValue>.Default.Equals(value, _value);
			if (areEqual)
			{
				return false;
			}

			value = _value;
			return true;
		}
	}
}
