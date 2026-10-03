// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Test.RenderTestHelpers;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// What a <see cref="CommandBuffer"/> records, read back off the CPU.
/// <para/>
/// A command buffer is a byte-stream recorder: it makes no GL call, it appends a 2-byte opcode plus a
/// fixed-size payload and pushes object references into a side table. That makes the whole encoder
/// surface assertable headless, which is what these tests do through <see cref="StreamDecoder"/>.
/// <para/>
/// Headless is not configured but merely inherited: <c>Graphics.IsHeadless</c> is a computed property
/// (<c>GL == null</c>), so it is true because nothing ever calls <c>Graphics.Initialize()</c>.
/// <c>RenderStats</c> is process-global, hence BeginFrame/EndFrame around the assertions that read it.
/// </summary>
public class CommandBufferTests : RuntimeTestBase
{
    /// <summary>A fresh buffer straight from the pool, never submitted.</summary>
    private static CommandBuffer Rent(string name = "test") => Graphics.GetCommandBuffer(name);

    private static GraphicsVertexArray NewVertexArray()
    {
        var format = new VertexFormat([new VertexFormat.Element(VertexFormat.VertexSemantic.Position, VertexFormat.VertexType.Float, (byte)3)]);
        GraphicsBuffer vertices = Graphics.CreateBuffer(BufferType.VertexBuffer, new float[3]);
        return new GraphicsVertexArray(format, vertices, indices: null);
    }

    // ================================================================
    //  Stream layout
    // ================================================================

    // A command is a 2-byte opcode header followed by its payload, packed end to end. SetViewport writes
    // four 4-byte values, so the whole command is 18 bytes and the payload decodes back to the arguments.
    [Fact]
    public void SetViewport_RecordsAHeaderAndSixteenPayloadBytes()
    {
        using CommandBuffer cmd = Rent();

        cmd.SetViewport(1, 2, 3, 4);

        var decoder = new StreamDecoder(cmd);
        Assert.Equal(CommandOpcode.SetViewport, decoder.ReadOpcode());
        Assert.Equal(1, decoder.ReadI32());
        Assert.Equal(2, decoder.ReadI32());
        Assert.Equal(3u, decoder.ReadU32());
        Assert.Equal(4u, decoder.ReadU32());
        Assert.True(decoder.AtEnd);
        Assert.Equal(18, cmd._streamPos);
    }

    // Nothing in the encoder deduplicates: three identical viewports record three opcodes. Redundant
    // state is dropped later, by the executor, which is where the framebuffer/VAO/raster mirrors live.
    // An optimisation added here would change what the stream contains, so this pins it on purpose.
    [Fact]
    public void SetViewport_RepeatedWithTheSameValues_RecordsEveryCommand()
    {
        using CommandBuffer cmd = Rent();

        cmd.SetViewport(0, 0, 64, 64);
        cmd.SetViewport(0, 0, 64, 64);
        cmd.SetViewport(0, 0, 64, 64);

        Assert.Equal(3, new StreamDecoder(cmd).CountOf(CommandOpcode.SetViewport));
        Assert.Equal(54, cmd._streamPos);
    }

    // Payloads past the current stream force a doubling copy. Variable-length data is *not* what grows
    // it - blobs are parked in the transient store and referenced by a 12-byte handle - so growth has to
    // be forced with commands whose payload is inline. How much that takes is not knowable up front: the
    // stream comes from the shared ArrayPool, so a recycled buffer may already be far larger than the
    // 4096 bytes the constructor asks for. The loop below therefore keeps going until the array actually
    // changes identity. What matters is that the bytes written before the growth survive the move, which
    // the first command's payload proves.
    [Fact]
    public void EnsureCapacity_GrowsTheStream_AndKeepsEarlierCommands()
    {
        using CommandBuffer cmd = Rent();
        cmd.SetViewport(7, 8, 9, 10);
        int sizeBefore = cmd._stream.Length;

        int written = 0;
        while (cmd._stream.Length == sizeBefore && written < 100_000)
        {
            cmd.SetViewport(written, 0, 1, 1);
            written++;
        }

        Assert.True(cmd._stream.Length > sizeBefore, $"stream never grew from {sizeBefore}");

        var decoder = new StreamDecoder(cmd);
        Assert.Equal(CommandOpcode.SetViewport, decoder.ReadOpcode());
        Assert.Equal(7, decoder.ReadI32());
        Assert.Equal(8, decoder.ReadI32());
        Assert.Equal(9u, decoder.ReadU32());
        Assert.Equal(10u, decoder.ReadU32());
        Assert.Equal(1 + written, new StreamDecoder(cmd).CountOf(CommandOpcode.SetViewport));
    }

    // Growth is never undone: returning the buffer to the pool rewinds the cursor but keeps the grown
    // array. One frame that needs a big stream therefore pins that array for the buffer's whole life, and
    // the pool holds up to 64 of them.
    [Fact]
    public void OnReturn_AfterGrowingTheStream_KeepsTheGrownArray()
    {
        CommandBuffer cmd = Rent();
        int sizeBefore = cmd._stream.Length;
        for (int i = 0; i < 100_000 && cmd._stream.Length == sizeBefore; i++)
            cmd.SetViewport(i, 0, 1, 1);

        int grown = cmd._stream.Length;
        Assert.True(grown > sizeBefore, "the stream never grew, so there is nothing to check");

        cmd.OnReturn();

        Assert.Equal(0, cmd._streamPos);
        Assert.True(cmd._stream.Length >= grown, $"stream was trimmed: {grown} -> {cmd._stream.Length}");
        cmd.Dispose();
    }

    // ================================================================
    //  Object table and name interning
    // ================================================================

    // Object references travel as ushort indices into one table, and names are interned into that same
    // table, so the same name recorded twice costs one slot and both payloads point at it.
    [Fact]
    public void InternName_TheSameNameTwice_ReusesOneObjectTableSlot()
    {
        using CommandBuffer cmd = Rent();

        cmd.SetGlobalFloat("prowl_Shared", 1f);
        cmd.SetGlobalFloat("prowl_Shared", 2f);
        cmd.SetGlobalFloat("prowl_Other", 3f);

        Assert.Equal(2, cmd._objects.Count);
        Assert.Equal(new object?[] { "prowl_Shared", "prowl_Other" }, cmd._objects);

        var decoder = new StreamDecoder(cmd);
        Assert.Equal(CommandOpcode.SetGlobalFloat, decoder.ReadOpcode());
        ushort first = decoder.ReadObjectIndex();
        Assert.Equal(1f, decoder.ReadF32());
        Assert.Equal(CommandOpcode.SetGlobalFloat, decoder.ReadOpcode());
        Assert.Equal(first, decoder.ReadObjectIndex());
        Assert.Equal(2f, decoder.ReadF32());
    }

    // The table is indexed with a ushort, and the bound is checked *before* the entry is added, so 65536
    // entries fit and the next one is refused without leaving a partial entry behind.
    [Fact]
    public void PushObject_OnceTheTableIsFull_RefusesFurtherEntries()
    {
        using CommandBuffer cmd = Rent();

        // Index 65535 is the last that fits, so 65536 distinct objects are accepted.
        for (int i = 0; i <= ushort.MaxValue; i++)
            cmd.ClearGlobalTexture("n" + i);

        Assert.Equal(ushort.MaxValue + 1, cmd._objects.Count);

        var overflow = Assert.Throws<InvalidOperationException>(() => cmd.ClearGlobalTexture("one-too-many"));
        Assert.Contains("65k entries", overflow.Message);
        Assert.Equal(ushort.MaxValue + 1, cmd._objects.Count);
    }

    // ================================================================
    //  Snapshotting
    // ================================================================

    // The class documents that property payloads are captured at encode time so the caller may mutate the
    // original immediately afterwards. This is the promise that lets the pipeline set keywords on a shared
    // material between two Blit calls without corrupting the earlier one.
    [Fact]
    public void SetMaterialProperties_MaterialMutatedAfterwards_LeavesTheSnapshotIntact()
    {
        using CommandBuffer cmd = Rent();
        var material = new Material();
        material.SetFloat("prowl_Value", 1f);

        cmd.SetMaterialProperties(material);
        material.SetFloat("prowl_Value", 99f);

        PropertyState snapshot = (PropertyState)cmd._objects[0];
        Assert.Equal(1f, snapshot.GetFloat("prowl_Value"));
        Assert.Equal(99f, material._properties.GetFloat("prowl_Value"));
    }

    // Every snapshot a buffer rents is tracked and handed back when the buffer returns to the pool;
    // without that the pool would grow for the lifetime of the process.
    [Fact]
    public void SetProperties_TracksEverySnapshotAndReturnsThemOnReturn()
    {
        CommandBuffer cmd = Rent();
        var first = new PropertyState();
        var second = new PropertyState();
        first.SetFloat("a", 1f);
        second.SetFloat("b", 2f);

        cmd.SetProperties(first);
        cmd.SetProperties(second);

        Assert.Equal(2, RentedSnapshotCount(cmd));
        Assert.Equal(2, cmd._objects.Count);

        cmd.OnReturn();

        Assert.Equal(0, RentedSnapshotCount(cmd));
        cmd.Dispose();
    }

    // KNOWN ISSUE: SetGlobalMatrices is the one payload that is *not* copied into the transient store. It
    // pushes the caller's live array by reference, so the encoder's documented snapshot promise does not
    // hold for it, and the executor copies at execute time - possibly several frames later, after the
    // caller has reused the array. See docs/PLAN_10_DE_10.md Fase 6. (H-RD-4)
    [Fact]
    public void SetGlobalMatrices_ArrayMutatedAfterwards_IsVisibleThroughTheRecordedReference()
    {
        using CommandBuffer cmd = Rent();
        Float4x4[] values = [Float4x4.Identity, Float4x4.Identity];

        cmd.SetGlobalMatrices("prowl_Mats", values);
        values[0] = Float4x4.Zero;

        // The stream still points at the same array, so the mutation is visible: nothing was copied.
        var decoder = new StreamDecoder(cmd);
        Assert.Equal(CommandOpcode.SetGlobalMatrices, decoder.ReadOpcode());
        decoder.ReadInternedName();

        Float4x4[] recorded = (Float4x4[])decoder.ReadObject()!;
        Assert.Same(values, recorded);
        Assert.Equal(Float4x4.Zero, recorded[0]);
    }

    // ================================================================
    //  Pool lifecycle
    // ================================================================

    // Encoding is closed once the buffer is back in the pool, and the guard is the in-pool flag rather than
    // the submitted flag, because returning to the pool clears the latter.
    [Fact]
    public void Encoding_AfterTheBufferIsBackInThePool_Throws()
    {
        CommandBuffer cmd = Rent();
        cmd.Dispose();
        Assert.True(cmd._inPool);

        var closed = Assert.Throws<InvalidOperationException>(() => cmd.SetViewport(0, 0, 1, 1));
        Assert.Contains("returned to the pool", closed.Message);
    }

    // Returning is idempotent, so a double Dispose cannot put the same buffer in the free list twice.
    [Fact]
    public void Dispose_CalledTwice_ReturnsTheBufferToThePoolOnlyOnce()
    {
        CommandBuffer cmd = Rent();
        cmd.SetViewport(1, 1, 1, 1);

        cmd.Dispose();
        Assert.True(cmd._inPool);
        Assert.False(cmd._ownerReleased);
        Assert.Equal(0, cmd._streamPos);

        cmd.Dispose();
        Assert.True(cmd._inPool);
        Assert.Equal(0, cmd._streamPos);
    }

    // Headless, Submit never reaches a device: it marks the buffer as released and hands it straight back
    // to the pool, which wipes the recorded commands. So the stream has to be inspected *before* submitting.
    [Fact]
    public void Submit_Headless_RecyclesTheBufferSynchronously()
    {
        CommandBuffer cmd = Rent();
        cmd.SetViewport(5, 6, 7, 8);
        Assert.True(cmd._streamPos > 0);

        Graphics.Submit(cmd);

        Assert.True(Graphics.IsHeadless);
        Assert.True(cmd._ownerReleased);
        Assert.True(cmd._inPool);
        Assert.Equal(0, cmd._streamPos);
        Assert.Empty(cmd._objects);
    }

    // The owner flag is what makes Dispose a no-op after Submit, so the using-block that submits a buffer
    // does not put it into the free list a second time on the way out.
    [Fact]
    public void Dispose_AfterSubmit_DoesNotReturnTheBufferASecondTime()
    {
        CommandBuffer cmd = Rent();
        cmd.SetViewport(5, 6, 7, 8);
        Graphics.Submit(cmd);

        cmd.Dispose();

        Assert.True(cmd._inPool);
        Assert.True(cmd._ownerReleased);
        Assert.Equal(0, cmd._streamPos);
    }

    // ================================================================
    //  Render stats
    // ================================================================

    // The two indexed draw encoders feed the profiler, counting primitives from the topology.
    [Fact]
    public void DrawIndexed_RecordsTheDrawCallAndItsTriangleCount()
    {
        RenderStats.BeginFrame();
        using CommandBuffer cmd = Rent();

        cmd.DrawIndexed(NewVertexArray(), Topology.Triangles, indexCount: 30);

        RenderStats.EndFrame();
        Assert.Equal(1, RenderStats.Last.DrawCalls);
        Assert.Equal(10, RenderStats.Last.Triangles);
        Assert.Equal(30, RenderStats.Last.Vertices);
    }

    // KNOWN ISSUE: DrawArrays is the one draw encoder that does not call RenderStats.RecordDraw, so every
    // non-indexed draw - debug overlays, particles, wireframe, UI strips - is invisible in the stats and
    // DrawCalls under-reports. See docs/PLAN_10_DE_10.md Fase 6. (H-RD-7)
    [Fact]
    public void DrawArrays_IsNotCountedInRenderStats()
    {
        RenderStats.BeginFrame();
        using CommandBuffer cmd = Rent();

        cmd.DrawArrays(NewVertexArray(), Topology.Triangles, first: 0, count: 3);

        RenderStats.EndFrame();
        Assert.Equal(0, RenderStats.Last.DrawCalls);
        Assert.Equal(0, RenderStats.Last.Triangles);
        Assert.Equal(1, new StreamDecoder(cmd).CountOf(CommandOpcode.DrawArrays));
    }

    // ================================================================
    //  High-level expansion
    // ================================================================

    // DrawMesh is encoder sugar: it expands inline into the low-level opcodes, and the default model
    // matrix means "no override", so no transform uniform is recorded at all.
    [Fact]
    public void DrawMesh_WithNoModelMatrix_RecordsNoObjectToWorldUniform()
    {
        using CommandBuffer cmd = Rent();
        var material = new Material();

        cmd.DrawMesh(Mesh.CreateCube(Float3.One), material);

        List<CommandOpcode> opcodes = new StreamDecoder(cmd).Scan();
        Assert.Contains(CommandOpcode.SetShader, opcodes);
        Assert.Contains(CommandOpcode.SetRasterState, opcodes);
        Assert.Contains(CommandOpcode.SetMaterialProperties, opcodes);
        Assert.Contains(CommandOpcode.ClearInstanceProperties, opcodes);
        Assert.Contains(CommandOpcode.DrawIndexed, opcodes);

        // prowl_ObjectToWorld is only recorded when a model matrix is actually supplied.
        Assert.DoesNotContain(CommandOpcode.SetUniformMatrix, opcodes);
    }

    // KNOWN ISSUE: "no override" is decided by comparing the matrix against default, so an explicitly
    // zero matrix cannot be expressed. The draw inherits whatever prowl_ObjectToWorld the previous batch
    // left bound. See docs/PLAN_10_DE_10.md Fase 6. (H-RD-6)
    [Fact]
    public void DrawMesh_WithAnExplicitlyZeroMatrix_AlsoRecordsNoTransform()
    {
        using CommandBuffer cmd = Rent();
        var material = new Material();
        Float4x4 zero = default;

        cmd.DrawMesh(Mesh.CreateCube(Float3.One), material, model: in zero);

        List<CommandOpcode> opcodes = new StreamDecoder(cmd).Scan();
        Assert.Contains(CommandOpcode.DrawIndexed, opcodes);
        Assert.DoesNotContain(CommandOpcode.SetUniformMatrix, opcodes);
    }

    // KNOWN ISSUE: the clear that precedes a blit always has the stencil bit set, whether or not the
    // caller asked for it - there is no clearStencil parameter to ask with. So clearing the colour of a
    // render target also wipes its stencil. See docs/PLAN_10_DE_10.md Fase 6. (H-RD-1)
    [Fact]
    public void Blit_ClearingColourOnly_AlsoRecordsTheStencilFlag()
    {
        using CommandBuffer cmd = Rent();
        using RenderTexture destination = new(64, 32, hasDepthAttachment: true, [TextureImageFormat.Color4b]);

        cmd.Blit(null, destination, clearColor: true);

        var decoder = new StreamDecoder(cmd);
        var opcodes = new List<CommandOpcode>();
        ClearFlags flags = ClearFlags.Stencil;
        bool sawClear = false;

        while (!decoder.AtEnd)
        {
            CommandOpcode current = decoder.ReadOpcode();
            opcodes.Add(current);
            if (current != CommandOpcode.ClearRenderTarget)
            {
                decoder.Skip(StreamDecoder.PayloadSize(current));
                continue;
            }

            sawClear = true;
            flags = (ClearFlags)decoder.ReadU8();
            decoder.Skip(StreamDecoder.PayloadSize(current) - 1);
        }

        Assert.True(sawClear, "the blit recorded no clear at all");
        Assert.Equal(ClearFlags.Color | ClearFlags.Stencil, flags);
        Assert.Contains(CommandOpcode.DrawIndexed, opcodes);
    }

    /// <summary>Reads CommandBuffer._rentedSnapshots, which is private because the pool is an implementation detail.</summary>
    private static int RentedSnapshotCount(CommandBuffer cmd) =>
        ((System.Collections.IEnumerable)typeof(CommandBuffer)
            .GetField("_rentedSnapshots", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(cmd)!).Cast<PropertyState>().Count();
}