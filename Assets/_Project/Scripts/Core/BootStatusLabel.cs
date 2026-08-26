using TMPro;
using UnityEngine;

namespace Company.ChestGame.Core
{
    // The boot scene's half of IBootStatus: it holds the label that scene already had, so the
    // bootstrapper reports through the interface and never names a component.
    public class BootStatusLabel : MonoBehaviour, IBootStatus
    {
        [SerializeField] private TMP_Text _label;

        // Boot is the worst place to turn an unwired slot into a crash: the game would fail to
        // start over a message nobody would have read.
        public void Report(string message)
        {
            if (_label == null) return;

            _label.text = message;
        }
    }
}
