// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Runtime.Test.RenderTestHelpers;

/// <summary>
/// Reads back a <see cref="CommandBuffer"/>'s recorded byte stream on the CPU.
/// <para/>
/// <b>Mirror of the decoder in CommandExecutor.cs:967-1003; keep in sync.</b> The executor is the only
/// other thing that knows this wire format, and it is unreachable headless (it has no IsHeadless guard
/// and dereferences <c>Graphics.GL</c> 34 times), so this is how a test inspects what was encoded.
/// <para/>
/// Nothing here touches the graphics device: a command buffer is a byte-stream recorder, and every field
/// it writes is either a fixed-size unmanaged value, a <c>ushort</c> index into
/// <c>CommandBuffer._objects</c>, or a <see cref="TransientStore.Ref"/> into the buffer's blob arena.
/// All three are reachable because <c>Zenith.Runtime.csproj</c> grants InternalsVisibleTo to this
/// assembly.
/// </summary>
/// <remarks>
/// Every payload has a <b>fixed</b> size for a given opcode — variable-length data (matrix arrays, buffer
/// uploads, pixel rectangles) is parked into the transient store and referenced by a fixed 12-byte
/// <see cref="TransientStore.Ref"/> — so <see cref="PayloadSize"/> can skip any command without decoding
/// it. That is what makes <see cref="Scan"/> possible.
/// </remarks>
internal ref struct StreamDecoder
{
    private readonly CommandBuffer _cmd;
    private readonly int _end;
    private int _pos;

    public StreamDecoder(CommandBuffer cmd)
    {
        _cmd = cmd;
        _end = cmd._streamPos;
        _pos = 0;
    }

    /// <summary>True once every recorded command has been consumed.</summary>
    public readonly bool AtEnd => _pos >= _end;

    /// <summary>Bytes consumed so far. Equals <c>CommandBuffer._streamPos</c> when fully decoded.</summary>
    public readonly int Position => _pos;

    // ─────────────────────── Primitives ───────────────────────

    public CommandOpcode ReadOpcode()
    {
        CommandOpcode op = MemoryMarshal.Read<CommandOpcode>(_cmd._stream.AsSpan(_pos, sizeof(CommandOpcode)));
        _pos += sizeof(CommandOpcode);
        return op;
    }

    public byte ReadU8()
    {
        byte value = _cmd._stream[_pos];
        _pos += 1;
        return value;
    }

    public ushort ReadU16()
    {
        ushort value = MemoryMarshal.Read<ushort>(_cmd._stream.AsSpan(_pos, sizeof(ushort)));
        _pos += sizeof(ushort);
        return value;
    }

    public int ReadI32()
    {
        int value = MemoryMarshal.Read<int>(_cmd._stream.AsSpan(_pos, sizeof(int)));
        _pos += sizeof(int);
        return value;
    }

    public uint ReadU32()
    {
        uint value = MemoryMarshal.Read<uint>(_cmd._stream.AsSpan(_pos, sizeof(uint)));
        _pos += sizeof(uint);
        return value;
    }

    public float ReadF32()
    {
        float value = MemoryMarshal.Read<float>(_cmd._stream.AsSpan(_pos, sizeof(float)));
        _pos += sizeof(float);
        return value;
    }

    /// <summary>Reads a raw <c>ushort</c> object-table index without resolving it.</summary>
    public ushort ReadObjectIndex() => ReadU16();

    /// <summary>Reads an object-table index and resolves it against <c>CommandBuffer._objects</c>.</summary>
    public object? ReadObject() => _cmd._objects[ReadU16()];

    /// <summary>Reads an interned-name index and returns the string it points at.</summary>
    public string ReadInternedName() => (string)_cmd._objects[ReadU16()]!;

    /// <summary>Reads a fixed-size unmanaged payload (<c>RasterizerState</c>, <c>Float4x4</c>, ...).</summary>
    public T ReadStruct<T>() where T : unmanaged
    {
        T value = MemoryMarshal.Read<T>(_cmd._stream.AsSpan(_pos, Unsafe.SizeOf<T>()));
        _pos += Unsafe.SizeOf<T>();
        return value;
    }

    /// <summary>Reads a <see cref="TransientStore.Ref"/> pointing into the buffer's blob arena.</summary>
    public TransientStore.Ref ReadBlobRef() => ReadStruct<TransientStore.Ref>();

    /// <summary>Reads a blob reference and copies its bytes out of the store.</summary>
    public byte[] ReadBlobBytes()
    {
        TransientStore.Ref reference = ReadBlobRef();
        return _cmd._store.Read(reference).ToArray();
    }

    public void Skip(int bytes) => _pos += bytes;

    /// <summary>Reads the next command's header and skips its payload in one step.</summary>
    public CommandOpcode ReadCommand()
    {
        CommandOpcode op = ReadOpcode();
        Skip(PayloadSize(op));
        return op;
    }

    // ─────────────────────── Whole-stream scanning ───────────────────────

    /// <summary>
    /// The opcode of every command in the buffer, in recording order, with each payload skipped. The
    /// usual way to assert on what an encoder expanded into.
    /// </summary>
    public List<CommandOpcode> Scan()
    {
        List<CommandOpcode> opcodes = [];
        while (!AtEnd)
            opcodes.Add(ReadCommand());

        return opcodes;
    }

    /// <summary>How many times <paramref name="op"/> was recorded.</summary>
    public int CountOf(CommandOpcode op)
    {
        int count = 0;
        foreach (CommandOpcode recorded in Scan())
            if (recorded == op)
                count++;

        return count;
    }

    /// <summary>The first command equal to <paramref name="op"/>, or null when it was never recorded.</summary>
    public CommandOpcode? FirstOrNull(CommandOpcode op)
    {
        foreach (CommandOpcode recorded in Scan())
            if (recorded == op)
                return recorded;

        return null;
    }

    /// <summary>
    /// Size in bytes of an opcode's payload, excluding its 2-byte header.
    /// </summary>
    /// <remarks>
    /// The opcodes that appear in a <c>DrawMesh</c>/<c>Blit</c> expansion, and every global/uniform setter,
    /// are all listed. Anything else throws rather than guessing, because a wrong size would silently
    /// desynchronise the scan and produce a test that passes for the wrong reason.
    /// </remarks>
    public static int PayloadSize(CommandOpcode op) => op switch
    {
        // Render target / viewport / clear
        CommandOpcode.SetRenderTarget => 2,
        CommandOpcode.SetRenderTargets => 4,
        CommandOpcode.SetViewport => 16,                                  // int x, int y, uint w, uint h
        CommandOpcode.SetScissor => 16,
        CommandOpcode.DisableScissor => 0,
        CommandOpcode.ClearRenderTarget => 25,                           // byte flags, 4 floats, float depth, int stencil
        CommandOpcode.BlitFramebuffer => 34,                             // 8 ints, byte mask, byte filter

        // Pipeline state
        CommandOpcode.SetRasterState => Unsafe.SizeOf<RasterizerState>(),
        CommandOpcode.SetShader => 2,

        // Property binding
        CommandOpcode.SetProperties => 2,
        CommandOpcode.SetMaterialProperties => 4,                        // property snapshot + shader
        CommandOpcode.ClearProperties => 0,
        CommandOpcode.SetInstanceProperties => 2,
        CommandOpcode.ClearInstanceProperties => 0,

        // Globals
        CommandOpcode.SetGlobalTexture => 4,                              // name, texture
        CommandOpcode.ClearGlobalTexture => 2,
        CommandOpcode.SetGlobalInt => 6,
        CommandOpcode.SetGlobalFloat => 6,
        CommandOpcode.SetGlobalVec2 => 10,
        CommandOpcode.SetGlobalVec3 => 14,
        CommandOpcode.SetGlobalVec4 => 18,
        CommandOpcode.SetGlobalColor => 18,                              // encoded as 4 floats
        CommandOpcode.SetGlobalMatrix => 2 + Unsafe.SizeOf<Float4x4>(),
        CommandOpcode.SetGlobalMatrices => 4,                            // name, array reference (by design)
        CommandOpcode.SetGlobalBuffer => 8,
        CommandOpcode.SetGlobalTexture3D => 4,
        CommandOpcode.SetGlobalTextureCube => 4,
        CommandOpcode.ClearAllGlobals => 0,

        // Per-uniform sugar
        CommandOpcode.SetUniformFloat => 6,
        CommandOpcode.SetUniformInt => 6,
        CommandOpcode.SetUniformVec2 => 10,
        CommandOpcode.SetUniformVec3 => 14,
        CommandOpcode.SetUniformVec4 => 18,
        CommandOpcode.SetUniformMatrix => 2 + Unsafe.SizeOf<Float4x4>(),
        CommandOpcode.SetUniformMatrixArray => 2 + 4 + Unsafe.SizeOf<TransientStore.Ref>(),
        CommandOpcode.SetUniformTexture => 4,
        CommandOpcode.SetUniformBuffer => 8,

        // Uploads
        CommandOpcode.UpdateBuffer => 2 + 4 + Unsafe.SizeOf<TransientStore.Ref>(),
        CommandOpcode.UpdateTexture => 34,                               // texture, x, y, w, h, mip, ref
        CommandOpcode.GenerateMipmap => 2,

        // Draws
        CommandOpcode.DrawIndexed => 16,                                 // vao, byte topology, uint count, uint start, int base, byte index32
        CommandOpcode.DrawIndexedInstanced => 20,                        // the above plus uint instanceCount
        CommandOpcode.DrawArrays => 11,                                   // vao, byte topology, int first, uint count

        // Debug markers
        CommandOpcode.BeginSample => 2,
        CommandOpcode.EndSample => 0,

        _ => throw new NotSupportedException(
            $"StreamDecoder.PayloadSize has no documented size for {op}. Add it, mirroring the writer in " +
            "CommandBuffer.cs and the reader in CommandExecutor.cs, rather than guessing: a wrong size " +
            "desynchronises every later command in the scan.")
    };

    // ─────────────────────── Opcode reference ───────────────────────
    //
    // All 66 opcodes, in declaration order (CommandOpcode.cs), with what each writes after its 2-byte
    // header. Kept here so a test author does not have to open the encoder, and so a mismatch between
    // this table and the code is obvious in review. `obj` = ushort index into _objects,
    // `name` = interned name (also an obj index), `ref` = TransientStore.Ref (12 bytes).
    //
    //  #  Opcode                     Payload
    //  1  SetRenderTarget            obj
    //  2  SetRenderTargets           obj draw, obj read
    //  3  SetViewport                int x, int y, uint w, uint h
    //  4  SetScissor                 int x, int y, uint w, uint h
    //  5  DisableScissor             (none)
    //  6  ClearRenderTarget          byte flags, f32 r, f32 g, f32 b, f32 a, f32 depth, int stencil
    //  7  BlitFramebuffer            8x int (src rect then dst rect), byte mask, byte filter
    //  8  SetRasterState             RasterizerState (whole struct)
    //  9  SetShader                  obj program
    // 10  SetProperties              obj property snapshot
    // 11  SetMaterialProperties      obj property snapshot, obj shader
    // 12  ClearProperties            (none)
    // 13  SetInstanceProperties      obj property snapshot
    // 14  ClearInstanceProperties    (none)
    // 15  SetGlobalTexture           name, obj texture
    // 16  ClearGlobalTexture         name
    // 17  SetGlobalInt               name, int
    // 18  SetGlobalFloat             name, float
    // 19  SetGlobalVec2              name, Float2
    // 20  SetGlobalVec3              name, Float3
    // 21  SetGlobalVec4              name, Float4
    // 22  SetGlobalColor             name, Float4 (Color is written as 4 floats)
    // 23  SetGlobalMatrix            name, Float4x4
    // 24  SetGlobalMatrices          name, obj Float4x4[]   <-- reference, not a copy (H-RD-4)
    // 25  SetGlobalBuffer            name, obj buffer, uint bindingPoint
    // 26  SetGlobalTexture3D         name, obj texture
    // 27  SetGlobalTextureCube       name, obj cubemap
    // 28  ClearAllGlobals            (none)
    // 29  SetUniformFloat            name, float
    // 30  SetUniformInt              name, int
    // 31  SetUniformVec2             name, Float2 (written as 2 separate floats)
    // 32  SetUniformVec3             name, Float3 (3 floats)
    // 33  SetUniformVec4             name, Float4 (4 floats)
    // 34  SetUniformMatrix           name, Float4x4
    // 35  SetUniformMatrixArray      name, uint count, ref Float4x4[]
    // 36  SetUniformTexture          name, obj texture
    // 37  SetUniformBuffer           name, obj buffer, uint bindingPoint
    // 38  UpdateBuffer               obj buffer, uint dstOffset, ref bytes
    // 39  UpdateTexture              obj texture, int x, int y, uint w, uint h, int mip, ref bytes
    // 40  GenerateMipmap             obj texture
    // 41  DrawIndexed                obj vao, byte topology, uint indexCount, uint startIndex, int baseVertex, byte index32
    // 42  DrawIndexedInstanced       obj vao, byte topology, uint indexCount, uint instanceCount, uint startIndex, int baseVertex, byte index32
    // 43  DrawArrays                 obj vao, byte topology, int first, uint count
    // 44  CreateBuffer               obj buffer, byte dynamic, ref bytes
    // 45  DisposeBuffer              obj buffer
    // 46  CreateTexture              obj texture
    // 47  AllocateTexture2D          obj texture, int mip, uint w, uint h, int border, ref bytes
    // 48  AllocateTexture3D          obj texture, int mip, uint w, uint h, uint d, ref bytes
    // 49  AllocateTextureCubeFace    obj texture, int face, int mip, uint size, ref bytes
    // 50  UpdateTexture3D            obj texture, int mip, int x, int y, int z, uint w, uint h, uint d, ref bytes
    // 51  SetTextureWrap             obj texture, byte axis, byte mode
    // 52  SetTextureFiltersOp        obj texture, byte min, byte mag
    // 53  SetTextureCompareMode      obj texture, byte enabled
    // 54  GetTextureData             obj texture, int mip, obj destination array
    // 55  GetTextureDataPtr          obj texture, int mip, long pointer
    // 56  GetTextureCubeFaceData     obj texture, int face, int mip, obj destination array
    // 57  DisposeTexture             obj texture
    // 58  CreateVertexArrayOp        obj vao
    // 59  DisposeVertexArray         obj vao
    // 60  CreateFramebufferOp        obj framebuffer
    // 61  DisposeFramebuffer         obj framebuffer
    // 62  CompileShader              obj program
    // 63  DisposeShader              obj program
    // 64  BeginSample                obj label
    // 65  EndSample                  (none)
    // 66  Screenshot                 obj texture, uint width, uint height
    //
    // Note there is no `None = 0`: a zero header reaches the executor's `default:` arm and throws
    // "Unknown command opcode", which is what makes a zeroed/corrupt stream detectable at all.
}