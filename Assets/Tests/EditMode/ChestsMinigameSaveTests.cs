using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    // Decision #9 (docs/design-decisions.md) says the prize location is never stored - this fixture
    // is what turns that decision into something that fails if a later change breaks it, rather than
    // a comment nobody re-checks. Every ISaveService here is a real SaveService over a FakeSaveStore,
    // so what these tests pin is the actual bytes ChestsMinigameController persists, the same shape
    // GameLifetimeScopePauseQuitFlushTests already uses for the currency scheduler.
    public class ChestsMinigameSaveTests
    {
        private const int OpenMilliseconds = 100;
        private const int FramesToOpen = 2;

        private readonly List<ChestsMinigameController> _controllersToDispose = new();

        [TearDown]
        public void TearDown()
        {
            foreach (ChestsMinigameController controller in _controllersToDispose)
            {
                controller.Dispose();
            }
            _controllersToDispose.Clear();
        }

        private (ChestsMinigameController controller, ISaveService service, ISaveFlushRegistry registry, FakeRandomProvider random, FakeGameClock clock)
            NewController(ISaveService sharedService = null, int chestCount = 4, int attemptsCount = 4, FakeRandomProvider random = null)
        {
            ISaveService service = sharedService ?? new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());
            ISaveFlushRegistry registry = new SaveFlushRegistry();
            random ??= new FakeRandomProvider();
            FakeGameClock clock = new() { DeltaTime = 0.05f };

            ChestsMinigameController controller = new();
            controller.Configure(ChestsMinigameConfig.Create(chestCount, attemptsCount, OpenMilliseconds));
            controller.Inject(new FakeRewardsManager(), random, clock, service, registry);

            _controllersToDispose.Add(controller);

            return (controller, service, registry, random, clock);
        }

        private static void OpenChest(ChestsMinigameController controller, FakeGameClock clock, int index)
        {
            controller.OnChestClicked(controller.Chests[index]);
            clock.AdvanceFrames(FramesToOpen);
        }

        private static ChestsRunSaveDocument LoadStored(ISaveService service) =>
            SynchronousUniTask.Result(service.LoadAsync<ChestsRunSaveDocument>(ChestsRunSaveDocument.SaveKey, CancellationToken.None));

        private static void Seed(ISaveService service, ChestsRunSaveDocument document) =>
            SynchronousUniTask.Complete(service.SaveAsync(ChestsRunSaveDocument.SaveKey, document, CancellationToken.None));

        // The body exactly as it sits in the envelope, parsed but never mapped onto a type, so a
        // field ChestsRunSaveDocument does not declare is still there to be seen.
        private static JToken StoredBody(byte[] rawEnvelope) =>
            JToken.Parse(Encoding.UTF8.GetString(SaveEnvelope.Parse(Encoding.UTF8.GetString(rawEnvelope)).GetBody()));

        // --- Decision #9, enforced -----------------------------------------------------------

        [Test]
        public void MidRunSave_IsInvariantUnderWhichChestWouldWinNext()
        {
            // Both runs open chest 0 then chest 1, drawing empty both times (>1/4 and >1/3). The
            // third, never-drawn value differs so a different chest would win next in each, and so
            // does every Range draw - a controller that placed the prize up front with Range and
            // persisted where it put it has to be told apart too. The whole point being that the
            // persisted document must not care. Both providers are primed before Inject, so even a
            // draw taken while the controller is being wired up differs between the two runs.
            FakeRandomProvider randomA = new(0.30f, 0.40f, 0.10f) { NextRangeResult = 2 }; // 0.10 would win chest 2 next
            randomA.RangeSequence.Enqueue(2);
            FakeSaveStore storeA = new();
            (ChestsMinigameController controllerA, _, ISaveFlushRegistry registryA, _, FakeGameClock clockA) =
                NewController(new SaveService(new JsonCodec(), new NoProtection(), storeA), random: randomA);

            FakeRandomProvider randomB = new(0.30f, 0.40f, 0.90f) { NextRangeResult = 3 }; // 0.90 would stay empty next
            randomB.RangeSequence.Enqueue(3);
            FakeSaveStore storeB = new();
            (ChestsMinigameController controllerB, _, ISaveFlushRegistry registryB, _, FakeGameClock clockB) =
                NewController(new SaveService(new JsonCodec(), new NoProtection(), storeB), random: randomB);

            controllerA.NewGame();
            OpenChest(controllerA, clockA, 0);
            OpenChest(controllerA, clockA, 1);

            controllerB.NewGame();
            OpenChest(controllerB, clockB, 0);
            OpenChest(controllerB, clockB, 1);

            registryA.FlushAll();
            registryB.FlushAll();

            // The raw bytes in the store, not a typed load and re-encode: loading through
            // ChestsRunSaveDocument would silently drop any field it does not declare, which is
            // exactly where a leaked prize location would sit.
            byte[] bytesA = SynchronousUniTask.Result(storeA.ReadAsync(ChestsRunSaveDocument.SaveKey, CancellationToken.None));
            byte[] bytesB = SynchronousUniTask.Result(storeB.ReadAsync(ChestsRunSaveDocument.SaveKey, CancellationToken.None));
            Assert.IsNotNull(bytesA, "guard: the flush has to have written run A");
            Assert.IsNotNull(bytesB, "guard: the flush has to have written run B");

            CollectionAssert.AreEqual(bytesA, bytesB,
                "decision #9 requires the save to carry zero information about where the prize is; " +
                "a payload that moves when only the next draw differs would leak exactly that");

            JToken body = StoredBody(bytesA);
            JObject expected = JObject.Parse(@"{""ChestCount"":4,""OpenedChestIndices"":[0,1]}");
            Assert.IsTrue(JToken.DeepEquals(expected, body),
                $"a mid-run save holds the chest count and the chests opened empty, and nothing else; stored body was {body}");
        }

        [Test]
        public void SaveDocument_SerializesExactlyChestCountAndOpenedChestIndices()
        {
            HashSet<string> allowed = new() { nameof(ChestsRunSaveDocument.ChestCount), nameof(ChestsRunSaveDocument.OpenedChestIndices) };

            IEnumerable<string> actual = typeof(ChestsRunSaveDocument)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name);

            CollectionAssert.AreEquivalent(allowed, actual,
                "ChestsRunSaveDocument carries exactly ChestCount and OpenedChestIndices by decision #9 " +
                "(docs/design-decisions.md) - a mid-run save can only ever name Open_Empty chests, and " +
                "adding a member here is a deliberate act that has to revisit that decision, not a " +
                "change that should compile silently");
        }

        [Test]
        public void WinningTheRun_PersistsNothingResumable()
        {
            // Chest 0 is opened empty first, so what opening it recorded is a genuinely resumable
            // run. Only the win overwriting it keeps the next session from picking it back up.
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());

            (ChestsMinigameController controller, _, ISaveFlushRegistry registry, FakeRandomProvider random, FakeGameClock clock) = NewController(service);
            controller.NewGame();
            random.ValueSequence.Enqueue(0.99f); // > 1/4 -> empty
            random.NextValue = 0f;               // then a certain win

            OpenChest(controller, clock, 0);
            OpenChest(controller, clock, 1);
            Assert.AreEqual(ChestsMinigameController.State.Ended, controller.CurrentState, "guard: the second chest has to have won the run");
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Prize, controller.Chests[1].CurrentState);
            registry.FlushAll();

            (ChestsMinigameController next, _, _, _, _) = NewController(service);
            next.NewGame();

            Assert.AreEqual(0, next.Attempts, "a run that was won must never resume");
            Assert.IsTrue(next.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed),
                "the next session has to start on a fresh board, not one with the won run's empty chests already open");
        }

        [Test]
        public void RunningOutOfAttempts_PersistsNothingResumable()
        {
            // The next session is configured with more attempts than this one had. Under the same
            // 2-attempt config the restore guard would throw a stale two-chest run away on its own,
            // so only a config that would otherwise accept it shows whether ending the run actually
            // cleared it - a finished run never resumes, whatever the config says next time.
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());

            (ChestsMinigameController controller, _, ISaveFlushRegistry registry, FakeRandomProvider random, FakeGameClock clock) =
                NewController(service, chestCount: 10, attemptsCount: 2);
            controller.NewGame();
            random.NextValue = 1f;

            OpenChest(controller, clock, 0);
            OpenChest(controller, clock, 1);
            Assert.AreEqual(ChestsMinigameController.State.Ended, controller.CurrentState, "guard: two empty chests have to have used up both attempts");
            registry.FlushAll();

            (ChestsMinigameController next, _, _, _, _) = NewController(service, chestCount: 10, attemptsCount: 4);
            next.NewGame();

            Assert.AreEqual(0, next.Attempts, "a run that ran out of attempts must never resume");
            Assert.IsTrue(next.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed),
                "the next session has to start on a fresh board, not one with the finished run's chests already open");
        }

        // --- Restore -----------------------------------------------------------------------

        [Test]
        public void ASecondController_RestoresAttemptsAndChestStates_AndTheNextDrawSeesTheSameK()
        {
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());

            (ChestsMinigameController first, _, ISaveFlushRegistry firstRegistry, FakeRandomProvider firstRandom, FakeGameClock firstClock) = NewController(service);
            first.NewGame();
            firstRandom.ValueSequence.Enqueue(0.30f); // >1/4 -> empty
            firstRandom.ValueSequence.Enqueue(0.40f); // >1/3 -> empty
            OpenChest(first, firstClock, 0);
            OpenChest(first, firstClock, 1);
            firstRegistry.FlushAll();

            (ChestsMinigameController second, _, _, FakeRandomProvider secondRandom, FakeGameClock secondClock) = NewController(service);
            second.NewGame();

            Assert.AreEqual(2, second.Attempts, "the restored run has to resume with the same attempt count it was saved with");
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, second.Chests[0].CurrentState);
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, second.Chests[1].CurrentState);
            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, second.Chests[2].CurrentState);
            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, second.Chests[3].CurrentState);

            // The regression guard for k itself: at k=2 already opened, the next draw's odds are
            // 1/(4-3+1) = 1/2. 0.4 wins there but would still read as empty at the wrong,
            // unrestored odds of 1/(4-1+1) = 1/4 - the two are only told apart by Attempts having
            // actually been restored to 2 rather than left at 0.
            secondRandom.NextValue = 0.4f;
            OpenChest(second, secondClock, 2);

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Prize, second.Chests[2].CurrentState,
                "restoring must preserve k (the count of already-opened chests) for the next draw's odds");
            Assert.AreEqual(3, second.Attempts);
        }

        [Test]
        public void Discard_WhenChestCountDiffersFromConfiguration()
        {
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());
            Seed(service, new ChestsRunSaveDocument { ChestCount = 999, OpenedChestIndices = new List<int> { 0 } });

            (ChestsMinigameController controller, _, _, _, _) = NewController(service, chestCount: 4, attemptsCount: 4);
            controller.NewGame();

            Assert.AreEqual(0, controller.Attempts);
            Assert.IsTrue(controller.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        [Test]
        public void Discard_WhenAnIndexIsOutOfRange()
        {
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());
            Seed(service, new ChestsRunSaveDocument { ChestCount = 4, OpenedChestIndices = new List<int> { 0, 99 } });

            (ChestsMinigameController controller, _, _, _, _) = NewController(service, chestCount: 4, attemptsCount: 4);
            controller.NewGame();

            Assert.AreEqual(0, controller.Attempts);
            Assert.IsTrue(controller.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        [Test]
        public void Discard_WhenAnIndexIsDuplicated()
        {
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());
            Seed(service, new ChestsRunSaveDocument { ChestCount = 4, OpenedChestIndices = new List<int> { 0, 0 } });

            (ChestsMinigameController controller, _, _, _, _) = NewController(service, chestCount: 4, attemptsCount: 4);
            controller.NewGame();

            Assert.AreEqual(0, controller.Attempts);
            Assert.IsTrue(controller.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        [Test]
        public void Discard_WhenTheOpenedCountAlreadyReachedTotalAttempts()
        {
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());
            Seed(service, new ChestsRunSaveDocument { ChestCount = 4, OpenedChestIndices = new List<int> { 0, 1, 2, 3 } });

            (ChestsMinigameController controller, _, _, _, _) = NewController(service, chestCount: 4, attemptsCount: 4);
            controller.NewGame();

            Assert.AreEqual(0, controller.Attempts);
            Assert.IsTrue(controller.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        [Test]
        public void Discard_WhenAnIndexIsNegative()
        {
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());
            Seed(service, new ChestsRunSaveDocument { ChestCount = 4, OpenedChestIndices = new List<int> { 0, -1 } });

            (ChestsMinigameController controller, _, _, _, _) = NewController(service, chestCount: 4, attemptsCount: 4);
            controller.NewGame();

            Assert.AreEqual(0, controller.Attempts);
            Assert.IsTrue(controller.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        [Test]
        public void ASavedRunWhoseOpenedListIsNull_StartsFresh_RatherThanThrowing()
        {
            // An edited or hand-written save can say null where the list belongs; JsonCodec hands
            // that through as a null property rather than the empty list the type initialises.
            // Seeded as raw JSON because SaveAsync on the typed document could never produce it.
            FakeSaveStore store = new();
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), store);
            store.Seed(ChestsRunSaveDocument.SaveKey, Encoding.UTF8.GetBytes(
                $@"{{""v"":{SaveService.CurrentSchemaVersion},""codec"":""json"",""prot"":""none"",""enc"":""raw"",""body"":{{""ChestCount"":4,""OpenedChestIndices"":null}}}}"));

            (ChestsMinigameController controller, _, _, _, _) = NewController(service, chestCount: 4, attemptsCount: 4);

            Assert.DoesNotThrow(() => controller.NewGame(),
                "a saved run the controller cannot use has to be set aside, never allowed to stop the minigame starting");
            Assert.AreEqual(0, controller.Attempts);
            Assert.IsTrue(controller.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed));
            Assert.AreEqual(ChestsMinigameController.State.Playing, controller.CurrentState);
        }

        [Test]
        public void NewGame_OnARestart_DoesNotResumeTheJustSavedRun()
        {
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), new FakeSaveStore());

            (ChestsMinigameController controller, _, ISaveFlushRegistry registry, FakeRandomProvider random, FakeGameClock clock) = NewController(service);
            controller.NewGame();
            random.NextValue = 1f;
            OpenChest(controller, clock, 0);
            Assert.AreEqual(1, controller.Attempts, "guard: something has to be saved for a restart to fail to resume");

            controller.NewGame();
            registry.FlushAll();

            ChestsRunSaveDocument stored = LoadStored(service);
            Assert.AreEqual(0, stored.ChestCount);
            CollectionAssert.IsEmpty(stored.OpenedChestIndices);

            (ChestsMinigameController third, _, _, _, _) = NewController(service);
            third.NewGame();

            Assert.AreEqual(0, third.Attempts);
            Assert.IsTrue(third.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        // --- Wiring --------------------------------------------------------------------------

        [Test]
        public void Inject_RegistersItsSchedulerWithTheFlushRegistry_AndDisposeUnregistersIt()
        {
            (ChestsMinigameController controller, _, ISaveFlushRegistry registry, _, _) = NewController();

            Assert.AreEqual(1, registry.Registered.Count, "guard: Inject should have registered exactly one flushable");
            Assert.AreEqual(ChestsRunSaveDocument.SaveKey, registry.Registered[0].SaveKey);

            controller.Dispose();
            _controllersToDispose.Remove(controller);

            CollectionAssert.IsEmpty(registry.Registered);
        }

        [Test]
        public void Inject_OverAThreadHoppingComposition_ThrowsSynchronousLoadNeedsNonHoppingStore()
        {
            ISaveService hoppingService = new SaveService(new FakeSaveCodec(), new NoProtection(), new ThreadHoppingStore(new FakeSaveStore()));
            Assert.IsFalse(hoppingService.CompletesOnCallingThread, "guard: this composition has to be the hopping one this test means to drive");

            ChestsMinigameController controller = new();
            controller.Configure(ChestsMinigameConfig.Create(4, 4, OpenMilliseconds));

            SaveException error = Assert.Throws<SaveException>(() =>
                controller.Inject(new FakeRewardsManager(), new FakeRandomProvider(), new FakeGameClock(), hoppingService, new SaveFlushRegistry()));
            StringAssert.Contains("completes on the calling thread", error.Message);
        }

        [Test]
        public void Inject_WhenItThrows_LeavesNothingRegisteredWithTheFlushRegistry()
        {
            ISaveService hoppingService = new SaveService(new FakeSaveCodec(), new NoProtection(), new ThreadHoppingStore(new FakeSaveStore()));
            ISaveFlushRegistry registry = new SaveFlushRegistry();

            ChestsMinigameController controller = new();
            controller.Configure(ChestsMinigameConfig.Create(4, 4, OpenMilliseconds));

            Assert.Throws<SaveException>(() =>
                controller.Inject(new FakeRewardsManager(), new FakeRandomProvider(), new FakeGameClock(), hoppingService, registry));

            // An Inject that throws has to clean up after itself rather than count on its owner
            // to: the caller only ever sees the exception, and nothing promises it will go on to
            // Dispose() an object that never finished being injected. A registration taken before
            // the throw would sit in the singleton registry for the life of the process, flushed at
            // every pause, holding a dead controller's state - and one more would accumulate per
            // failed start.
            CollectionAssert.IsEmpty(registry.Registered);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            (ChestsMinigameController controller, _, _, _, _) = NewController();

            controller.Dispose();
            _controllersToDispose.Remove(controller);

            Assert.DoesNotThrow(() => controller.Dispose(),
                "Dispose was idempotent before this phase added a scheduler and a registry to it, and IDisposable requires it to stay that way");
        }

        [Test]
        public void Inject_WhenTheSavedRunCannotBeRead_DiscardsItAndLogs_RatherThanRefusingToStart()
        {
            FakeSaveStore store = new();
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), store);
            store.Seed(ChestsRunSaveDocument.SaveKey, Encoding.UTF8.GetBytes("{ not an envelope"));

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("The saved chests run could not be read and is being discarded")));

            (ChestsMinigameController controller, _, ISaveFlushRegistry registry, _, _) = NewController(sharedService: service);

            controller.NewGame();

            Assert.AreEqual(0, controller.Attempts);
            Assert.IsTrue(controller.Chests.All(chest => chest.CurrentState == ChestsMinigameChestModel.State.Closed),
                "an unreadable run has to start a clean one, not refuse to open the minigame at all");

            // The discard branch overwrites the unreadable document, so the save repairs itself
            // rather than failing every launch forever with nothing that ever clears it.
            registry.FlushAll();
            Assert.DoesNotThrow(() => LoadStored(service));
        }
    }
}
