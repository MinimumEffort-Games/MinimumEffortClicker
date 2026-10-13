using MinimumEffortClicker.Engine;

namespace Engine.Tests;

[TestClass]
public sealed class DeferredCleanupRetryTests
{
    [TestMethod]
    public void RequestsBeforeDispatchCoalesceWithoutPolling()
    {
        var work = new Queue<Action>(); int attempts = 0;
        var retry = new DeferredCleanupRetry(work.Enqueue, () => attempts++);
        retry.Request(); retry.Request(); retry.Request();
        Assert.HasCount(1, work);
        work.Dequeue()();
        Assert.AreEqual(1, attempts); Assert.HasCount(0, work);
        retry.Request(); work.Dequeue()();
        Assert.AreEqual(2, attempts); Assert.HasCount(0, work);
    }

    [TestMethod]
    public void PhysicalReleaseDuringRejectedAttemptGetsAnotherAttempt()
    {
        var work = new Queue<Action>(); int attempts = 0; bool held = true, owned = true;
        DeferredCleanupRetry? retry = null;
        retry = new(work.Enqueue, () =>
        {
            attempts++;
            bool blocked = held;
            if (attempts == 1) { held = false; retry!.Request(); }
            if (!blocked) owned = false;
        });
        retry.Request(); work.Dequeue()();
        Assert.AreEqual(2, attempts); Assert.IsFalse(owned); Assert.HasCount(0, work);
    }
}
