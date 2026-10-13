using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class TrayPreferencesTests
{
    [TestMethod]
    public void OldPreferencesKeepExistingSettingsAndReceiveTheFirstCloseNotice()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MEC-tray-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new Preferences(folder);
            Assert.IsTrue(store.Load().CloseToTray);
            Assert.IsFalse(store.Load().TrayNoticeShown);
            File.WriteAllText(Path.Combine(folder, "preferences.json"), """{"SchemaVersion":1,"Appearance":2,"ToggleKey":119,"StopKey":120,"HoldTrigger":true,"Overlay":false,"ClicksPerSecond":8,"ScaleClickPositions":false}""");
            var old = store.Load();
            Assert.IsTrue(old.CloseToTray);
            Assert.IsFalse(old.TrayNoticeShown);
            Assert.AreEqual(8, old.ClicksPerSecond);
            Assert.AreEqual(2, old.Appearance);
            Assert.IsTrue(old.HoldTrigger);
            Assert.IsFalse(old.ScaleClickPositions);
            Assert.IsFalse(old.Overlay);
            store.Save(old with { CloseToTray = false, TrayNoticeShown = true });
            Assert.AreEqual(old with { CloseToTray = false, TrayNoticeShown = true }, store.Load());
        }
        finally { Directory.Delete(folder, true); }
    }
}
