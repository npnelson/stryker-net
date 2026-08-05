using Microsoft.VisualStudio.TestTools.UnitTesting;
using StaticStateProbe;

namespace StaticStateProbe.Tests;

[TestClass]
public class StartupStateTests
{
    [TestMethod]
    public void Candidate01_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate01(1);
    }

    [TestMethod]
    public void Candidate02_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate02(1);
    }

    [TestMethod]
    public void Candidate03_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate03(1);
    }

    [TestMethod]
    public void Candidate04_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate04(1);
    }

    [TestMethod]
    public void Candidate05_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate05(1);
    }

    [TestMethod]
    public void Candidate06_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate06(1);
    }

    [TestMethod]
    public void Candidate07_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate07(1);
    }

    [TestMethod]
    public void Candidate08_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate08(1);
    }

    [TestMethod]
    public void Candidate09_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate09(1);
    }

    [TestMethod]
    public void Candidate10_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate10(1);
    }

    [TestMethod]
    public void Candidate11_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate11(1);
    }

    [TestMethod]
    public void Candidate12_observes_clean_startup_state()
    {
        Assert.IsTrue(StartupState.IsReady);
        _ = CandidateOperations.Candidate12(1);
    }
}
