using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Prompter.Services;
using Prompter.Tests.Fakes;
using Xunit;

namespace Prompter.Tests.Services;

public class HotkeyServiceTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    private static HotkeyService CreateServiceWithKey(uint vkCode)
    {
        var logger = new FakeFileLogger();
        var service = new HotkeyService(logger);
        var requiredKeyField = typeof(HotkeyService).GetField("_requiredKeyVk", BindingFlags.NonPublic | BindingFlags.Instance);
        requiredKeyField!.SetValue(service, vkCode);
        return service;
    }

    [Fact]
    public void RecordingStarted_IsNotInvokedWhileLockIsHeld()
    {
        var service = CreateServiceWithKey(0x41); // 'A'
        var stateLockField = typeof(HotkeyService).GetField("_stateLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var stateLock = stateLockField!.GetValue(service)!;

        bool eventFired = false;
        bool lockWasHeldDuringEvent = false;

        service.RecordingStarted += () =>
        {
            eventFired = true;
            lockWasHeldDuringEvent = Monitor.IsEntered(stateLock);
        };

        var kbd = new KBDLLHOOKSTRUCT { vkCode = 0x41, flags = 0 };
        var size = Marshal.SizeOf<KBDLLHOOKSTRUCT>();
        var lParam = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(kbd, lParam, false);

        var hookCallback = typeof(HotkeyService).GetMethod("HookCallback", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            hookCallback!.Invoke(service, new object[] { 0, (IntPtr)WM_KEYDOWN, lParam });
        }
        finally
        {
            Marshal.FreeHGlobal(lParam);
        }

        Assert.True(eventFired, "RecordingStarted should have fired.");
        Assert.False(lockWasHeldDuringEvent, "RecordingStarted should not be invoked while _stateLock is held.");
    }

    [Fact]
    public async Task RecordingStopped_IsNotInvokedWhileLockIsHeld()
    {
        var service = CreateServiceWithKey(0x41);
        var stateLockField = typeof(HotkeyService).GetField("_stateLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var stateLock = stateLockField!.GetValue(service)!;
        var recordingStartTimeField = typeof(HotkeyService).GetField("_recordingStartTime", BindingFlags.NonPublic | BindingFlags.Instance);
        recordingStartTimeField!.SetValue(service, DateTime.Now);

        bool eventFired = false;
        bool lockWasHeldDuringEvent = false;

        service.RecordingStopped += () =>
        {
            eventFired = true;
            lockWasHeldDuringEvent = Monitor.IsEntered(stateLock);
        };

        var startReleasePolling = typeof(HotkeyService).GetMethod("StartReleasePolling", BindingFlags.NonPublic | BindingFlags.Instance);
        startReleasePolling!.Invoke(service, null);

        // Wait for the polling task to detect key release and fire RecordingStopped
        var pollTaskField = typeof(HotkeyService).GetField("_pollTask", BindingFlags.NonPublic | BindingFlags.Instance);
        var pollTask = (Task?)pollTaskField!.GetValue(service);
        if (pollTask != null)
        {
            try
            {
                await pollTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                Assert.Fail("Polling task did not complete within timeout.");
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // expected
            }
        }

        Assert.True(eventFired, "RecordingStopped should have fired.");
        Assert.False(lockWasHeldDuringEvent, "RecordingStopped should not be invoked while _stateLock is held.");
    }

    [Fact]
    public void HotkeyService_ImplementsIDisposable()
    {
        var service = new HotkeyService(new FakeFileLogger());
        Assert.IsAssignableFrom<IDisposable>(service);
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenNeverInitialized()
    {
        var service = new HotkeyService(new FakeFileLogger());
        var disposeMethod = typeof(HotkeyService).GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance)
                            ?? typeof(HotkeyService).GetMethod("Dispose", BindingFlags.NonPublic | BindingFlags.Instance);
        if (disposeMethod == null)
        {
            Assert.Fail("HotkeyService does not have a Dispose method.");
        }

        var ex = Record.Exception(() => disposeMethod.Invoke(service, null));
        Assert.Null(ex);
    }

    [Fact]
    public async Task RapidStartReleasePolling_DoesNotThrowObjectDisposedException()
    {
        var logger = new FakeFileLogger();
        var service = new HotkeyService(logger);

        var recordingStartTimeField = typeof(HotkeyService).GetField("_recordingStartTime", BindingFlags.NonPublic | BindingFlags.Instance);
        // Pre-age the start time so every polling task immediately sees elapsed >= 300 ms and exits quickly
        recordingStartTimeField!.SetValue(service, DateTime.Now - TimeSpan.FromMilliseconds(400));

        var startReleasePolling = typeof(HotkeyService).GetMethod("StartReleasePolling", BindingFlags.NonPublic | BindingFlags.Instance);

        var ex = await Record.ExceptionAsync(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                startReleasePolling!.Invoke(service, null);
                await Task.Yield();
            }

            // Allow any remaining fire-and-forget disposal tasks to complete
            await Task.Delay(500);
        });

        Assert.Null(ex);

        // Dispose should also not throw
        var disposeEx = Record.Exception(() => ((IDisposable)service).Dispose());
        Assert.Null(disposeEx);
    }
}
