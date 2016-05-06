#region Copyright 2011-2014 by Roger Knapp, Licensed under the Apache License, Version 2.0
/* Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
#endregion
using System.Collections.Generic;

namespace CSharpTest.Net.Collections
{
	partial class BPlusTree<TKey, TValue>
	{
		private bool Seek(NodePin thisLock, TKey key, out NodePin pin, out int offset)
		{
			NodePin myPin = thisLock, nextPin = null;
			try
			{
				while (myPin != null)
				{
					var me = myPin.Ptr;

					var isValueNode = me.IsLeaf;
					int ordinal;
					if (me.ExistsUsingBinarySearch(_itemComparer, new Element(key), out ordinal) && isValueNode)
					{
						pin = myPin;
						myPin = null;
						offset = ordinal;
						return true;
					}
					if (isValueNode)
						break; // not found.

					nextPin = _storage.Lock(myPin, me[ordinal].ChildNode);
					myPin.Dispose();
					myPin = nextPin;
					nextPin = null;
				}
			}
			finally
			{
				if (myPin != null) myPin.Dispose();
				if (nextPin != null) nextPin.Dispose();
			}

			pin = null;
			offset = -1;
			return false;
		}

		private bool Search(NodePin thisLock, TKey key, ref TValue value)
		{
			NodePin pin;
			int offset;
			if (Seek(thisLock, key, out pin, out offset))
				using (pin)
				{
					value = pin.Ptr[offset].Payload;
					return true;
				}
			return false;
		}

		private bool SeekToEdge(NodePin thisLock, bool first, out NodePin pin, out int offset)
		{
			NodePin myPin = thisLock, nextPin = null;
			try
			{
				while (myPin != null)
				{
					var me = myPin.Ptr;
					var ordinal = first
						? 0
						: me.Count - 1;
					if (me.IsLeaf)
					{
						if (ordinal < 0 || ordinal >= me.Count)
							break;

						pin = myPin;
						myPin = null;
						offset = ordinal;
						return true;
					}

					nextPin = _storage.Lock(myPin, me[ordinal].ChildNode);
					myPin.Dispose();
					myPin = nextPin;
					nextPin = null;
				}
			}
			finally
			{
				if (myPin != null) myPin.Dispose();
				if (nextPin != null) nextPin.Dispose();
			}

			pin = null;
			offset = -1;
			return false;
		}

		private bool TryGetEdge(NodePin thisLock, bool first, out KeyValuePair<TKey, TValue> item)
		{
			NodePin pin;
			int offset;
			if (SeekToEdge(thisLock, first, out pin, out offset))
			{
				using (pin)
				{
					item = new KeyValuePair<TKey, TValue>(
						pin.Ptr[offset].Key,
						pin.Ptr[offset].Payload);
					return true;
				}
			}
			item = default(KeyValuePair<TKey, TValue>);
			return false;
		}

		private bool Update<T>(NodePin thisLock, TKey key, ref T value) where T : IUpdateValue<TKey, TValue>
		{
			NodePin pin;
			int offset;
			var found = Seek(thisLock, key, out pin, out offset);
			if (!found)
			{
				return false;
			}

			using (pin)
			{
				var newValue = pin.Ptr[offset].Payload;
				var updated = value.UpdateValue(key, ref newValue);
				if (!updated)
				{
					return false;
				}

				using (var trans = _storage.BeginTransaction())
				{
					trans.BeginUpdate(pin);
					pin.Ptr.SetValue(offset, key, newValue, _keyComparer);
					trans.UpdateValue(key, newValue);
					trans.Commit();
					return true;
				}
			}
		}

		private int CountValues(NodePin thisLock)
		{
			if (thisLock.Ptr.IsLeaf)
			{
				return thisLock.Ptr.Count;
			}

			var count = 0;
			for (var i = 0; i < thisLock.Ptr.Count; i++)
			{
				using (var child = _storage.Lock(thisLock, thisLock.Ptr[i].ChildNode))
				{
					count += CountValues(child);
				}
			}

			return count;
		}
	}
}