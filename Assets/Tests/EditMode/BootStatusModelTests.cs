using System.Collections.Generic;
using Company.ChestGame.Core;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Exercises <see cref="BootStatusModel"/>, reachable from edit mode because it is plain C# with
    /// no Unity dependency.
    /// </summary>
    public class BootStatusModelTests
    {
        [Test]
        public void Report_StoresTheMessage()
        {
            BootStatusModel model = new();

            model.Report("Loading...");

            Assert.AreEqual("Loading...", model.Message);
        }

        [Test]
        public void Report_RaisesOnMessageChangedWithTheMessage()
        {
            BootStatusModel model = new();
            List<string> observed = new();
            model.OnMessageChanged += observed.Add;

            model.Report("Loading...");

            CollectionAssert.AreEqual(new[] { "Loading..." }, observed);
        }

        [Test]
        public void ASecondReport_ReplacesTheMessage()
        {
            BootStatusModel model = new();
            List<string> observed = new();
            model.Report("Loading...");
            model.OnMessageChanged += observed.Add;

            model.Report("Ready");

            Assert.AreEqual("Ready", model.Message);
            CollectionAssert.AreEqual(new[] { "Ready" }, observed);
        }
    }
}
