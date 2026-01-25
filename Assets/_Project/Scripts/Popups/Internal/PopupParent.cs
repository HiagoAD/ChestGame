using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    public class PopupParent : MonoBehaviour
    {
        [SerializeField] private Transform _targetTransform;

        public Transform Target => _targetTransform;
    }
}
