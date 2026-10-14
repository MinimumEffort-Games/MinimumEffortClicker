using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace Core.Tests;

[TestClass]
public sealed class ConditionDefinitionTests
{
    private static readonly ConditionPolicy Policy = new(TimeSpan.FromMilliseconds(100));
    [TestMethod]
    public void ImageTemplatesRejectMalformedOversizedAndExcessiveSearchWork()
    {
        var image = ImageTemplate.FromSample(new(2, 1, new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }));
        var condition = new ConditionDefinition(ConditionKind.ImageAppears, Policy, new(new(0, 0, 300, 300, 96), 8, 8), Template: image);
        condition.Validate();
        Assert.AreEqual((byte)255, image.Decode().Pixels[2]);
        Assert.ThrowsExactly<ArgumentException>(() => (image with { Bgra = "invalid" }).Decode());
        Assert.ThrowsExactly<ArgumentException>(() => (image with { Width = 65 }).Decode());
        Assert.ThrowsExactly<ArgumentException>(() => (image with { Bgra = "AAAA" }).Decode());
        Assert.ThrowsExactly<ArgumentException>(() => (condition with { Template = null }).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => (condition with { Region = condition.Region! with { Width = 1 } }).Validate());
        var large = ImageTemplate.FromSample(new(64, 64, new byte[64 * 64 * 4]));
        Assert.ThrowsExactly<ArgumentException>(() => (condition with { Template = large, Region = condition.Region! with { Width = 256, Height = 256 } }).Validate());
        var bounded = condition with { Template = large, Region = condition.Region! with { Width = 64, Height = 64 } };
        var repeated = Enumerable.Range(0, 17).Select(_ => SequenceStep.WaitUntil("Image", bounded) with { Enabled = false }).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new(repeated)));
    }

    [TestMethod]
    public void SavedImageConditionRoundTripsItsExactPixels()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MEC-images-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "image.mecprofile");
            var image = ImageTemplate.FromSample(new(1, 1, new byte[] { 24, 85, 150, 255 }));
            var condition = new ConditionDefinition(ConditionKind.ImageDisappears, Policy, new(new(2, 3, 100, 100, 96), 4, 4), Template: image);
            var profile = new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Image", [new(Guid.NewGuid(), "Image", null, Sequence: new([SequenceStep.WaitUntil("Image gone", condition)]))]);
            HelperProfiles.Export(path, profile);
            Assert.AreEqual(condition, HelperProfiles.Import(path).Macros[0].Sequence!.Steps[0].Condition);
        }
        finally { Directory.Delete(folder, true); }
    }
    [TestMethod]
    public void ConditionsValidateDisabledStepsAndDoNotCountTowardTheLengthLimit()
    {
        var condition = new ConditionDefinition(ConditionKind.PixelColor, Policy, new(new(2, 3, 100, 100, 96), 1, 1));
        var step = SequenceStep.WaitUntil("Colour", condition);
        Assert.HasCount(1, SequenceCompiler.Compile(new([step])).Conditions);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([step with { Enabled = false, Condition = condition with { Region = condition.Region! with { Width = 2 } } }])));
        // A wait has no time limit, so only the actions count toward the one-day maximum.
        Assert.AreEqual(TimeSpan.FromDays(1), SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.FromDays(1)), step])).WorstCaseDuration);
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.FromDays(1)), step, SequenceStep.Wait(TimeSpan.FromSeconds(1))])));
        Assert.ThrowsExactly<ArgumentException>(() => SequenceCompiler.Compile(new([step with { Duration = TimeSpan.FromSeconds(1) }])));
    }
    [TestMethod]
    public void ConditionProfileRoundTripsWithoutNativeBindingsAndVersionFourStillMigrates()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MEC-conditions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "test.mecprofile");
            var condition = new ConditionDefinition(ConditionKind.WindowActive, Policy with { StableSamples = 2 });
            var profile = new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Wait", [new(Guid.NewGuid(), "Wait", null, Sequence: new([SequenceStep.WaitUntil("Target active", condition)]))]);
            HelperProfiles.Export(path, profile);
            Assert.AreEqual(condition, HelperProfiles.Import(path).Macros[0].Sequence!.Steps[0].Condition);
            var legacy = profile with { Macros = [profile.Macros[0] with { Sequence = new([SequenceStep.Wait(TimeSpan.FromSeconds(1))]) }] };
            HelperProfiles.Export(path, legacy);
            File.WriteAllText(path, File.ReadAllText(path).Replace($"\"schemaVersion\": {ProfileDefinition.CurrentVersion}", "\"schemaVersion\": 4"));
            Assert.AreEqual(ProfileDefinition.CurrentVersion, HelperProfiles.Import(path).SchemaVersion);
        }
        finally { Directory.Delete(folder, true); }
    }
}
