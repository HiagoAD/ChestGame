using System.Threading;
using Company.ChestGame.Popups.Internal;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Popups
{
    /// <summary>
    /// Where the shared popup canvas prefab comes from. It hands back the prefab, not an instance:
    /// when to instantiate it is <see cref="IPopupParentProvider"/>'s call, deferred to the first
    /// popup.
    /// </summary>
    public interface IPopupParentSource
    {
        UniTask<PopupParent> ReadAsync(CancellationToken ct);
    }
}
