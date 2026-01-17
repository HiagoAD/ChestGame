using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TapNation.Modules.Utils
{
    public class BidirectionalDictionary<T1, T2> : IDictionary<T1, T2>, IReadOnlyDictionary<T1, T2>
    {
        private Dictionary<T1, T2> _forward = new();
        private Dictionary<T2, T1> _reverse = new();


        public BidirectionalDictionary()
        {
        }

        public BidirectionalDictionary(IDictionary<T1, T2> dictionary)
        {
            foreach (KeyValuePair<T1, T2> entry in dictionary)
            {
                Add(entry);
            }
        }

        public BidirectionalDictionary(IDictionary<T2, T1> dictionary)
        {
            foreach (KeyValuePair<T2, T1> entry in dictionary)
            {
                Add(entry);
            }
        }

        public BidirectionalDictionary(IEnumerable<KeyValuePair<T1, T2>> collection)
        {
            foreach (KeyValuePair<T1, T2> entry in collection)
            {
                Add(entry);
            }
        }

        public BidirectionalDictionary(IEqualityComparer<T1> comparer1, IEqualityComparer<T2> comparer2)
        {
            _forward = new(comparer1);
            _reverse = new(comparer2);
        }

        public BidirectionalDictionary(int capacity)
        {
            _forward = new(capacity);
            _reverse = new(capacity);
        }

        public BidirectionalDictionary(IDictionary<T1, T2> dictionary, IEqualityComparer<T1> comparer1,
            IEqualityComparer<T2> comparer2)
        {
            _forward = new(comparer1);
            _reverse = new(comparer2);
            foreach (KeyValuePair<T1, T2> entry in dictionary)
            {
                Add(entry);
            }
        }

        public BidirectionalDictionary(IEnumerable<KeyValuePair<T1, T2>> collection, IEqualityComparer<T1> comparer1,
            IEqualityComparer<T2> comparer2)
        {
            _forward = new(comparer1);
            _reverse = new(comparer2);
            foreach (KeyValuePair<T1, T2> entry in collection)
            {
                Add(entry);
            }
        }

        public BidirectionalDictionary(int capacity, IEqualityComparer<T1> comparer1, IEqualityComparer<T2> comparer2)
        {
            _forward = new(capacity, comparer1);
            _reverse = new(capacity, comparer2);
        }


        public T2 this[T1 key]
        {
            get => _forward[key];
            set => _forward[key] = value;
        }

        public T1 this[T2 key]
        {
            get => _reverse[key];
            set => _reverse[key] = value;
        }

        ICollection<T1> IDictionary<T1, T2>.Keys => _forward.Keys;
        public IEnumerable<T1> Keys => ((IReadOnlyDictionary<T1, T2>)_forward).Keys;

        ICollection<T2> IDictionary<T1, T2>.Values => _reverse.Keys;

        public IEnumerable<T2> Values => ((IReadOnlyDictionary<T2, T1>)_reverse).Keys;

        public int Count
        {
            get
            {
                Debug.Assert(_forward.Count == _reverse.Count,
                    "Internal error: Dictionaries with different sizes. Assuming smaller");
                return Math.Min(_forward.Count, _reverse.Count);
            }
        }

        public bool IsReadOnly => false;


        public void Add(T1 key, T2 value)
        {
            _forward.Add(key, value);
            _reverse.Add(value, key);
        }

        public void Add(KeyValuePair<T1, T2> item)
        {
            Add(item.Key, item.Value);
        }

        public void Add(T2 key, T1 value)
        {
            _reverse.Add(key, value);
            _forward.Add(value, key);
        }

        public void Add(KeyValuePair<T2, T1> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            _forward.Clear();
            _reverse.Clear();
        }

        public bool ContainsKey(T1 key) => _forward.ContainsKey(key);
        public bool ContainsKey(T2 key) => _reverse.ContainsKey(key);

        public bool Contains(KeyValuePair<T1, T2> item) => ContainsKey(item.Key) && ContainsKey(item.Value);
        public bool Contains(KeyValuePair<T2, T1> item) => ContainsKey(item.Key) && ContainsKey(item.Value);

        public bool Contains(T1 item) => ContainsKey(item);
        public bool Contains(T2 item) => ContainsKey(item);


        public void CopyTo(KeyValuePair<T1, T2>[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            if (arrayIndex < 0 || arrayIndex > array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));

            if ((array.Length - arrayIndex) < _forward.Count)
                throw new ArgumentException("The destination array has insufficient space.");

            int i = arrayIndex;
            foreach (KeyValuePair<T1, T2> kvp in _forward)
            {
                array[i++] = kvp;
            }
        }

        public void CopyTo(KeyValuePair<T2, T1>[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            if (arrayIndex < 0 || arrayIndex > array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));

            if ((array.Length - arrayIndex) < _reverse.Count)
                throw new ArgumentException("The destination array has insufficient space.");

            int i = arrayIndex;
            foreach (KeyValuePair<T2, T1> kvp in _reverse)
            {
                array[i++] = kvp;
            }
        }

        public IEnumerator<KeyValuePair<T1, T2>> GetEnumerator() => _forward.GetEnumerator();

        public bool Remove(T1 key)
        {
            if (!_forward.ContainsKey(key)) return false;

            T2 value = _forward[key];
            return _forward.Remove(key) && _reverse.Remove(value);
        }

        public bool Remove(T2 key)
        {
            if (!_reverse.ContainsKey(key)) return false;

            T1 value = _reverse[key];
            return _reverse.Remove(key) && _forward.Remove(value);
        }

        public bool Remove(KeyValuePair<T1, T2> item)
        {
            if (!(_forward.ContainsKey(item.Key) && _reverse.ContainsKey(item.Value))) return false;
            return _forward.Remove(item.Key) && _reverse.Remove(item.Value);
        }

        public bool Remove(KeyValuePair<T2, T1> item)
        {
            if (!(_reverse.ContainsKey(item.Key) && _forward.ContainsKey(item.Value))) return false;
            return _reverse.Remove(item.Key) && _forward.Remove(item.Value);
        }

        public bool TryGetValue(T1 key, out T2 value) => _forward.TryGetValue(key, out value);
        public bool TryGetValue(T2 key, out T1 value) => _reverse.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}