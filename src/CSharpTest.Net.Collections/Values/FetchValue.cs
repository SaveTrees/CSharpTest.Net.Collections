namespace CSharpTest.Net.Values
{
	using Collections;

	internal struct FetchValue<TKey, TValue> : ICreateOrUpdateValue<TKey, TValue>
	{
		public FetchValue(TValue value)
		{
			Value = value;
		}

		public TValue Value { get; private set; }

		public bool CreateValue(TKey key, out TValue value)
		{
			value = Value;
			return true;
		}

		public bool UpdateValue(TKey key, ref TValue value)
		{
			Value = value;
			return false;
		}
	}
}
