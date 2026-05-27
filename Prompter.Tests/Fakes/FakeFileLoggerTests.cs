using Prompter.Tests.Fakes;
using Xunit;

namespace Prompter.Tests.Fakes;

public class FakeFileLoggerTests
{
    [Fact]
    public void GetRecentLogs_ReturnsMostRecentFirst()
    {
        var logger = new FakeFileLogger();
        logger.Log("First");
        logger.Log("Second");
        logger.Log("Third");

        var logs = logger.GetRecentLogs().ToList();

        Assert.Equal(3, logs.Count);
        Assert.Equal("Third", logs[0].Message);
        Assert.Equal("Second", logs[1].Message);
        Assert.Equal("First", logs[2].Message);
    }
}
