using System.Reflection;
using Company.ChestGame.Core;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers what <see cref="BootStatusLabel"/> renders once bound to a <see cref="BootStatusModel"/>,
    /// closing the gap docs/WIP.md recorded: no test read the boot status label back.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md. See docs/architecture.md, "Telling the player what boot is doing".
    /// </remarks>
    public class BootStatusLabelTests
    {
        private GameObject _labelObject;

        [TearDown]
        public void TearDown()
        {
            if (_labelObject != null) Object.DestroyImmediate(_labelObject);
        }

        private BootStatusLabel BuildLabel(out TMP_Text text)
        {
            _labelObject = new GameObject("Status Label");
            _labelObject.SetActive(false);

            BootStatusLabel label = _labelObject.AddComponent<BootStatusLabel>();
            text = _labelObject.AddComponent<TextMeshProUGUI>();
            Set(label, "_label", text);

            _labelObject.SetActive(true);
            return label;
        }

        private static void Set(object target, string fieldName, object value) =>
            target.GetType()
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);

        [Test]
        public void Bind_RendersTheModelsCurrentMessageImmediately()
        {
            BootStatusModel model = new();
            model.Report("Loading...");

            BootStatusLabel label = BuildLabel(out TMP_Text text);
            label.Bind(model);

            Assert.AreEqual("Loading...", text.text);
        }

        [Test]
        public void Bind_BeforeAnythingIsReported_LeavesTheAuthoredTextAlone()
        {
            BootStatusModel model = new();
            BootStatusLabel label = BuildLabel(out TMP_Text text);
            text.text = "Loading...";

            label.Bind(model);

            Assert.AreEqual("Loading...", text.text,
                "binding a model nothing has reported to must not blank the text the scene authored");
        }

        [Test]
        public void AfterBinding_ALaterReport_UpdatesTheLabel()
        {
            BootStatusModel model = new();
            BootStatusLabel label = BuildLabel(out TMP_Text text);
            label.Bind(model);

            model.Report("Ready");

            Assert.AreEqual("Ready", text.text);
        }
    }
}
