using TMPro;
using UnityEngine;

namespace Company.ChestGame.Core
{
    // The boot scene's half of IBootStatus: it holds the label that scene already had, and does
    // nothing else. Registered in the root scope by GameLifetimeScope, which is the only thing that
    // can see it, so the bootstrapper reports through the interface and never names a component.
    public class BootStatusLabel : MonoBehaviour, IBootStatus
    {
        [SerializeField] private TMP_Text _label;

        // An unwired slot is the common authoring mistake, and boot is the worst possible place to
        // turn one into a crash: the game would fail to start over a message nobody would have read.
        public void Report(string message)
        {
            if (_label == null) return;

            _label.text = message;
        }
    }
}
