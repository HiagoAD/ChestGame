using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Popups
{
    /// <summary>
    /// Where the authored popup entries come from. <c>PopupCatalog</c> takes a plain list and knows
    /// nothing about loading; this is the only half a different loading technology has to replace.
    /// </summary>
    public interface IPopupListSource
    {
        UniTask<IReadOnlyList<PopupBase>> ReadAsync(CancellationToken ct);
    }
}
