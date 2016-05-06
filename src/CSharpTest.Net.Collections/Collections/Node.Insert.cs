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
namespace CSharpTest.Net.Collections
{
	using System;
	using Values;

	partial class BPlusTree<TKey, TValue>
	{
		static readonly KeyValueUpdate<TKey, TValue> IgnoreUpdate = (k, v) => v;

		private InsertResult Insert<T>(NodePin thisLock, TKey key, ref T value, NodePin parent, int parentIx)
			where T : ICreateOrUpdateValue<TKey, TValue>
		{
			var me = thisLock.Ptr;
			if (me.Count == me.Size && parent != null)
			{
				using (var trans = _storage.BeginTransaction())
				{
					TKey splitAt;
					if (parent.Ptr.IsRoot) //Is root node
					{
						var rootNode = trans.BeginUpdate(parent);
						using (var newRoot = trans.Create(parent, false))
						{
							rootNode.ReplaceChild(0, thisLock.Handle, newRoot.Handle);
							newRoot.Ptr.Insert(0, new Element(default(TKey), thisLock.Handle));

							using (var next = Split(trans, ref thisLock, newRoot, 0, out splitAt, false))
							using (thisLock)
							{
								trans.Commit();
								GC.KeepAlive(thisLock);
								GC.KeepAlive(next);
							}

							return Insert(newRoot, key, ref value, parent, parentIx);
						}
					}

					trans.BeginUpdate(parent);
					using (var next = Split(trans, ref thisLock, parent, parentIx, out splitAt, false))
					using (thisLock)
					{
						trans.Commit();

						if (_keyComparer.Compare(key, splitAt) >= 0)
						{
							thisLock.Dispose();
							return Insert(next, key, ref value, parent, parentIx + 1);
						}
						next.Dispose();
						return Insert(thisLock, key, ref value, parent, parentIx);
					}
				}
			}
			if (parent != null)
				parent.Dispose(); //done with the parent lock.

			int ordinal;
			if (me.ExistsUsingBinarySearch(_itemComparer, new Element(key), out ordinal) && me.IsLeaf)
			{
				var updatedValue = me[ordinal].Payload;
				if (value.UpdateValue(key, ref updatedValue))
				{
					using (var trans = _storage.BeginTransaction())
					{
						me = trans.BeginUpdate(thisLock);
						me.SetValue(ordinal, key, updatedValue, _keyComparer);
						trans.UpdateValue(key, updatedValue);
						trans.Commit();
						return InsertResult.Updated;
					}
				}
				return InsertResult.Exists;
			}

			if (me.IsLeaf)
			{
				TValue newValue;
				if (value.CreateValue(key, out newValue))
				{
					using (var trans = _storage.BeginTransaction())
					{
						me = trans.BeginUpdate(thisLock);
						me.Insert(ordinal, new Element(key, newValue));
						trans.AddValue(key, newValue);
						trans.Commit();
						return InsertResult.Inserted;
					}
				}
				return InsertResult.NotFound;
			}

			if (ordinal >= me.Count) ordinal = me.Count - 1;
			using (var child = _storage.Lock(thisLock, me[ordinal].ChildNode))
				return Insert(child, key, ref value, thisLock, ordinal);
		}

		private NodePin Split(NodeTransaction trans, ref NodePin thisLock, NodePin parentLock, int parentIx, out TKey splitKey, bool leftHeavy)
		{
			var me = thisLock.Ptr;

			var prev = trans.Create(parentLock, thisLock.Ptr.IsLeaf);
			var next = trans.Create(parentLock, thisLock.Ptr.IsLeaf);
			try
			{
				int ix;
				var count = me.Count >> 1;
				if (leftHeavy)
				{
					var minSize = thisLock.Ptr.IsLeaf
						? _options.MinimumValueNodes
						: _options.MinimumChildNodes;
					count = me.Count - minSize;
				}

				for (ix = 0; ix < count; ix++)
					prev.Ptr.Insert(prev.Ptr.Count, me[ix]);

				splitKey = me[count].Key;

				if (!thisLock.Ptr.IsLeaf)
					next.Ptr.Insert(next.Ptr.Count, new Element(default(TKey), me[ix++].ChildNode));
				for (; ix < me.Count; ix++)
					next.Ptr.Insert(next.Ptr.Count, me[ix]);

				parentLock.Ptr.ReplaceChild(parentIx, thisLock.Handle, prev.Handle);
				parentLock.Ptr.Insert(parentIx + 1, new Element(splitKey, next.Handle));

				trans.Destroy(thisLock);
				thisLock = prev;
				return next;
			}
			catch
			{
				prev.Dispose();
				next.Dispose();
				throw;
			}
		}
	}
}