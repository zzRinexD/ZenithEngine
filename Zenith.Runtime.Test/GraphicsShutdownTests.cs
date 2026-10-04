// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// H-RD-52: <c>Graphics.Submit</c> must be a total function across process shutdown.
///
/// <para>Every GPU resource with a finalizer (<c>Mesh</c>, <c>Texture</c>, <c>RenderTexture</c>,
/// <c>AudioClip</c>) disposes itself from the finalizer thread, and disposal ends in
/// <c>Graphics.Submit</c>. <c>Graphics.Dispose</c> calls <c>CompleteAdding()</c> on the render
/// queue, so a <c>Submit</c> that arrives after it throws <see cref="InvalidOperationException"/> —
/// and an unhandled exception on a finalizer thread terminates the process. Reproduces as:
/// open a project with textures, close the editor, let the GC reclaim a texture.</para>
///
/// <para><b>Scope of what can be asserted headless.</b> These tests run without a GL context, so
/// <c>Graphics.IsHeadless</c> is already true and <c>Submit</c> takes the early-return path whether
/// or not the shutdown guard exists. They therefore pin the <i>contract</i> (submitting after
/// shutdown is a no-op that returns the buffer, and never throws) and the flag itself — they cannot
/// distinguish guarded from unguarded code in this environment. The distinguishing scenario is
/// recorded as the KNOWN ISSUE below.</para>
/// </summary>
public class GraphicsShutdownTests
{
    /// <summary>After shutdown begins, <c>Submit</c> must drop the work and recycle the buffer
    /// instead of queueing it on the completed collection.</summary>
    [Fact]
    public void Submit_AfterShutdownBegins_IsANoOpAndDoesNotThrow()
    {
        using var cmd = Graphics.GetCommandBuffer("H-RD-52 submit");
        Graphics.SetShuttingDown(true);
        try
        {
            // The whole point: this used to be able to throw InvalidOperationException from a
            // finalizer thread, which kills the process rather than the resource.
            Graphics.Submit(cmd);
        }
        finally
        {
            Graphics.SetShuttingDown(false);
        }
    }

    /// <summary>Same contract for the blocking variant, which also parks on a render-thread job.</summary>
    [Fact]
    public void SubmitAndWait_AfterShutdownBegins_IsANoOpAndDoesNotThrow()
    {
        using var cmd = Graphics.GetCommandBuffer("H-RD-52 submit-and-wait");
        Graphics.SetShuttingDown(true);
        try
        {
            Graphics.SubmitAndWait(cmd);
        }
        finally
        {
            Graphics.SetShuttingDown(false);
        }
    }

    /// <summary>The buffer is returned to the pool rather than leaked, so a late finalizer cannot
    /// exhaust it.</summary>
    [Fact]
    public void Submit_AfterShutdownBegins_RecyclesTheCommandBuffer()
    {
        var cmd = Graphics.GetCommandBuffer("H-RD-52 recycle");
        Graphics.SetShuttingDown(true);
        try
        {
            Graphics.Submit(cmd);
        }
        finally
        {
            Graphics.SetShuttingDown(false);
        }

        // Handed back rather than leaked: encoding is closed because the buffer is pool-owned again.
        var closed = Assert.Throws<InvalidOperationException>(() => cmd.SetViewport(0, 0, 1, 1));
        Assert.Contains("returned to the pool", closed.Message);
    }

    [Fact]
    public void IsShuttingDown_FlipsWithTheFlag()
    {
        Assert.False(Graphics.IsShuttingDown);

        Graphics.SetShuttingDown(true);
        try
        {
            Assert.True(Graphics.IsShuttingDown);
        }
        finally
        {
            Graphics.SetShuttingDown(false);
        }

        Assert.False(Graphics.IsShuttingDown);
    }

    // KNOWN ISSUE: after Graphics.Dispose() the render queue is completed, so an unguarded Submit
    // throws. Asserting that end-to-end needs a live GL context, because headless Submit already
    // returns early via IsHeadless and never reaches the Add. Repro once a GPU test harness exists:
    //   1. initialize Graphics on a real context and start the render thread
    //   2. create a Texture, drop the last reference without disposing it
    //   3. Graphics.Dispose()                      // CompleteAdding + Join
    //   4. GC.Collect(); GC.WaitForPendingFinalizers()
    //   5. assert the process is still alive       // pre-fix: terminated on the finalizer thread
}