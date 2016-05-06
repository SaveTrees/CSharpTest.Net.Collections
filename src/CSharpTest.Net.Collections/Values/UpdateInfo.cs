namespace CSharpTest.Net.Values
{
	using System.Collections.Generic;
	using Collections;

	internal struct UpdateInfo<TKey, TValue> : IUpdateValue<TKey, TValue>
	{
		private TValue _oldValue;
		private readonly TValue _newValue;
		private readonly KeyValueUpdate<TKey, TValue> _fnUpdate;

		public UpdateInfo(KeyValueUpdate<TKey, TValue> fnUpdate) : this()
		{
			_fnUpdate = fnUpdate;
		}

		public UpdateInfo(TValue newValue) : this()
		{
			_newValue = newValue;
		}

		public bool UpdateValue(TKey key, ref TValue value)
		{
			Updated = true;
			_oldValue = value;
			value = _fnUpdate == null
				? _newValue
				: _fnUpdate(key, value);

			//var areEqual = EqualityComparer<TValue>.Default.Equals(value, _oldValue);
			//return !areEqual;
			return true;
		}

		public bool Updated { get; private set; }
	}
}