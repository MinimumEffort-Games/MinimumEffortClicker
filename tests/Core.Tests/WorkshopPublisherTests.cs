using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class WorkshopPublisherTests
{
    /// <summary>Steam stand-in: records every upload and answers with whatever the test queued.</summary>
    private sealed class FakeWorkshop : IWorkshopClient
    {
        public List<WorkshopUpload> Uploads { get; } = [];
        public Queue<Func<WorkshopUpload, WorkshopPublishResult>> Answers { get; } = new();
        public bool Available => true;
        public string? UnavailableReason => null;
        public event Action? ItemsChanged { add { } remove { } }
        public void Pump() { }
        public Task<WorkshopPublishResult> PublishAsync(WorkshopUpload upload, IProgress<double>? progress = null)
        {
            Assert.IsTrue(File.Exists(Path.Combine(upload.ContentFolder, WorkshopMacros.UploadFileName)), "Steam reads the folder while uploading.");
            Uploads.Add(upload);
            return Task.FromResult(Answers.Dequeue()(upload));
        }
        public IReadOnlyList<WorkshopInstalledItem> InstalledItems() => [];
        public void OpenWorkshop() { }
        public void OpenItem(ulong itemId) { }
        public void Dispose() { }
    }

    private static string root = "";
    [TestInitialize] public void Folder() { root = Path.Combine(Path.GetTempPath(), "MEC-publish-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
    [TestCleanup] public void Clean() => Directory.Delete(root, true);

    private static string MacroFile()
    {
        var step = QuickActions.Click(PointerButton.Left, QuickClickKind.Single, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(20), new(1, 1, 800, 600, 96));
        string path = Path.Combine(root, "Farm.mecprofile");
        HelperProfiles.Export(path, new(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Farm", [new(Guid.NewGuid(), "Farm", null, Sequence: new([step]))]));
        return path;
    }

    [TestMethod]
    public async Task AFirstShareCreatesTheItemAndTheNextOneUpdatesIt()
    {
        var steam = new FakeWorkshop(); var shares = new WorkshopShares(root); var macro = Guid.NewGuid(); string file = MacroFile();
        var publisher = new WorkshopPublisher(steam, shares, Path.Combine(root, "work"));
        steam.Answers.Enqueue(_ => new(true, 77, "Shared on the Workshop."));
        var first = await publisher.ShareAsync(macro, file, "Farm", "Loops the farm.", WorkshopVisibility.Public, null, () => "logo.png");
        Assert.IsTrue(first.Success);
        Assert.IsNull(steam.Uploads[0].ExistingItem); Assert.AreEqual("logo.png", steam.Uploads[0].PreviewImage, "A new item gets the logo.");
        Assert.AreEqual(new WorkshopShare(77, "Farm", "Loops the farm.", WorkshopVisibility.Public), shares.Find(macro));

        steam.Answers.Enqueue(_ => new(true, 77, "Workshop item updated."));
        await publisher.ShareAsync(macro, file, "Farm v2", "Faster.", WorkshopVisibility.FriendsOnly, null, () => throw new AssertFailedException("An update keeps its picture."));
        Assert.AreEqual(77UL, steam.Uploads[1].ExistingItem);
        Assert.IsNull(steam.Uploads[1].PreviewImage);
        Assert.AreEqual("Farm v2", shares.Find(macro)!.Title);
        Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(root, "work")).Length, "Upload folders are removed.");
    }

    [TestMethod]
    public async Task AnItemDeletedOnSteamIsSharedAgainAsANewItem()
    {
        var steam = new FakeWorkshop(); var shares = new WorkshopShares(root); var macro = Guid.NewGuid();
        shares.Set(macro, new(55, "Old", "", WorkshopVisibility.Public));
        steam.Answers.Enqueue(_ => new(false, 55, "deleted", ItemGone: true));
        steam.Answers.Enqueue(_ => new(true, 88, "Shared on the Workshop."));
        var result = await new WorkshopPublisher(steam, shares, Path.Combine(root, "work")).ShareAsync(macro, MacroFile(), "Farm", "", WorkshopVisibility.Public, null, () => "logo.png");
        Assert.IsTrue(result.Success);
        Assert.IsNull(steam.Uploads[1].ExistingItem); Assert.AreEqual("logo.png", steam.Uploads[1].PreviewImage);
        Assert.AreEqual(88UL, shares.Find(macro)!.ItemId);
    }

    [TestMethod]
    public async Task AFailedShareRemembersNothingNew()
    {
        var steam = new FakeWorkshop(); var shares = new WorkshopShares(root); var macro = Guid.NewGuid();
        steam.Answers.Enqueue(_ => new(false, 0, "Steam is busy."));
        var result = await new WorkshopPublisher(steam, shares, Path.Combine(root, "work")).ShareAsync(macro, MacroFile(), "Farm", "", WorkshopVisibility.Private, null, () => "logo.png");
        Assert.IsFalse(result.Success);
        Assert.IsNull(shares.Find(macro));
    }
}
