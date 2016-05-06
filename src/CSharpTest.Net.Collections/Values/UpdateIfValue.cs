namespace CSharpTest.Net.Values
{
	using System.Collections.Generic;
	using Collections;

	internal struct UpdateIfValue<TKey, TValue> : IUpdateValue<TKey, TValue>
	{
		private readonly TValue _comparisonValue;
		private readonly TValue _newValue;

		public UpdateIfValue(TValue newValue, TValue comparisonValue)
			: this()
		{
			_newValue = newValue;
			_comparisonValue = comparisonValue;
		}

		public bool UpdateValue(TKey key, ref TValue value)
		{
			var areEqual = EqualityComparer<TValue>.Default.Equals(value, _comparisonValue);
			if (!areEqual)
			{
				return false;
			}

			Updated = true;
			areEqual = EqualityComparer<TValue>.Default.Equals(value, _newValue);
			if (areEqual)
			{
				return false;
			}

			value = _newValue;
			return true;
		}
		public bool Updated { get; private set; }
	}
}
