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
}
