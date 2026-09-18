using Company.ChestGame.Currency;
using Company.ChestGame.Rewards;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="RewardReceivedPopup"/>'s icon mapping for a <see cref="CurrencyType"/> the
    /// popup does not know how to show.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
    public class RewardReceivedPopupTests
    {
        private GameObject _popupObject;

        [TearDown]
        public void TearDown()
        {
            if (_popupObject != null) Object.DestroyImmediate(_popupObject);
        }

        [Test]
        public void Initialize_WithAnUnmappedCurrencyType_ThrowsUnmappedCurrencyIcon()
        {
            _popupObject = new GameObject("RewardReceivedPopup");
            _popupObject.SetActive(false);
            RewardReceivedPopup popup = _popupObject.AddComponent<RewardReceivedPopup>();
            CurrencyType unmapped = (CurrencyType)99;

            UnmappedCurrencyIconException error = Assert.Throws<UnmappedCurrencyIconException>(
                () => popup.Initialize(new RewardReceivedPopupData(unmapped, 10)));

            Assert.AreEqual(unmapped, error.CurrencyType);
        }
    }
}
