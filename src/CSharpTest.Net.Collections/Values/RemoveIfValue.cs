namespace CSharpTest.Net.Values
{
	using System.Collections.Generic;
	using Collections;

	internal struct RemoveIfValue<TKey, TValue> : IRemoveValue<TKey, TValue>
	{
		private readonly TValue _value;

		public RemoveIfValue(TKey key, TValue value)
		{
			_value = value;
		}

		bool IRemoveValue<TKey, TValue>.RemoveValue(TKey key, TValue value)
		{
			var areEqual = EqualityComparer<TValue>.Default.Equals(value, _value);

			return areEqual;
		}
	}
}