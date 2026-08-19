using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Popups
{
    // Where the authored popup entries come from. PopupCatalog takes a plain list and knows nothing
    // about loading; this is the half that does, and it is the only half a different loading
    // technology has to replace.
    public interface IPopupListSource
    {
        UniTask<IReadOnlyList<PopupBase>> ReadAsync(CancellationToken ct);
    }
}
