using System;
using Company.ChestGame.Common;

namespace Company.ChestGame.Minigame
{
    // A minigame was requested that the catalog does not list, by container type or by id.
    public class MinigameNotFoundException : ChestGameException
    {
        public Type ContainerType { get; }
        public string Id { get; }

        public MinigameNotFoundException(Type containerType)
            : base($"No minigame is registered for container type {containerType.Name}")
        {
            ContainerType = containerType;
        }

        public MinigameNotFoundException(string id)
            : base($"No minigame is registered with id '{id}'")
        {
            Id = id;
        }
    }
}
