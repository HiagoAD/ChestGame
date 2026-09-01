using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // The one place that turns a PoolStrategy into a pool, and the one place that knows what a
    // holder has to be. Static and stateless, like CatalogBuilder and ConfigValidation.
    public static class PoolFactory
    {
        // holderParent is where a holder gets built if the strategy needs one. Which strategies
        // need one is this method's decision, not the caller's - hence no ready-made holder
        // parameter.
        public static IPrefabPool<T> Create<T>(PoolStrategy strategy, T prefab, Transform holderParent, int maxSize, string holderName)
            where T : Component
        {
            // The baseline parks nothing, so a holder for it would be an empty object in the
            // hierarchy claiming this screen parks something.
            if (strategy == PoolStrategy.DirectSpawner) return new DirectSpawner<T>(prefab);

            Transform holder = CreateHolder(holderParent, holderName);

            return strategy switch
            {
                PoolStrategy.ParkedPool => new ParkedPool<T>(prefab, holder, maxSize),
                PoolStrategy.UnityPool => new UnityPool<T>(prefab, holder, maxSize),

                // A working pool rather than a throw, so a serialized field left on an enum member
                // this switch has not heard of still comes up with a working board.
                _ => new ActivationPool<T>(prefab, holder, maxSize)
            };
        }

        // The parent must not be whatever carries a layout group: parking under one makes every release
        // and every get dirty a rebuild, which is most of what pooling was supposed to save. The caller
        // passes the parent precisely so that stays its decision.
        //
        // Hidden with a Canvas component switched off, not by deactivating it - ParkedPool refuses an
        // inactive holder. A disabled Canvas draws nothing, keeps every GameObject under it active, and
        // cuts the subtree out of the canvas above. It carries no GraphicRaycaster, so nothing parked
        // under it can be clicked.
        public static Transform CreateHolder(Transform parent, string name)
        {
            GameObject holder = new(name, typeof(RectTransform), typeof(Canvas));

            // Under the caller's transform rather than at the scene root, so the holder and anything
            // parked in it die with the screen instead of outliving the bundle the prefab came from.
            holder.transform.SetParent(parent, false);
            holder.GetComponent<Canvas>().enabled = false;

            return holder.transform;
        }
    }
}
