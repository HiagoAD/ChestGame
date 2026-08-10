using System;
using Company.ChestGame.Common;

namespace Company.ChestGame.Minigame
{
    // A minigame was requested that the catalog does not list.
    public class MinigameNotFoundException : ChestGameException
    {
        public Type ContainerType { get; }

        public MinigameNotFoundException(Type containerType)
            : base($"No minigame is registered for container type {containerType.Name}")
        {
            ContainerType = containerType;
        }
    }
}
