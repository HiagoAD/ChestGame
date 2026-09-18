using System;

namespace Company.ChestGame.Mvc
{
    /// <summary>
    /// Holds a screen's rules and session state. Implementations are plain C# and never
    /// <see cref="UnityEngine.MonoBehaviour"/>, so they run in edit mode with no scene.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md.
    /// </remarks>
    public interface IController : IDisposable
    {
    }
}
