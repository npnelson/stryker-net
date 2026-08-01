using System;
using System.IO;

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

        // Fibonacci always returns 0, so the printed sequence is its only observable behaviour.
        // Asserting on it is what makes these mutants killable rather than merely covered.
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            sut.Fibonacci(5);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.AreEqual("0 1 1 2 3 ", captured.ToString());
    }
}
