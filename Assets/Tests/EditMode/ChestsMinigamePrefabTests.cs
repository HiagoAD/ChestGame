using Company.ChestGame.Pooling;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // The authored value, not the code default. A [SerializeField] with an initializer keeps that
    // initializer only while the key is absent from the asset; the moment anything re-saves the
    // prefab, whatever the inspector was showing wins instead.
    //
    // Same shape as the GameLifetimeScopeTests assertions against the real composition root: run
    // against the real authored asset, not a copy of what it is supposed to contain.
    public class ChestsMinigamePrefabTests
    {
        private const string PrefabPath = "Assets/_Project/Minigames/Chests/ChestsMinigame.prefab";

        [Test]
        public void TheShippedPrefab_SelectsTheStrategyTheBenchmarkChose()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"no chests minigame prefab at {PrefabPath}");

            UnityEditor.SerializedObject serialized = null;
            foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "ChestsMinigameView") continue;

                serialized = new UnityEditor.SerializedObject(behaviour);
                break;
            }

            Assert.IsNotNull(serialized, "the prefab carries no ChestsMinigameView");

            UnityEditor.SerializedProperty strategy = serialized.FindProperty("_poolStrategy");
            Assert.IsNotNull(strategy, "ChestsMinigameView has no _poolStrategy field any more - this test is stale");
            Assert.AreEqual((int)PoolStrategy.ParkedPool, strategy.enumValueIndex,
                $"the shipped prefab selects {(PoolStrategy)strategy.enumValueIndex}, not the ParkedPool that docs/design-decisions.md section 14 says the measurement chose");
#else
            Assert.Ignore("Reads an authored asset through the AssetDatabase, so it only runs in the editor.");
#endif
        }
    }
}
