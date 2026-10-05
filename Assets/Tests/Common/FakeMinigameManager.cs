using System;
using System.Collections.Generic;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Core;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Hands back whatever container a test registered under an id with <see cref="Set"/>, and
    /// records every id asked for.
    /// </summary>
    public class FakeMinigameManager : IMinigameManager
    {
        private readonly Dictionary<string, MinigameContainer> _containersById = new();

        public readonly List<string> GetCalls = new();

        public void Set(string id, MinigameContainer container) => _containersById[id] = container;

        public TContainer Get<TContainer>() where TContainer : MinigameContainer =>
            throw new NotSupportedException($"{nameof(FakeMinigameManager)} only resolves minigames by id");

        public MinigameContainer Get(string id)
        {
            GetCalls.Add(id);
            return _containersById[id];
        }
    }
}
