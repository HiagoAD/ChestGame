using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Saving.Demo;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// <see cref="SaveInspectorController"/>'s own rules: selection, and the save and tamper flows
    /// built over <see cref="SavePipelineProbe"/> and <see cref="SaveTamper"/>. Storage is InMemory
    /// throughout - see <c>SavePipelineProbeTests</c> for why - and every test writes its own save
    /// before reading it back, so the process-shared store never leaks a stale value between cases.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md. See docs/saving.md, "The save inspector".
    /// </remarks>
    public class SaveInspectorControllerTests
    {
        private const int InMemoryStorageIndex = 3;
        private const int JsonCodecIndex = 0;
        private const int NoneProtectionIndex = 0;
        private const int HmacProtectionIndex = 3;

        private static SaveInspectorController NewController() => new();

        [Test]
        public void SetStorage_ChangesTheIndex_AndRaisesOnSelectionChanged()
        {
            SaveInspectorController controller = NewController();
            int raised = 0;
            controller.OnSelectionChanged += () => raised++;

            controller.SetStorage(InMemoryStorageIndex);

            Assert.AreEqual(InMemoryStorageIndex, controller.StorageIndex);
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void SetCodec_ChangesTheIndex_AndRaisesOnSelectionChanged()
        {
            SaveInspectorController controller = NewController();
            int raised = 0;
            controller.OnSelectionChanged += () => raised++;

            controller.SetCodec(JsonCodecIndex);

            Assert.AreEqual(JsonCodecIndex, controller.CodecIndex);
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void SetProtection_ChangesTheIndex_AndRaisesOnSelectionChanged()
        {
            SaveInspectorController controller = NewController();
            int raised = 0;
            controller.OnSelectionChanged += () => raised++;

            controller.SetProtection(HmacProtectionIndex);

            Assert.AreEqual(HmacProtectionIndex, controller.ProtectionIndex);
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void SaveAsync_OverInMemory_RecordsWhatItSaved()
        {
            SaveInspectorController controller = NewController();
            controller.SetStorage(InMemoryStorageIndex);
            controller.SetCodec(JsonCodecIndex);
            controller.SetProtection(NoneProtectionIndex);

            SaveProbeResult? completed = null;
            controller.OnSaveCompleted += (result, _) => completed = result;

            SynchronousUniTask.Complete(controller.SaveAsync(CancellationToken.None));

            Assert.IsTrue(controller.HasSaved);
            Assert.AreEqual(SaveStorage.InMemory, controller.SavedStorage);
            Assert.AreEqual(SaveCodec.Json, controller.SavedCodec);
            Assert.AreEqual(SaveProtection.None, controller.SavedProtection);
            Assert.IsTrue(completed.HasValue, "OnSaveCompleted did not fire");
            Assert.AreEqual(1_250, completed.Value.Loaded.Balance);
        }

        [Test]
        public void SaveAsync_WhileAlreadyBusy_DoesNotRunTheSaveAgain()
        {
            SaveInspectorController controller = NewController();
            controller.SetStorage(InMemoryStorageIndex);

            int completedCount = 0;
            controller.OnSaveCompleted += (_, _) => completedCount++;

            bool reentered = false;
            controller.OnBusyChanged += busy =>
            {
                if (!busy || reentered) return;
                reentered = true;
                SynchronousUniTask.Complete(controller.SaveAsync(CancellationToken.None));
            };

            SynchronousUniTask.Complete(controller.SaveAsync(CancellationToken.None));

            Assert.IsTrue(reentered, "guard: the reentrant call was never attempted");
            Assert.AreEqual(1, completedCount, "a save started while one was already running ran the pipeline a second time");
        }

        [Test]
        public void TamperAsync_BeforeAnySave_DoesNothing()
        {
            SaveInspectorController controller = NewController();
            bool completed = false;
            controller.OnTamperCompleted += _ => completed = true;

            SynchronousUniTask.Complete(controller.TamperAsync(CancellationToken.None));

            Assert.IsFalse(completed);
        }

        [Test]
        public void TamperAsync_WithNoneOverInMemory_ReportsTheEditAccepted()
        {
            SaveInspectorController controller = NewController();
            controller.SetStorage(InMemoryStorageIndex);
            controller.SetCodec(JsonCodecIndex);
            controller.SetProtection(NoneProtectionIndex);
            SynchronousUniTask.Complete(controller.SaveAsync(CancellationToken.None));

            SaveTamperResult? completed = null;
            controller.OnTamperCompleted += result => completed = result;

            SynchronousUniTask.Complete(controller.TamperAsync(CancellationToken.None));

            Assert.IsTrue(completed.HasValue, "OnTamperCompleted did not fire");
            Assert.AreEqual(SaveTamperOutcome.Loaded, completed.Value.Outcome);
            Assert.AreEqual(SaveInspectorController.TamperedBalance, completed.Value.LoadedBalance);
        }

        [Test]
        public void TamperAsync_WithHmacOverInMemory_ReportsTheEditRejected()
        {
            SaveInspectorController controller = NewController();
            controller.SetStorage(InMemoryStorageIndex);
            controller.SetCodec(JsonCodecIndex);
            controller.SetProtection(HmacProtectionIndex);
            SynchronousUniTask.Complete(controller.SaveAsync(CancellationToken.None));

            SaveTamperResult? completed = null;
            controller.OnTamperCompleted += result => completed = result;

            SynchronousUniTask.Complete(controller.TamperAsync(CancellationToken.None));

            Assert.IsTrue(completed.HasValue, "OnTamperCompleted did not fire");
            Assert.AreEqual(SaveTamperOutcome.RejectedAsTampered, completed.Value.Outcome);
            Assert.IsInstanceOf<SaveTamperedException>(completed.Value.Error);
        }

        [Test]
        public void TamperAsync_WhileAlreadyBusy_DoesNotRunTheTamperAgain()
        {
            SaveInspectorController controller = NewController();
            controller.SetStorage(InMemoryStorageIndex);
            controller.SetCodec(JsonCodecIndex);
            controller.SetProtection(NoneProtectionIndex);
            SynchronousUniTask.Complete(controller.SaveAsync(CancellationToken.None));

            int completedCount = 0;
            controller.OnTamperCompleted += _ => completedCount++;

            bool reentered = false;
            controller.OnBusyChanged += busy =>
            {
                if (!busy || reentered) return;
                reentered = true;
                SynchronousUniTask.Complete(controller.TamperAsync(CancellationToken.None));
            };

            SynchronousUniTask.Complete(controller.TamperAsync(CancellationToken.None));

            Assert.IsTrue(reentered, "guard: the reentrant call was never attempted");
            Assert.AreEqual(1, completedCount, "a tamper started while one was already running ran the pipeline a second time");
        }
    }
}
