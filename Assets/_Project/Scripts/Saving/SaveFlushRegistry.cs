using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    // Main-thread only, no locking: everything below is only ever touched from the thread Unity
    // calls its own lifecycle callbacks on.
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

        // Snapshotted first so a flush that unregisters something - itself included - cannot
        // invalidate the enumeration it is running inside of.
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
                    // Swallowed on purpose: this runs from OnApplicationPause/OnApplicationQuit,
                    // where one flushable failing must never stop every other one from getting its
                    // own chance.
                    Debug.LogError($"The save under '{flushable.SaveKey}' failed to flush on pause/quit: {exception.Message}");
                }
            }
        }
    }
}
