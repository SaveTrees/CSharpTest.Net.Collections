namespace CSharpTest.Net.Values
{
	using Collections;

	internal struct RemoveIfPredicate<TKey, TValue> : IRemoveValue<TKey, TValue>
	{
		private readonly KeyValuePredicate<TKey, TValue> _test;

		public RemoveIfPredicate(KeyValuePredicate<TKey, TValue> test)
		{
			_test = test;
		}

		bool IRemoveValue<TKey, TValue>.RemoveValue(TKey key, TValue value)
		{
			var canRemove = _test(key, value);

			return canRemove;
		}
	}
}
