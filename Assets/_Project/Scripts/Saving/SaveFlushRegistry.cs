using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    /// <remarks>
    /// See docs/saving.md, "Why the flush stopped being one field".
    /// </remarks>
    public class SaveFlushRegistry : ISaveFlushRegistry
    {
        private readonly List<ISaveFlushable> _flushables = new();

        public IReadOnlyList<ISaveFlushable> Registered => _flushables;

        public void Register(ISaveFlushable flushable)
        {
            if (!flushable.CanFlushBlocking) throw SaveException.SchedulerCannotFlushBlocking(flushable.SaveKey);
            if (_flushables.Contains(flushable)) return;

            _flushables.Add(flushable);
        }

        public void Unregister(ISaveFlushable flushable) => _flushables.Remove(flushable);

        public void FlushAll()
        {
            foreach (ISaveFlushable flushable in _flushables.ToArray())
            {
                try
                {
                    flushable.FlushBlocking();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"The save under '{flushable.SaveKey}' failed to flush on pause/quit: {exception.Message}");
                }
            }
        }
    }
}
