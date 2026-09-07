using System;

namespace Company.ChestGame.Saving.Demo
{
    // The demo was asked to do something it cannot honestly do. Under InvalidOperationException,
    // not ChestGameException, for the reason PoolRaceException gives: this is the demo wired wrong,
    // not a player-facing content failure. See docs/architecture.md, "Exception hierarchy".
    public class SaveInspectorException : InvalidOperationException
    {
        public SaveInspectorException(string message) : base(message) { }

        public static SaveInspectorException NothingToTamper(string key) =>
            new($"Nothing is stored under '{key}' to tamper with; run the probe first");

        // The panel's own two. Authoring faults, not player-facing ones, so they go through the
        // same door PoolRaceException.NoDocument/MissingElement already use for the pooling demo.
        public static SaveInspectorException NoDocument() =>
            new("The save inspector panel has no UIDocument assigned, so there is no chrome to bind to");

        public static SaveInspectorException MissingElement(string type, string name) =>
            new($"SaveInspector.uxml has no {type} named '{name}', so the panel cannot bind to it");
    }
}
