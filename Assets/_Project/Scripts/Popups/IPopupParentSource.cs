using System.Threading;
using Company.ChestGame.Popups.Internal;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Popups
{
    // Where the shared popup canvas prefab comes from. It hands back the prefab, not an instance:
    // deciding when to instantiate it is IPopupParentProvider's job, and that decision is
    // deliberately deferred to the first popup rather than made while content is loading.
    public interface IPopupParentSource
    {
        UniTask<PopupParent> ReadAsync(CancellationToken ct);
    }
}
