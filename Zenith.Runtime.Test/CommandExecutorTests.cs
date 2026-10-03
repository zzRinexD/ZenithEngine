// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Silk.NET.OpenGL;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// What <see cref="CommandExecutor.Execute"/> does with the commands it can interpret without a graphics
/// device: the global-property opcodes, which write straight into the static
/// <see cref="PropertyState"/> dictionaries, plus the dispatch behaviour around them.
/// <para/>
/// The executor has no headless guard (see the KNOWN ISSUE below), so these tests deliberately feed it
/// only opcodes that never reach <c>Graphics.GL</c>, or that return before they do. The 36 GPU opcodes
/// and the whole of <c>PrepareDraw</c>, <c>DoClear</c> and <c>DoBlit</c> are unreachable here and stay a
/// documented coverage gap: the Silk.NET entry points are non-virtual P/Invoke wrappers, so no substitute
/// can stand in for a device.
/// <para/>
/// One executor per test, never <c>Graphics.Executor</c>: that one is a process-lifetime singleton
/// (<c>Graphics.cs:43</c>) whose mirrors would leak between tests, and it cannot be reset.
/// </summary>
public class CommandExecutorTests : IDisposable
{
    private readonly CommandExecutor _executor = new();

    public CommandExecutorTests() => PropertyState.ClearGlobalsInternal();

    public void Dispose()
    {
        PropertyState.ClearGlobalsInternal();
        GC.SuppressFinalize(this);
    }

    /// <summary>A buffer with one global float and one global int recorded, nothing else.</summary>
    private static CommandBuffer GlobalsOnly(string name, float value)
    {
        CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        cmd.SetGlobalFloat(name, value);
        cmd.SetGlobalInt(name, (int)value);
        return cmd;
    }

    /// <summary>Reads PropertyState.s_globalMatrixArr, which has no public getter.</summary>
    private static Dictionary<string, System.Numerics.Matrix4x4[]> GlobalMatrixArray() =>
        (Dictionary<string, System.Numerics.Matrix4x4[]>)typeof(PropertyState)
            .GetField("s_globalMatrixArr", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

    // ================================================================
    //  Global property opcodes
    // ================================================================

    // Globals are process-wide, not per buffer: the opcode path writes the static dictionary directly
    // rather than going through the public setter, which would submit a command buffer from inside the
    // executor and recurse. So an executed command is visible to any reader, with no buffer in hand.
    [Fact]
    public void Execute_GlobalFloat_WritesTheStaticDictionary()
    {
        CommandBuffer cmd = GlobalsOnly("prowl_Exposure", 2.5f);

        _executor.Execute(cmd);

        Assert.Equal(2.5f, PropertyState.GetGlobalFloat("prowl_Exposure"));
        Assert.Equal(2, PropertyState.GetGlobalInt("prowl_Exposure"));
        cmd.Dispose();
    }

    // The public ClearGlobals cannot be used for a reset: it rents a command buffer and submits it, and
    // headless Submit drops the buffer without ever reaching an executor. So the globals stay put, and only
    // the internal entry point - or a ClearAllGlobals opcode actually executed - empties them.
    [Fact]
    public void ClearGlobals_ThePublicApiIsANoOpHeadless_ButTheOpcodeClearsEverything()
    {
        CommandBuffer seed = GlobalsOnly("prowl_Exposure", 7f);
        _executor.Execute(seed);
        seed.Dispose();
        Assert.Equal(7f, PropertyState.GetGlobalFloat("prowl_Exposure"));

        // The public API submits a buffer nobody executes, so nothing is cleared.
        PropertyState.ClearGlobals();
        Assert.Equal(7f, PropertyState.GetGlobalFloat("prowl_Exposure"));

        // Executing the opcode does clear it.
        CommandBuffer clear = Graphics.GetCommandBuffer("test");
        clear.ClearAllGlobals();
        _executor.Execute(clear);
        clear.Dispose();

        Assert.Equal(0f, PropertyState.GetGlobalFloat("prowl_Exposure"));
        Assert.Equal(0, PropertyState.GetGlobalInt("prowl_Exposure"));
    }

    // A null texture is not stored as a null value: it removes the entry, which is what makes
    // "SetGlobalTexture(name, null)" and "ClearGlobalTexture(name)" the same wire command.
    [Fact]
    public void Execute_GlobalTextureWithNull_RemovesTheEntry()
    {
        var texture = new Texture2D(4, 4);

        CommandBuffer set = Graphics.GetCommandBuffer("test");
        set.SetGlobalTexture("prowl_Tex", texture);
        _executor.Execute(set);
        set.Dispose();
        Assert.Same(texture, PropertyState.GetGlobalTexture("prowl_Tex"));

        CommandBuffer clear = Graphics.GetCommandBuffer("test");
        clear.SetGlobalTexture("prowl_Tex", null);
        _executor.Execute(clear);
        clear.Dispose();

        Assert.Null(PropertyState.GetGlobalTexture("prowl_Tex"));
    }

    // A global buffer carries its binding point alongside it, and the opcode writes both dictionaries in
    // one step, so a shader that reads the sampler gets the point the setter was given.
    [Fact]
    public void Execute_GlobalBuffer_WritesTheBufferAndItsBindingPoint()
    {
        GraphicsBuffer buffer = Graphics.CreateBuffer(BufferType.UniformBuffer, new byte[256]);

        CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        cmd.SetGlobalBuffer("prowl_Buf", buffer, 7u);
        _executor.Execute(cmd);
        cmd.Dispose();

        Assert.Same(buffer, PropertyState.GetGlobalBuffer("prowl_Buf"));
        Assert.Equal(7u, PropertyState.GetGlobalBufferBinding("prowl_Buf"));
    }

    // Matrix arrays are the one global with no public getter, and the executor converts them to the layout
    // the uniform block expects on the way in.
    [Fact]
    public void Execute_GlobalMatrices_ConvertsThemIntoTheUploadLayout()
    {
        Float4x4[] values = [Float4x4.Identity, Float4x4.CreateScale(2f, 2f, 2f)];

        CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        cmd.SetGlobalMatrices("prowl_Mats", values);
        _executor.Execute(cmd);
        cmd.Dispose();

        System.Numerics.Matrix4x4[] stored = GlobalMatrixArray()["prowl_Mats"];
        Assert.Equal(2, stored.Length);
        Assert.Equal(2f, stored[1].M11);
    }

    // ================================================================
    //  Dispatch around the GPU opcodes
    // ================================================================

    // KNOWN ISSUE: Execute has no IsHeadless guard and no try. Headless, Graphics.GL is null, so the first
    // opcode that touches the device throws and the rest of the buffer is silently dropped - with every
    // CPU-side mutation made before it already committed, and no way to tell "skipped" from "half
    // applied". In production this cannot happen because Submit drops the buffer before it ever reaches an
    // executor, which is why it is a testability limitation rather than a live crash.
    // See docs/PLAN_10_DE_10.md Fase 6. (H-RD-20)
    [Fact]
    public void Execute_ACommandThatTouchesTheDevice_ThrowsNullReferenceWithoutAGuard()
    {
        Assert.True(Graphics.IsHeadless);
        CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        cmd.SetViewport(0, 0, 64, 64);

        var failure = Assert.Throws<NullReferenceException>(() => _executor.Execute(cmd));

        Assert.Contains("CommandExecutor", failure.StackTrace);
        cmd.Dispose();
    }

    // The stream is decoded and executed in one pass, so an exception abandons everything still queued:
    // a global recorded after a device command never lands.
    [Fact]
    public void Execute_ADeviceCommandBeforeAGlobal_AbandonsTheRestOfTheBuffer()
    {
        CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        cmd.SetViewport(0, 0, 64, 64);
        cmd.SetGlobalInt("prowl_Late", 5);

        Assert.Throws<NullReferenceException>(() => _executor.Execute(cmd));

        Assert.Equal(0, PropertyState.GetGlobalInt("prowl_Late"));
        cmd.Dispose();
    }

    // The mirror image, and the reason the hazard above matters: the global recorded *before* the device
    // command stays committed. A buffer that fails halfway leaves the globals half-updated, and the
    // executor's own mirrors stay that way for the life of the process.
    [Fact]
    public void Execute_AGlobalBeforeADeviceCommand_StaysCommittedEvenThoughTheBufferFails()
    {
        CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        cmd.SetGlobalInt("prowl_Early", 5);
        cmd.SetViewport(0, 0, 64, 64);

        Assert.Throws<NullReferenceException>(() => _executor.Execute(cmd));

        Assert.Equal(5, PropertyState.GetGlobalInt("prowl_Early"));
        cmd.Dispose();
    }

    // The opcode enum starts at 1, so a zeroed or corrupted header reaches the switch's default arm
    // instead of matching anything. That is what makes a truncated stream detectable at all.
    [Fact]
    public void Execute_AnUnknownOpcode_ThrowsNamingIt()
    {
        CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        cmd.SetViewport(0, 0, 1, 1);
        cmd._stream[0] = 0xFF;
        cmd._stream[1] = 0xFF;

        var failure = Assert.Throws<InvalidOperationException>(() => _executor.Execute(cmd));

        Assert.Contains("Unknown command opcode", failure.Message);
        cmd.Dispose();
    }

    // The topology translation is a pure function, reachable by reflection. Quads has no case and falls
    // through to the default, so a legacy quad mesh would be drawn as an unrelated triangle list.
    [Fact]
    public void ToGL_MapsEveryTopologyAndFallsBackToTrianglesForQuads()
    {
        MethodInfo toGl = typeof(CommandExecutor).GetMethod("ToGL", BindingFlags.Static | BindingFlags.NonPublic)!;
        PrimitiveType Map(Topology topology) => (PrimitiveType)toGl.Invoke(null, [topology])!;

        Assert.Equal(PrimitiveType.Points, Map(Topology.Points));
        Assert.Equal(PrimitiveType.Lines, Map(Topology.Lines));
        Assert.Equal(PrimitiveType.LineLoop, Map(Topology.LineLoop));
        Assert.Equal(PrimitiveType.LineStrip, Map(Topology.LineStrip));
        Assert.Equal(PrimitiveType.Triangles, Map(Topology.Triangles));
        Assert.Equal(PrimitiveType.TriangleStrip, Map(Topology.TriangleStrip));
        Assert.Equal(PrimitiveType.TriangleFan, Map(Topology.TriangleFan));

        // Topology.Quads is declared by the engine but has no mapping, and anything out of range does not
        // either: both silently rasterize as triangles.
        Assert.Equal(PrimitiveType.Triangles, Map(Topology.Quads));
        Assert.Equal(PrimitiveType.Triangles, Map((Topology)99));
    }
}