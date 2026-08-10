namespace NetCoreTestProject.MSTest.MTP;

[TestClass]
public class FibonacciTests
{
    [TestMethod]
    public void TestFibonacci()
    {
        // In solution mode this test executes code from a second mutated assembly (Library), so
        // the test host carries two injected MutantControl copies sharing one coverage file
        var sut = new ExampleClassLibrary.RecursiveMath();

        Assert.AreEqual(0, sut.Fibonacci(3));
    }
}
