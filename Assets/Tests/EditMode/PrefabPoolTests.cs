using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Company.ChestGame.Common;
using Company.ChestGame.Pooling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// One fixture rather than four near-identical ones: what the seam promises is written once and
    /// run against every implementation through a factory, and the places an implementation answers
    /// differently get their own tests at the bottom.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "What the pool contract tests pin".
    /// </remarks>
    public class PrefabPoolTests
    {
        private readonly List<Object> _created = new();

        private RectTransform _prefab;
        private Transform _holder;
        private Transform _parent;

        [SetUp]
        public void SetUp()
        {
            _prefab = NewObject("PoolPrefab", typeof(RectTransform)).GetComponent<RectTransform>();
            _holder = NewObject("Holder").transform;
            _parent = NewObject("Parent").transform;
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        private GameObject NewObject(string name, params Type[] components)
        {
            GameObject host = new(name, components);
            _created.Add(host);
            return host;
        }

        /// <summary>
        /// Expects exactly <paramref name="count"/> occurrences of the edit-mode destroy-refusal
        /// error log. A test that does not call this is asserting that it destroyed nothing.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        private static void ExpectDestroys(int count)
        {
            for (int i = 0; i < count; i++) LogAssert.Expect(LogType.Error, EditModeDestroy);
        }

        private static readonly Regex EditModeDestroy = new Regex("Destroy may not be called from edit mode");

        /// <summary>
        /// A pool implementation under test. <see cref="ToString"/> returns its name.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "What the pool contract tests pin".
        /// </remarks>
        public class PoolCase
        {
            private readonly string _name;
            private readonly Func<RectTransform, Transform, int, IPrefabPool<RectTransform>> _create;

            /// <summary>
            /// Whether releasing an instance through this case destroys it rather than parking it.
            /// </summary>
            /// <remarks>
            /// See docs/pooling.md, "What the pool contract tests pin".
            /// </remarks>
            public bool ReleaseDestroys { get; }

            public PoolCase(string name, bool releaseDestroys, Func<RectTransform, Transform, int, IPrefabPool<RectTransform>> create)
            {
                _name = name;
                ReleaseDestroys = releaseDestroys;
                _create = create;
            }

            public IPrefabPool<RectTransform> Create(RectTransform prefab, Transform holder, int maxSize) =>
                _create(prefab, holder, maxSize);

            public override string ToString() => _name;
        }

        private static readonly PoolCase Baseline =
            new PoolCase("DirectSpawner", releaseDestroys: true, (prefab, holder, maxSize) => new DirectSpawner<RectTransform>(prefab));

        private static readonly PoolCase Activation =
            new PoolCase("ActivationPool", releaseDestroys: false, (prefab, holder, maxSize) => new ActivationPool<RectTransform>(prefab, holder, maxSize));

        private static readonly PoolCase Parked =
            new PoolCase("ParkedPool", releaseDestroys: false, (prefab, holder, maxSize) => new ParkedPool<RectTransform>(prefab, holder, maxSize));

        private static readonly PoolCase Engine =
            new PoolCase("UnityPool", releaseDestroys: false, (prefab, holder, maxSize) => new UnityPool<RectTransform>(prefab, holder, maxSize));

        /// <summary>
        /// Everything the seam promises regardless of strategy, the baseline included.
        /// </summary>
        private static IEnumerable<PoolCase> EveryImplementation()
        {
            yield return Baseline;
            yield return Activation;
            yield return Parked;
            yield return Engine;
        }

        /// <summary>
        /// The three implementations that actually keep instances.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "What the pool contract tests pin".
        /// </remarks>
        private static IEnumerable<PoolCase> PoolingImplementations()
        {
            yield return Activation;
            yield return Parked;
            yield return Engine;
        }

        /// <remarks>
        /// See docs/pooling.md, "What the pool contract tests pin".
        /// </remarks>
        [TestCaseSource(nameof(EveryImplementation))]
        public void Get_OfAPrefabWithAnInactiveRoot_StillHandsBackSomethingVisible(PoolCase implementation)
        {
            _prefab.gameObject.SetActive(false);

            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 8);

            RectTransform first = pool.Get(_parent);
            Assert.IsTrue(first.gameObject.activeSelf,
                "a miss handed back an inactive instance - nothing the caller does would make it appear");

            if (implementation.ReleaseDestroys) return;

            pool.Release(first);

            Assert.IsTrue(pool.Get(_parent).gameObject.activeSelf,
                "a hit handed back an inactive instance");
        }

        /// <remarks>
        /// See docs/pooling.md, "What the pool contract tests pin".
        /// </remarks>
        [TestCaseSource(nameof(PoolingImplementations))]
        public void Prewarm_AgainstAPoolThatAlreadyHoldsStock_CreatesTheFullCount(PoolCase implementation)
        {
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 8);

            RectTransform a = pool.Get(_parent);
            RectTransform b = pool.Get(_parent);
            pool.Release(a);
            pool.Release(b);

            Assert.AreEqual(2, pool.AvailableCount, "guard: the pool should be holding two before it is warmed");

            pool.Prewarm(3);

            Assert.AreEqual(5, pool.AvailableCount,
                "warming a pool that already held two should leave five parked, not overwrite what was there");
            Assert.AreEqual(5, pool.CreatedCount, "warming three against two parked has to instantiate three more");
        }

        [TestCaseSource(nameof(EveryImplementation))]
        public void Get_ParentsTheInstanceToTheRequestedParent(PoolCase implementation)
        {
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 4);

            RectTransform instance = pool.Get(_parent);

            Assert.AreSame(_parent, instance.transform.parent,
                "a caller that named a parent has to get the instance under it, not wherever the pool was keeping it");
            Assert.AreEqual(1, pool.CreatedCount);
            Assert.AreEqual(1, pool.ActiveCount);
        }

        [TestCaseSource(nameof(EveryImplementation))]
        public void Release_OfAnInstanceThisPoolNeverHandedOut_ThrowsPoolException(PoolCase implementation)
        {
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 4);
            RectTransform foreign = Object.Instantiate(_prefab);
            _created.Add(foreign.gameObject);

            Assert.Throws<PoolException>(() => pool.Release(foreign));

            Assert.AreEqual(0, pool.DestroyedCount, "and it must not have destroyed something it never owned");
            Assert.AreEqual(0, pool.AvailableCount, "nor parked it, which would hand someone else's object out later");
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        [TestCaseSource(nameof(EveryImplementation))]
        public void Release_OfTheSameInstanceTwice_ThrowsPoolException(PoolCase implementation)
        {
            ExpectDestroys(implementation.ReleaseDestroys ? 1 : 0);
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 4);
            RectTransform instance = pool.Get(_parent);
            pool.Release(instance);
            int parkedAfterTheFirstRelease = pool.AvailableCount;

            Assert.Throws<PoolException>(() => pool.Release(instance));

            Assert.AreEqual(parkedAfterTheFirstRelease, pool.AvailableCount,
                "the release that was rejected must not have parked the instance a second time, or two callers get it");
            Assert.AreEqual(0, pool.ActiveCount);
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        [TestCaseSource(nameof(EveryImplementation))]
        public void ReleaseAll_TakesBackEveryInstanceThatWasHandedOut(PoolCase implementation)
        {
            ExpectDestroys(implementation.ReleaseDestroys ? 3 : 0);
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 8);
            pool.Get(_parent);
            pool.Get(_parent);
            pool.Get(_parent);

            pool.ReleaseAll();

            Assert.AreEqual(0, pool.ActiveCount, "a screen tearing down has to be able to hand everything back at once");
            Assert.AreEqual(3, pool.CreatedCount);
            Assert.DoesNotThrow(() => pool.ReleaseAll(), "and calling it again on an empty pool is not an error");
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        [TestCaseSource(nameof(EveryImplementation))]
        public void Dispose_DestroysEverythingThePoolOwns(PoolCase implementation)
        {
            ExpectDestroys(2);
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 8);
            RectTransform first = pool.Get(_parent);
            pool.Get(_parent);
            pool.Release(first);

            pool.Dispose();

            Assert.AreEqual(2, pool.CreatedCount);
            Assert.AreEqual(pool.CreatedCount, pool.DestroyedCount,
                "parked and handed out alike: anything left behind outlives the pool with nobody holding it");
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(0, pool.AvailableCount);
            Assert.DoesNotThrow(() => pool.Dispose(), "and disposing twice is not an error");
        }

        /// <remarks>
        /// See docs/pooling.md, "What the pool contract tests pin".
        /// </remarks>
        [TestCaseSource(nameof(EveryImplementation))]
        public void AfterDispose_GettingOrPrewarmingThrowsPoolException(PoolCase implementation)
        {
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 4);
            pool.Dispose();

            Assert.Throws<PoolException>(() => pool.Get(_parent));
            Assert.Throws<PoolException>(() => pool.Prewarm(1));
            Assert.AreEqual(0, pool.CreatedCount);
        }

        [TestCaseSource(nameof(EveryImplementation))]
        public void Constructing_WithoutAUsablePrefab_ThrowsPoolException(PoolCase implementation)
        {
            Assert.Throws<PoolException>(() => implementation.Create(null, _holder, 4));

            RectTransform destroyed = Object.Instantiate(_prefab);
            Object.DestroyImmediate(destroyed.gameObject);

            Assert.Throws<PoolException>(() => implementation.Create(destroyed, _holder, 4),
                "a destroyed prefab is not a prefab, which is why the check leans on Unity's own equality rather than ReferenceEquals");
        }

        [TestCaseSource(nameof(PoolingImplementations))]
        public void Get_AfterRelease_ReturnsTheSameInstance(PoolCase implementation)
        {
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 4);
            RectTransform first = pool.Get(_parent);
            pool.Release(first);

            RectTransform second = pool.Get(_parent);

            Assert.AreSame(first, second, "reuse is the whole point");
            Assert.AreEqual(1, pool.CreatedCount, "and reuse means the second get instantiated nothing");
            Assert.AreEqual(0, pool.DestroyedCount);
            Assert.AreEqual(0, pool.AvailableCount);
        }

        [TestCaseSource(nameof(PoolingImplementations))]
        public void Release_TakesTheInstanceOffTheParentItWasGotFor(PoolCase implementation)
        {
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 4);
            RectTransform instance = pool.Get(_parent);

            pool.Release(instance);

            Assert.AreNotSame(_parent, instance.transform.parent,
                "an instance left under the caller's parent is still in the layout it was supposed to have left");
            Assert.AreSame(_holder, instance.transform.parent);
            Assert.AreEqual(1, pool.AvailableCount);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [TestCaseSource(nameof(PoolingImplementations))]
        public void Prewarm_CreatesExactlyWhatItWasAskedFor_AndTheGetsAfterItCreateNothing(PoolCase implementation)
        {
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 8);

            pool.Prewarm(3);

            Assert.AreEqual(3, pool.CreatedCount);
            Assert.AreEqual(3, pool.AvailableCount);
            Assert.AreEqual(0, pool.ActiveCount, "warming hands nothing out");

            for (int i = 0; i < 3; i++) pool.Get(_parent);

            Assert.AreEqual(3, pool.CreatedCount,
                "three gets against three parked instances must not instantiate a fourth, or the warm-up bought nothing");
            Assert.AreEqual(0, pool.AvailableCount);
            Assert.AreEqual(3, pool.ActiveCount);
        }

        [TestCaseSource(nameof(PoolingImplementations))]
        public void Prewarm_PastTheMaxSize_ThrowsPoolExceptionInsteadOfWarmingFewer(PoolCase implementation)
        {
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 2);

            Assert.Throws<PoolException>(() => pool.Prewarm(3));

            Assert.AreEqual(0, pool.CreatedCount, "and it refuses before instantiating any of them");
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        [TestCaseSource(nameof(PoolingImplementations))]
        public void Release_PastTheMaxSize_DestroysTheSurplus(PoolCase implementation)
        {
            ExpectDestroys(1);
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 2);
            RectTransform first = pool.Get(_parent);
            RectTransform second = pool.Get(_parent);
            RectTransform third = pool.Get(_parent);

            pool.Release(first);
            pool.Release(second);
            pool.Release(third);

            Assert.AreEqual(3, pool.CreatedCount);
            Assert.AreEqual(2, pool.AvailableCount, "the bound is a bound, not a suggestion");
            Assert.AreEqual(1, pool.DestroyedCount, "and the one over it is destroyed rather than parked");
            Assert.AreEqual(0, pool.ActiveCount);

            RectTransform backOut = pool.Get(_parent);
            RectTransform alsoBackOut = pool.Get(_parent);

            CollectionAssert.AreEquivalent(new[] { first, second }, new[] { backOut, alsoBackOut },
                "the surplus must not be sitting in the pool waiting to be handed out");
            Assert.AreEqual(3, pool.CreatedCount);
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// See docs/design-decisions.md, "The seam itself".
        /// </remarks>
        [TestCaseSource(nameof(PoolingImplementations))]
        public void Trim_DestroysWhatIsParkedAndLeavesWhatIsHandedOut(PoolCase implementation)
        {
            ExpectDestroys(1);
            IPrefabPool<RectTransform> pool = implementation.Create(_prefab, _holder, 4);
            RectTransform stillOut = pool.Get(_parent);
            pool.Release(pool.Get(_parent));

            pool.Trim();

            Assert.AreEqual(0, pool.AvailableCount);
            Assert.AreEqual(1, pool.ActiveCount, "what is still out stays out, or trimming would destroy a live view");
            Assert.AreEqual(1, pool.DestroyedCount);
            Assert.AreEqual(2, pool.CreatedCount);

            pool.Release(stillOut);

            Assert.AreEqual(1, pool.AvailableCount, "a trimmed pool is empty, not broken");
        }

        [TestCaseSource(nameof(PoolingImplementations))]
        public void Constructing_WithNowhereToParkOrNoRoomToPark_ThrowsPoolException(PoolCase implementation)
        {
            Assert.Throws<PoolException>(() => implementation.Create(_prefab, null, 4));
            Assert.Throws<PoolException>(() => implementation.Create(_prefab, _holder, 0),
                "a max size of zero is a pool that can never park anything, which is the baseline wearing a pool's name");
        }

        /// <remarks>
        /// See docs/pooling.md, "What the pool contract tests pin".
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        [Test]
        public void DirectSpawner_Get_AfterRelease_ReturnsANewInstanceRatherThanTheOldOne()
        {
            ExpectDestroys(1);
            DirectSpawner<RectTransform> spawner = new(_prefab);
            RectTransform first = spawner.Get(_parent);
            spawner.Release(first);

            RectTransform second = spawner.Get(_parent);

            Assert.AreNotSame(first, second);
            Assert.AreEqual(2, spawner.CreatedCount, "every get instantiates");
            Assert.AreEqual(1, spawner.DestroyedCount, "every release destroys");
            Assert.AreEqual(0, spawner.AvailableCount, "and nothing is held in between");
        }

        /// <remarks>
        /// See docs/design-decisions.md, "The seam itself".
        /// </remarks>
        [Test]
        public void DirectSpawner_PrewarmAndTrim_DoNothingBecauseItHoldsNothing()
        {
            DirectSpawner<RectTransform> spawner = new(_prefab);

            spawner.Prewarm(3);
            spawner.Trim();

            Assert.AreEqual(0, spawner.CreatedCount,
                "there is nowhere to park a warmed instance, so warming would only leak three of them");
            Assert.AreEqual(0, spawner.AvailableCount);
            Assert.AreEqual(0, spawner.DestroyedCount);

            RectTransform instance = spawner.Get(_parent);

            Assert.IsNotNull(instance);
            Assert.AreEqual(1, spawner.CreatedCount);
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove ParkedPool avoids OnDisable".
        /// </remarks>
        [Test]
        public void ParkedPool_AcrossAGetAndRelease_NeverDeactivatesTheInstance()
        {
            ParkedPool<RectTransform> pool = new(_prefab, _holder, 4);

            RectTransform instance = pool.Get(_parent);
            Assert.IsTrue(instance.gameObject.activeSelf, "handed out and inactive would be invisible");

            pool.Release(instance);

            Assert.IsTrue(instance.gameObject.activeSelf,
                "parking must not deactivate: OnDisable and the layout rebuild it drags with it are the cost this pool exists to avoid");
            Assert.IsTrue(instance.gameObject.activeInHierarchy,
                "and the holder must not deactivate it either, which is why an inactive holder is refused");

            RectTransform again = pool.Get(_parent);

            Assert.AreSame(instance, again);
            Assert.IsTrue(again.gameObject.activeSelf);
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove ParkedPool avoids OnDisable".
        /// </remarks>
        [Test]
        public void ActivationPool_Release_DeactivatesTheInstance_AndGetBringsItBack()
        {
            ActivationPool<RectTransform> pool = new(_prefab, _holder, 4);
            RectTransform instance = pool.Get(_parent);
            Assert.IsTrue(instance.gameObject.activeSelf);

            pool.Release(instance);

            Assert.IsFalse(instance.gameObject.activeSelf,
                "the hand-rolled pool parks by deactivating, which is exactly what ParkedPool refuses to do");

            RectTransform again = pool.Get(_parent);

            Assert.AreSame(instance, again);
            Assert.IsTrue(again.gameObject.activeSelf, "and a reused instance has to come back visible");
        }

        [Test]
        public void ParkedPool_WithAnInactiveHolder_ThrowsPoolException()
        {
            GameObject inactiveHolder = NewObject("InactiveHolder");
            inactiveHolder.SetActive(false);

            Assert.Throws<PoolException>(() => new ParkedPool<RectTransform>(_prefab, inactiveHolder.transform, 4),
                "a child parked under an inactive object is deactivated by the hierarchy, so this pool would be paying the cost it was written to avoid and reporting nothing");
        }

        /// <remarks>
        /// See docs/pooling.md, "How the tests prove ParkedPool avoids OnDisable".
        /// </remarks>
        [Test]
        public void ParkedPool_ParksWithoutFiringOnDisable_WhereActivationPoolFiresIt()
        {
            _prefab.gameObject.AddComponent<DisableProbe>();

            ActivationPool<RectTransform> activation = new(_prefab, _holder, 4);
            RectTransform activationInstance = activation.Get(_parent);
            DisableProbe activationProbe = activationInstance.GetComponent<DisableProbe>();
            int activationDisablesWhileHeld = activationProbe.DisableCount;

            activation.Release(activationInstance);

            Assert.AreEqual(activationDisablesWhileHeld + 1, activationProbe.DisableCount,
                "control: ActivationPool parks by deactivating, so exactly one OnDisable has to have run. Zero means edit-mode tests do not run ExecuteAlways callbacks at all and this test can measure nothing");

            ParkedPool<RectTransform> parked = new(_prefab, _holder, 4);
            RectTransform parkedInstance = parked.Get(_parent);
            DisableProbe parkedProbe = parkedInstance.GetComponent<DisableProbe>();
            int parkedDisablesWhileHeld = parkedProbe.DisableCount;

            parked.Release(parkedInstance);
            parked.Get(_parent);

            Assert.AreEqual(parkedDisablesWhileHeld, parkedProbe.DisableCount,
                "and the whole of what ParkedPool buys: a get, a release and a get again wake nothing up");
        }

        /// <summary>
        /// Counts how many times <see cref="OnDisable"/> fires. Marked with <c>[ExecuteAlways]</c>
        /// so the callback also runs in edit mode.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "How the tests prove ParkedPool avoids OnDisable".
        /// </remarks>
        [ExecuteAlways]
        public class DisableProbe : MonoBehaviour
        {
            public int DisableCount;

            private void OnDisable() => DisableCount++;
        }

        /// <remarks>
        /// See docs/architecture.md, "Exception hierarchy".
        /// </remarks>
        [Test]
        public void PoolException_IsDeliberatelyNotUnderChestGameException()
        {
            PoolException failure = Assert.Throws<PoolException>(
                () => new ActivationPool<RectTransform>(null, _holder, 4));

            Assert.IsNotInstanceOf<ChestGameException>(failure,
                "or the shell would report a wiring bug to the player as a content download failure and carry on");
        }

        /// <remarks>
        /// See docs/pooling.md, "UnityPool, and what wrapping ObjectPool costs".
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        [Test]
        public void UnityPool_AfterATrim_StillReportsWhatIsStillHandedOut()
        {
            ExpectDestroys(1);
            UnityPool<RectTransform> pool = new(_prefab, _holder, 4);
            RectTransform stillOut = pool.Get(_parent);
            pool.Release(pool.Get(_parent));

            pool.Trim();

            Assert.AreEqual(1, pool.ActiveCount);
            Assert.AreEqual(0, pool.AvailableCount);
            Assert.DoesNotThrow(() => pool.Release(stillOut), "and what was still out is still releasable afterwards");
            Assert.AreEqual(1, pool.AvailableCount);
        }
    }
}
