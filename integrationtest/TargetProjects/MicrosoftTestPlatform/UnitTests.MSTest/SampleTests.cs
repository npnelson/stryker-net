using TargetProject.StrykerFeatures;

namespace NetCoreTestProject.MSTest.MTP;

[TestClass]
public class SampleTests
{
    [TestMethod]
    [DataRow(29, false)]
    [DataRow(31, true)]
    public void TestAgeExplicit(int age, bool expired)
    {
        var sut = new KilledMutants { Age = age };

        var result = sut.IsExpiredBool();

        Assert.IsTrue(expired == result);
    }

    [TestMethod]
    public void TestTimeout()
    {
        var sut = new TargetProject.StrykerFeatures.Timeout();

        sut.SomeLoop();
    }

    [TestMethod]
    public void TestStackOverflow()
    {
        var sut = new TargetProject.StrykerFeatures.StackOverflow();

        // Mutating the recursion makes this overflow the stack and crash the test host,
        // which the MTP runner reports as a RuntimeError mutant.
        Assert.AreEqual(6, sut.SumTo(3));
    }

    [TestMethod]
    public void TestFibonacci()
    {
        // Runs mid-suite by design: this is the first test to execute code from a second
        // mutated assembly (Library), so its injected MutantControl copy initializes while
        // the per-test epoch relay is already mid-session
        var sut = new ExampleClassLibrary.RecursiveMath();

        Assert.AreEqual(0, sut.Fibonacci(3));
    }

    // --- stryker-net#3742 contamination fixture -------------------------------------------
    // These three tests all reach CachedRules, whose static cache is populated once per
    // process. On a reused test host, a ComputeLimit mutant tested earlier leaves the cache
    // holding its mutated value, and the Describe mutant - which survives on a clean host -
    // is then reported killed.

    [TestMethod]
    public void TestCachedLimitIsFifteen()
    {
        var sut = new CachedRules();

        Assert.AreEqual(15, sut.GetLimit("default"));
    }

    [TestMethod]
    public void TestDescribeMentionsTheLimit()
    {
        var sut = new CachedRules();

        // Deliberately loose: mutating the "limit:" literal must survive on a clean host.
        Assert.IsTrue(sut.Describe().Contains("15"));
    }
}

