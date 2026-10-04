// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Prowl.Runtime;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// H-RD-53: the frame-end gate must never be waited on forever.
///
/// <para><c>Graphics.BeginFrame</c> re-arms <c>s_renderFrameDone</c> every frame and
/// <c>EndFrameAndWait</c> blocks on it. The render thread only signals that gate from inside its
/// drain loop, so a render thread that failed at startup — the realistic case being
/// <c>MakeCurrent</c> throwing — never signals anything again. Before the watchdog the very first
/// frame then blocked indefinitely: a dead render thread presented as a frozen editor, with no
/// crash and no way out.</para>
///
/// <para>These tests drive the real gate with no render thread running, which is exactly the dead
/// state. They run the call on a worker task with their own outer deadline, so a regression fails
/// the test instead of hanging the whole suite.</para>
/// </summary>
public class RenderThreadWatchdogTests
{
    /// <summary>Saved so a hung watchdog can never outlive the test run.</summary>
    private const int OuterDeadlineMs = 30_000;

    private static (int gateTimeout, Task work) ArmDeadGate(Action body, int gateTimeoutMs)
    {
        int saved = Graphics.RenderGateTimeoutMs;
        Graphics.RenderGateTimeoutMs = gateTimeoutMs;

        // Re-arm the gate exactly as the frame loop does, then never signal it: no render thread.
        Graphics.BeginFrame();
        Task work = Task.Run(body);
        return (saved, work);
    }

    /// <summary>Waits for <paramref name="work"/> without turning its exception into a test failure.
    /// <see cref="Task.Wait(TimeSpan)"/> rethrows an <see cref="AggregateException"/> when the task
    /// faulted, which is the very exception under test here, so <see cref="Task.WaitAny"/> is the
    /// primitive that reports completion only.</summary>
    private static bool CompletesWithin(Task work) =>
        Task.WaitAny([work], OuterDeadlineMs) >= 0;

    /// <summary>The regression itself: with a dead render thread, the frame must fail loudly instead
    /// of blocking forever.</summary>
    [Fact]
    public void EndFrameAndWait_WithNoRenderThread_ThrowsInsteadOfHanging()
    {
        var (saved, work) = ArmDeadGate(Graphics.EndFrameAndWait, 300);

        try
        {
            Assert.True(CompletesWithin(work),
                "EndFrameAndWait never returned: the watchdog is not firing and the test would hang.");

            var timeout = Assert.Throws<TimeoutException>(() => work.GetAwaiter().GetResult());
            Assert.Contains("render thread did not signal", timeout.Message);
        }
        finally
        {
            Graphics.RenderGateTimeoutMs = saved;
        }
    }

    /// <summary>Timing out must not leave the gate signalled, or the next frame would sail past the
    /// check and hide the dead thread.</summary>
    [Fact]
    public void EndFrameAndWait_AfterATimeout_LeavesTheGateDisarmed()
    {
        var (saved, work) = ArmDeadGate(Graphics.EndFrameAndWait, 200);

        try
        {
            Assert.True(CompletesWithin(work));
            Assert.Throws<TimeoutException>(() => work.GetAwaiter().GetResult());
        }
        finally
        {
            Graphics.RenderGateTimeoutMs = saved;
        }

        // A second frame must hit the same wall, not inherit a signal nobody sent.
        var (saved2, work2) = ArmDeadGate(Graphics.EndFrameAndWait, 200);
        try
        {
            Assert.True(CompletesWithin(work2));
            Assert.Throws<TimeoutException>(() => work2.GetAwaiter().GetResult());
        }
        finally
        {
            Graphics.RenderGateTimeoutMs = saved2;
        }
    }

    /// <summary>A signalled gate still returns immediately: the watchdog costs nothing on the happy
    /// path, and <see cref="Graphics.LastFrameWaitMs"/> is still recorded.</summary>
    [Fact]
    public void EndFrameAndWait_WhenTheGateIsSignalled_ReturnsWithoutThrowing()
    {
        int saved = Graphics.RenderGateTimeoutMs;
        Graphics.RenderGateTimeoutMs = 10_000;
        try
        {
            // No BeginFrame here, so the gate keeps its initial signalled state - the state a
            // frame would find if the render thread were alive and already done with the sentinel.
            var work = Task.Run(Graphics.EndFrameAndWait);

            Assert.True(CompletesWithin(work));
            work.GetAwaiter().GetResult(); // must not throw

            Assert.True(Graphics.LastFrameWaitMs < 5000f,
                $"A signalled gate must not wait; measured {Graphics.LastFrameWaitMs:F1} ms.");
        }
        finally
        {
            Graphics.RenderGateTimeoutMs = saved;
        }
    }

    // KNOWN ISSUE: the shutdown half of H-RD-53 - Graphics.Dispose() joining the render thread with
    // a deadline instead of forever - cannot be asserted here, because Dispose() also calls
    // GL.Dispose() on a static GL that is null headless, and it completes the process-wide render
    // queue for every later test. Repro on a machine with a GL context:
    //   1. initialize Graphics, start the render thread, enter the frame loop
    //   2. from another thread, wedge the render thread inside a GL call (or MakeCurrent-fail it)
    //   3. Graphics.Dispose()
    //   4. assert it returns within the deadline and logs "did not exit"   // pre-fix: hung here
    // The frame-side watchdog above is the part that can be regression-tested headless, and it is
    // the one that froze the editor.
}