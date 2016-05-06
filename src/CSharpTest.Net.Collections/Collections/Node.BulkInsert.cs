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
using System;
using System.Collections.Generic;
using System.Threading;
using CSharpTest.Net.Interfaces;
using System.IO;
using CSharpTest.Net.Serialization;
using CSharpTest.Net.Synchronization;

namespace CSharpTest.Net.Collections
{
    /// <summary>
    /// Options for bulk insertion
    /// </summary>
    public class BulkInsertOptions
    {
        private bool _inputIsSorted;
        private bool _commitOnCompletion;
        private bool _replaceContents;
        private DuplicateHandling _duplicateHandling;

        /// <summary> Constructs with defaults: false/RaisesException </summary>
        public BulkInsertOptions()
        {
            _replaceContents = false;
            _commitOnCompletion = true;
            _inputIsSorted = false;
            _duplicateHandling = DuplicateHandling.RaisesException;
        }

        /// <summary> Gets or sets a value that controls input presorting </summary>
        public bool InputIsSorted
        {
            get { return _inputIsSorted; }
            set { _inputIsSorted = value; }
        }

        /// <summary> Gets or sets the handling for duplicate key collisions </summary>
        public DuplicateHandling DuplicateHandling
        {
            get { return _duplicateHandling; }
            set { _duplicateHandling = value; }
        }

        /// <summary> When true (default) BulkInsert will call CommitChanges() on successfull completion </summary>
        public bool CommitOnCompletion
        {
            get { return _commitOnCompletion; }
            set { _commitOnCompletion = value; }
        }

        /// <summary> When false merges the data with the existing contents, set to true to replace all content </summary>
        public bool ReplaceContents
        {
            get { return _replaceContents; }
            set { _replaceContents = value; }
        }
    }

    partial class BPlusTree<TKey, TValue>
    {
        private class AddRangeInfo : IDisposable
        {
            public readonly bool AllowUpdate;
            private bool _continue;
            private readonly IEnumerator<KeyValuePair<TKey, TValue>> _values;

            public AddRangeInfo(bool allowUpdate, IEnumerable<KeyValuePair<TKey, TValue>> values)
            {
                AllowUpdate = allowUpdate;
                _values = values.GetEnumerator();
                MoveNext();
            }

            public void Dispose()
            {
                _continue = false;
                _values.Dispose();
            }

            public KeyValuePair<TKey, TValue> Current { get { return _values.Current; } }

            public void MoveNext()
            {
                _continue = _values.MoveNext();
            }

            public bool IsComplete { get { return _continue == false; } }
        }
        private struct KeyRange
        {
            public KeyRange(IComparer<TKey> keyComparer)
                : this()
            {
                _keyComparer = keyComparer;
            }
            private readonly IComparer<TKey> _keyComparer;
            private bool _hasMinKey, _hasMaxKey;
            private TKey _minKey, _maxKey;
            public void SetMinKey(TKey key) { _hasMinKey = true; _minKey = key; }
            public void SetMaxKey(TKey key) { _hasMaxKey = true; _maxKey = key; }
            public bool IsKeyInRange(TKey key)
            {
                if (_hasMinKey && _keyComparer.Compare(key, _minKey) < 0) return false;
                if (_hasMaxKey && _keyComparer.Compare(key, _maxKey) >= 0) return false;
                return true;
            }
        }

        private int AddRange(NodePin thisLock, ref KeyRange range, AddRangeInfo value, NodePin parent, int parentIx)
        {
            var counter = 0;
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

                            using (var next = Split(trans, ref thisLock, newRoot, 0, out splitAt, true))
                            using (thisLock)
                            {
                                trans.Commit();
                                GC.KeepAlive(thisLock);
                                GC.KeepAlive(next);
                            }

                            return AddRange(newRoot, ref range, value, parent, parentIx);
                        }
                    }

                    trans.BeginUpdate(parent);
                    using (var next = Split(trans, ref thisLock, parent, parentIx, out splitAt, true))
                    using (thisLock)
                    {
                        trans.Commit();

                        if (_keyComparer.Compare(value.Current.Key, splitAt) >= 0)
                        {
                            thisLock.Dispose();
                            range.SetMinKey(splitAt);
                            return AddRange(next, ref range, value, parent, parentIx + 1);
                        }
                        next.Dispose();
                        range.SetMaxKey(splitAt);
                        return AddRange(thisLock, ref range, value, parent, parentIx);
                    }
                }
            }

            if (parent != null)
                parent.Dispose();

            if (me.IsLeaf)
            {
                using (var trans = _storage.BeginTransaction())
                {
                    me = trans.BeginUpdate(thisLock);
                    var inserted = 0;

                    while (me.Count < me.Size && !value.IsComplete && range.IsKeyInRange(value.Current.Key))
                    {
                        int ordinal;
                        var exists = me.ExistsUsingBinarySearch(_itemComparer, new Element(value.Current.Key), out ordinal);
                        DuplicateKeyException.Assert(!exists || value.AllowUpdate);

                        if (exists)
                        {
                            me.SetValue(ordinal, value.Current.Key, value.Current.Value, _keyComparer);
                            trans.UpdateValue(value.Current.Key, value.Current.Value);
                        }
                        else
                        {
                            me.Insert(ordinal, new Element(value.Current.Key, value.Current.Value));
                            trans.AddValue(value.Current.Key, value.Current.Value);
                            inserted++;
                        }
                        counter++;
                        value.MoveNext();
                    }
                    trans.Commit();

                    if (_hasCount && inserted > 0)
                    {
                        int count = _count, test;
                        while (count != (test = Interlocked.CompareExchange(ref _count, count + inserted, count)))
                            count = test;
                    }
                }
            }
            else
            {
                int ordinal;
                me.ExistsUsingBinarySearch(_itemComparer, new Element(value.Current.Key), out ordinal);

                if (ordinal >= me.Count)
                    ordinal = me.Count - 1;

                if (ordinal > 0) range.SetMinKey(me[ordinal - 1].Key);
                if (ordinal < me.Count - 1) range.SetMaxKey(me[ordinal + 1].Key);

                using (var child = _storage.Lock(thisLock, me[ordinal].ChildNode))
                    counter += AddRange(child, ref range, value, thisLock, ordinal);
            }
            return counter;
        }
        
        /// <summary>
        /// Rewrite the entire BTree as a transaction to include the provided items.  This method is Thread safe.
        /// If the input is already sorted, use BulkInsertOptions overload to specify InputIsSorted = true.
        /// </summary>
        public int BulkInsert(IEnumerable<KeyValuePair<TKey, TValue>> items)
        { return BulkInsert(items, new BulkInsertOptions()); }
        /// <summary>
        /// Rewrite the entire BTree as a transaction to include the provided items.  This method is Thread safe.
        /// If the input is already sorted, use BulkInsertOptions overload to specify InputIsSorted = true.
        /// </summary>
        public int BulkInsert(IEnumerable<KeyValuePair<TKey, TValue>> items, BulkInsertOptions bulkOptions)
        {
            NodePin oldRoot = null;
            if (bulkOptions.InputIsSorted == false)
            {
                var kvserializer = new KeyValueSerializer<TKey, TValue>(_options.KeySerializer, _options.ValueSerializer);
                items = new OrderedKeyValuePairs<TKey, TValue>(_options.KeyComparer, items, kvserializer)
                    {
                        DuplicateHandling = bulkOptions.DuplicateHandling
                    };
            }

            var handles = new List<IStorageHandle>();
            try
            {
                var counter = 0;
                using (var root = LockRoot(LockType.Insert, "Merge", false))
                {
                    if (root.Pin.Ptr.Count != 1)
                        throw new InvalidDataException();

                    var oldRootHandle = root.Pin.Ptr[0].ChildNode;
                    oldRoot = _storage.Lock(root.Pin, oldRootHandle);

                    if (oldRoot.Ptr.Count == 0 || bulkOptions.ReplaceContents)
                    {
                        // Currently empty, so just enforce duplicate keys...
                        items = OrderedKeyValuePairs<TKey, TValue>
                            .WithDuplicateHandling(items,
                                                   new KeyValueComparer<TKey, TValue>(_options.KeyComparer),
                                                   bulkOptions.DuplicateHandling);
                    }
                    else 
                    {
                        // Merging with existing data and enforce duplicate keys...
                        items = OrderedKeyValuePairs<TKey, TValue>
                            .Merge(_options.KeyComparer, bulkOptions.DuplicateHandling, EnumerateNodeContents(oldRoot), items);
                    }

                    var newtree = BulkWrite(handles, ref counter, items);
                    if (newtree == null) // null when enumeration was empty
                        return 0;

                    using (var trans = _storage.BeginTransaction())
                    {
                        var rootNode = trans.BeginUpdate(root.Pin);
                        rootNode.ReplaceChild(0, oldRootHandle, new NodeHandle(newtree.StorageHandle));
                        trans.Commit();
                    }

                    _count = counter;
                }

                //point of no return...
                handles.Clear();
                DeleteTree(oldRoot);
                oldRoot = null;

                if (bulkOptions.CommitOnCompletion)
                {
                    //Since transaction logs do not deal with bulk-insert, we need to commit our current state
                    Commit();
                }
                return counter;
            }
            catch
            {
                if (oldRoot != null)
                    oldRoot.Dispose();

                foreach(var sh in handles)
                {
                    try { _storage.Storage.Destroy(sh); }
                    catch (ThreadAbortException) { throw; }
                    catch { continue; }
                }
                throw;
            }
        }

        private Node BulkWrite(ICollection<IStorageHandle> handles, ref int counter, IEnumerable<KeyValuePair<TKey, TValue>> itemsEnum)
        {
            var working = new List<Node>();
            Node leafNode = null;
            
            using (var items = itemsEnum.GetEnumerator())
            {
                var more = items.MoveNext();
                while (more)
                {
                    var handle = new NodeHandle(_storage.Storage.Create());
                    handles.Add(handle.StoreHandle);

                    leafNode = new Node(handle.StoreHandle, _options.MaximumValueNodes);

                    while (leafNode.Count < leafNode.Size && more)
                    {
                        leafNode.Insert(leafNode.Count, new Element(items.Current.Key, items.Current.Value));
                        counter++;
                        more = items.MoveNext();
                    }
                    _storage.Storage.Update(handle.StoreHandle, _storage.NodeSerializer, leafNode);
                    leafNode.ToReadOnly();

                    if (!more && working.Count == 0)
                        return leafNode;

                    InsertWorkingNode(handles, working, working.Count - 1, new Element(leafNode[0].Key, handle));
                }
            }

            if (leafNode == null)
                return null;

            if (leafNode.Count < _options.MinimumValueNodes)
            {
                working.Add(leafNode.CloneForWrite(LockType.Insert));
            }

            // Reballance of right-edge
            for (var i = 1; i < working.Count; i++)
            {
                var me = working[i];
                var isleaf = me.IsLeaf;
                var limitMin = isleaf ? _options.MinimumValueNodes : _options.MinimumChildNodes;
                if (me.Count < limitMin)
                {
                    var parent = working[i - 1];
                    var prev = parent.Count - 2;
                    Node prevNode;
                    var success = _storage.Storage.TryGetNode(parent[prev].ChildNode.StoreHandle,
                                                               out prevNode, _storage.NodeSerializer);
                    AssertionFailedException.Assert(success);
                    prevNode = prevNode.CloneForWrite(LockType.Insert);
                    if (!isleaf)
                        me.ReplaceKey(0, parent[parent.Count - 1].Key);

                    while (me.Count < limitMin)
                    {
                        var item = prevNode[prevNode.Count - 1];
                        if (me.Count + 1 == limitMin)
                        {
                            if (isleaf) me.Insert(0, item);
                            else me.Insert(0, new Element(default(TKey), item.ChildNode));
                            parent.ReplaceKey(parent.Count - 1, item.Key);
                        }
                        else
                            me.Insert(0, item);
                        prevNode.Remove(prevNode.Count - 1, item, _keyComparer);
                    }

                    _storage.Storage.Update(prevNode.StorageHandle, _storage.NodeSerializer, prevNode);
                    prevNode.ToReadOnly();
                }
            }
            // Save the remaining nodes
            for (var i = working.Count - 1; i >= 0; i--)
            {
                _storage.Storage.Update(working[i].StorageHandle, _storage.NodeSerializer, working[i]);
                working[i].ToReadOnly();
            }

            return working[0];
        }

        private void InsertWorkingNode(ICollection<IStorageHandle> handles, List<Node> working, int index, Element child)
        {
            if(index < 0)
            {
                working.Insert(0, new Node(_storage.Storage.Create(), _options.MaximumChildNodes));
                handles.Add(working[0].StorageHandle);

                if (working.Count > 1)
                    working[0].Insert(0, new Element(default(TKey), new NodeHandle(working[1].StorageHandle)));
                index++;
            }
            var parent = working[index];

            if (parent.Count == parent.Size)
            {
                _storage.Storage.Update(parent.StorageHandle, _storage.NodeSerializer, parent);
                parent.ToReadOnly();

                parent = new Node(_storage.Storage.Create(), _options.MaximumChildNodes);
                handles.Add(parent.StorageHandle);

                var count = working.Count;
                InsertWorkingNode(handles, working, index - 1, new Element(child.Key, new NodeHandle(parent.StorageHandle)));
                if (count < working.Count)
                    index++;
                working[index] = parent;
            }

            if(parent.Count == 0)
                parent.Insert(parent.Count, new Element(default(TKey), child.ChildNode));
            else
                parent.Insert(parent.Count, child);
        }

        /// <summary>
        /// Exclusive access, deep-locking enumeration for bulk-insert, essentially this enumerates
        /// while at the same time it chases existing writers out of the tree.
        /// </summary>
        private IEnumerable<KeyValuePair<TKey, TValue>> EnumerateNodeContents(NodePin root)
        {
            if (root.Ptr.IsLeaf)
            {
                for (var ix = 0; ix < root.Ptr.Count; ix++)
                    yield return root.Ptr[ix].ToKeyValuePair();
                yield break;
            }

            var todo = new Stack<KeyValuePair<NodePin, int>>();
            todo.Push(new KeyValuePair<NodePin, int>(root, 0));
            try
            {
                while (todo.Count > 0)
                {
                    var cur = todo.Pop();
                    if (cur.Value == cur.Key.Ptr.Count)
                    {
                        if (todo.Count == 0)
                            yield break;
                        cur.Key.Dispose();
                        continue;
                    }
                    todo.Push(new KeyValuePair<NodePin, int>(cur.Key, cur.Value + 1));

                    var child = _storage.Lock(cur.Key, cur.Key.Ptr[cur.Value].ChildNode);
                    if (child.Ptr.IsLeaf)
                    {
                        using (child)
                        {
                            for (var ix = 0; ix < child.Ptr.Count; ix++)
                                yield return child.Ptr[ix].ToKeyValuePair();
                        }
                    }
                    else
                    {
                        todo.Push(new KeyValuePair<NodePin, int>(child, 0));
                    }
                }
            }
            finally
            {
                while (todo.Count > 1)
                    todo.Pop().Key.Dispose();
            }
        }

        private void DeleteTree(NodePin pin)
        {
            var children = new List<NodeHandle>();

            if (!pin.Ptr.IsLeaf)
            {
                for (var i = 0; i < pin.Ptr.Count; i++)
                    children.Add(pin.Ptr[i].ChildNode);
            }

            try
            {
                using (var trans = _storage.BeginTransaction())
                {
                    trans.Destroy(pin);
                    trans.Commit();
                }
            }
            finally
            {
                if (children.Count > 0)
                {
                    foreach (var h in children)
                    {
                        using (var ch = _storage.Lock(pin, h))
                        {
                            DeleteTree(ch);
                        }
                    }
                }
            }
        }
    }
}
