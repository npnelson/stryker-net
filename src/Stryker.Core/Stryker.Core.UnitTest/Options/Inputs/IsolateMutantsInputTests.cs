using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Configuration.Options.Inputs;
using TestRunnerOption = Stryker.Abstractions.Options.TestRunner;

namespace Stryker.Core.UnitTest.Options.Inputs;

[TestClass]
public class IsolateMutantsInputTests : TestBase
{
    [TestMethod]
    [DataRow(null, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public void ShouldValidateForMicrosoftTestPlatform(bool? input, bool expected)
    {
        var logger = new Mock<ILogger<IsolateMutantsInput>>();
        var target = new IsolateMutantsInput { SuppliedInput = input };

        var result = target.Validate(TestRunnerOption.MicrosoftTestPlatform, logger.Object);

        result.ShouldBe(expected);
        logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void ShouldWarnWhenEnabledForVsTest()
    {
        var logger = new Mock<ILogger<IsolateMutantsInput>>();
        var target = new IsolateMutantsInput { SuppliedInput = true };

        var result = target.Validate(TestRunnerOption.VsTest, logger.Object);

        result.ShouldBeTrue();
        logger.Verify(
            LogLevel.Warning,
            "Mutant process isolation was requested but only applies to the Microsoft Test Platform runner; it is ignored for the VsTest runner.");
        logger.VerifyNoOtherCalls();
    }
}
