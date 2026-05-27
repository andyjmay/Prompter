using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Xunit;

namespace Prompter.Tests.Services;

public class FoundryLocalManagerAccessorTests
{
    [Fact]
    public async Task InitializeAsync_Faulted_WhenCreateThrows()
    {
        var logger = new FakeFileLogger();
        var accessor = new ThrowingAccessor(logger);

        var initTask = accessor.InitializeAsync(5);

        var completed = await Task.WhenAny(accessor.InitializationCompleted, Task.Delay(2000));
        Assert.Same(accessor.InitializationCompleted, completed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => accessor.InitializationCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => initTask);
    }

    [Fact]
    public async Task DisposeAsync_AllowsReinitialization()
    {
        var logger = new FakeFileLogger();
        var accessor = new CountingAccessor(logger);

        await accessor.InitializeAsync(5);
        Assert.Equal(1, accessor.InitCount);

        await accessor.DisposeAsync();

        await accessor.InitializeAsync(5);
        Assert.Equal(2, accessor.InitCount);
    }

    private class ThrowingAccessor : FoundryLocalManagerAccessor
    {
        public ThrowingAccessor(IFileLogger fileLogger) : base(fileLogger) { }

        protected override Task InitializeManagerAsync(Configuration config, ILogger logger)
            => throw new InvalidOperationException("boom");
    }

    private class CountingAccessor : FoundryLocalManagerAccessor
    {
        public int InitCount { get; private set; }

        public CountingAccessor(IFileLogger fileLogger) : base(fileLogger) { }

        protected override async Task InitializeManagerAsync(Configuration config, ILogger logger)
        {
            InitCount++;
            await Task.CompletedTask;
        }
    }
}
