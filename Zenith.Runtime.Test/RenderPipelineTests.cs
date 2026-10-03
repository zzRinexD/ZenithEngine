// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Test.RenderTestHelpers;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// The pipeline's per-frame decisions that need no graphics device: what gets culled, in what order the
/// survivors are drawn, which renderables the scene collects, and what the global uniform block ends up
/// holding.
/// <para/>
/// Nothing here reaches the device. <c>RenderPipeline</c> is an abstract class with no abstract members,
/// so a bare subclass is a usable pipeline, and every method under test either takes its geometry as
/// arguments or writes into a command buffer, which is a recorder. Draw assertions decode that stream with
/// <see cref="StreamDecoder"/>.
/// </summary>
public class RenderPipelineTests : RuntimeTestBase
{
    /// <summary>
    /// RenderPipeline declares no abstract members, so this is a complete pipeline. The base class only
    /// reaches for the device while drawing.
    /// </summary>
    private sealed class TestPipeline : RenderPipeline
    {
    }

    /// <summary>
    /// An IRenderable with no scene behind it: every answer is supplied by the test, and the culling
    /// callback counts its invocations so caching can be observed.
    /// </summary>
    private class ProbeRenderable : IRenderable
    {
        public Material Material = new();
        public int Layer;
        public Float3 Position;
        public Mesh Mesh = Mesh.CreateCube(Float3.One);
        public InstanceData[]? Instances;
        public bool Renderable = true;
        public AABB Bounds = new(Float3.Zero - Float3.One, Float3.Zero + Float3.One);
        public int SubMeshIndex = -1;
        public int CullingDataCalls;

        public Material GetMaterial() => Material;
        public int GetLayer() => Layer;
        public Float3 GetPosition() => Position;
        public int GetSubMeshIndex() => SubMeshIndex;

        public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh,
                                     out Float4x4 model, out InstanceData[]? instanceData)
        {
            properties = new PropertyState();
            mesh = Mesh;
            model = Float4x4.Identity;
            instanceData = Instances;
        }

        public void GetCullingData(out bool isRenderable, out AABB bounds)
        {
            CullingDataCalls++;
            isRenderable = Renderable;
            bounds = Bounds;
        }
    }

    /// <summary>Routes to the instanced path by type rather than by instance count.</summary>
    private sealed class ProbeProcedural : ProbeRenderable, IProceduralInstanced
    {
        public int InstanceCount { get; init; } = 2;
    }

    private readonly TestPipeline _pipeline = new();

    /// <summary>
    /// A frustum with a known inside and a known outside, derived from the frustum's own corners rather
    /// than from hardcoded axes, so it does not depend on the engine's handedness.
    /// </summary>
    private static Frustum TestFrustum(out Float3 inside, out Float3 outside)
    {
        Frustum frustum = Frustum.FromMatrix(Float4x4.CreateOrtho(20f, 20f, 0.1f, 100f));
        Float3 corner = frustum.GetCorners()[0];
        inside = corner;
        outside = corner + new Float3(1_000_000f, 0f, 0f);
        return frustum;
    }

    private static AABB BoxAt(Float3 center) => new(center - Float3.One, center + Float3.One);

    // ================================================================
    //  1-4. Culling
    // ================================================================

    // The returned mask is per index into the input list, and true means "culled, skip it": a renderable
    // reports not-renderable, or its world bounds miss the frustum.
    [Fact]
    public void CullRenderables_OutsideTheFrustum_MarksThatIndexCulled()
    {
        Frustum frustum = TestFrustum(out Float3 inside, out Float3 outside);
        List<IRenderable> renderables =
        [
            new ProbeRenderable { Bounds = BoxAt(inside) },
            new ProbeRenderable { Bounds = BoxAt(outside) },
        ];

        bool[] culled = _pipeline.CullRenderables(renderables, frustum, LayerMask.Everything);

        Assert.Equal(new[] { false, true }, culled);
    }

    // A null frustum means "do not frustum-cull" - the shadow cascades and the editor preview pass pass
    // null - so only the layer mask can reject a renderable. FromMask takes *inclusion* bits, so this
    // mask admits layers 0-2 and rejects everything else.
    [Fact]
    public void CullRenderables_WithoutAFrustum_CullsByLayerMaskOnly()
    {
        TestFrustum(out Float3 inside, out Float3 outside);
        List<IRenderable> renderables =
        [
            new ProbeRenderable { Bounds = BoxAt(inside), Layer = 0 },
            new ProbeRenderable { Bounds = BoxAt(outside), Layer = 3 },
        ];

        bool[] culled = _pipeline.CullRenderables(renderables, worldFrustum: null, LayerMask.FromMask(0b111));

        Assert.Equal(new[] { false, true }, culled);
    }

    [Fact]
    public void CullRenderables_ExcludedLayer_CullsOnlyThatLayer()
    {
        TestFrustum(out Float3 inside, out _);
        List<IRenderable> renderables =
        [
            new ProbeRenderable { Bounds = BoxAt(inside), Layer = 0 },
            new ProbeRenderable { Bounds = BoxAt(inside), Layer = 1 },
            new ProbeRenderable { Bounds = BoxAt(inside), Layer = 2 },
        ];

        // Everything except layer 1.
        bool[] culled = _pipeline.CullRenderables(renderables, null, LayerMask.FromMask(0b101));

        Assert.Equal(new[] { false, true, false }, culled);
    }

    // A renderable that reports itself as not renderable is dropped whatever its bounds say: the flag is
    // how an object switches itself off without leaving the collection.
    [Fact]
    public void CullRenderables_NotRenderable_IsCulledEvenWhenTheFrustumAcceptsItsBounds()
    {
        Frustum frustum = TestFrustum(out Float3 inside, out _);
        List<IRenderable> renderables =
        [
            new ProbeRenderable { Bounds = BoxAt(inside), Renderable = false },
        ];

        bool[] culled = _pipeline.CullRenderables(renderables, frustum, LayerMask.Everything);

        Assert.Equal(new[] { true }, culled);
    }

    // ================================================================
    //  5. World bounds cache
    // ================================================================

    // World bounds are cached per frame so the main cull and every shadow cascade cull transform each
    // renderable once instead of once per frustum. The cache is keyed on the list instance and its count,
    // so a second pass over the same list asks nothing.
    [Fact]
    public void EnsureWorldBounds_TwiceOverTheSameList_QueriesCullingDataOnce()
    {
        var first = new ProbeRenderable();
        var second = new ProbeRenderable();
        List<IRenderable> renderables = [first, second];

        _pipeline.EnsureWorldBounds(renderables);
        Assert.Equal(1, first.CullingDataCalls);
        Assert.Equal(1, second.CullingDataCalls);

        _pipeline.EnsureWorldBounds(renderables);
        Assert.Equal(1, first.CullingDataCalls);
        Assert.Equal(1, second.CullingDataCalls);
    }

    // ================================================================
    //  6-7. Sorting
    // ================================================================

    // Sorting is by squared distance from the camera, so no square root is needed. Opaques go front to
    // back for early-Z; transparents go back to front so alpha blends in the right order.
    [Fact]
    public void SortRenderables_OrdersByDistanceSquared_InTheRequestedDirection()
    {
        var near = new ProbeRenderable { Position = new Float3(0f, 0f, 1f) };
        var middle = new ProbeRenderable { Position = new Float3(0f, 0f, 2f) };
        var far = new ProbeRenderable { Position = new Float3(0f, 0f, 3f) };
        List<IRenderable> renderables = [far, near, middle];

        List<IRenderable> frontToBack = _pipeline.SortRenderables(renderables, null, Float3.Zero, RenderPipeline.SortMode.FrontToBack);
        Assert.Equal(new IRenderable[] { near, middle, far }, frontToBack);

        List<IRenderable> backToFront = _pipeline.SortRenderables(renderables, null, Float3.Zero, RenderPipeline.SortMode.BackToFront);
        Assert.Equal(new IRenderable[] { far, middle, near }, backToFront);
    }

    // The culled mask is the same per-index array the cull produced, so a transparent pass can be given
    // the list of survivors and still sort by distance. A null mask sorts everything.
    [Fact]
    public void SortRenderables_DropsCulledIndices_AndReusesItsResultList()
    {
        var keep = new ProbeRenderable { Position = new Float3(0f, 0f, 5f) };
        var culled = new ProbeRenderable { Position = new Float3(0f, 0f, 1f) };
        List<IRenderable> renderables = [culled, keep];

        bool[] mask = [true, false];
        List<IRenderable> first = _pipeline.SortRenderables(renderables, mask, Float3.Zero, RenderPipeline.SortMode.FrontToBack);
        Assert.Equal(new IRenderable[] { keep }, first);

        // The returned list is a reused buffer, not a fresh one: a second sort overwrites it.
        List<IRenderable> second = _pipeline.SortRenderables(renderables, null, Float3.Zero, RenderPipeline.SortMode.FrontToBack);
        Assert.Same(first, second);
        Assert.Equal(2, second.Count);
    }

    // ================================================================
    //  8. Collection
    // ================================================================

    // The static wrapper hands the scene two empty lists and returns whatever the components appended,
    // in order. It is the seam a scene uses to publish its renderables and lights.
    [Fact]
    public void CollectRenderables_ReturnsWhatTheSceneComponentsAppended()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject();
        var probe = go.AddComponent<CollectProbe>();
        scene.Add(go);

        var expected = new ProbeRenderable();
        probe.Renderable = expected;

        (List<IRenderable> renderables, List<IRenderableLight> lights) =
            RenderPipeline.CollectRenderables(scene, null!);

        Assert.Same(expected, Assert.Single(renderables));
        Assert.Empty(lights);
    }

    /// <summary>Publishes one renderable during the collect phase.</summary>
    private sealed class CollectProbe : MonoBehaviour
    {
        public IRenderable Renderable = null!;

        public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights) =>
            renderables.Add(Renderable);
    }

    // ================================================================
    //  9-11. Batching
    // ================================================================

    // Renderables that share a material, a pass and a mesh are merged into one batch and still draw once
    // each. The default shader's first pass is tagged RenderOrder/Opaque, which is the shape the pipeline
    // itself calls with.
    [Fact]
    public void DrawRenderables_TwoRenderablesSharingMaterialAndMesh_DrawEachOfThem()
    {
        var material = new Material();
        var mesh = Mesh.CreateCube(Float3.One);
        List<IRenderable> renderables =
        [
            new ProbeRenderable { Material = material, Mesh = mesh },
            new ProbeRenderable { Material = material, Mesh = mesh },
        ];

        using CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        RenderStats.BeginFrame();
        _pipeline.DrawRenderables(cmd, renderables, "RenderOrder", "Opaque", default, null, updatePreviousMatrices: false);
        RenderStats.EndFrame();

        Assert.Equal(2, new StreamDecoder(cmd).CountOf(CommandOpcode.DrawIndexed));
        Assert.Equal(2, RenderStats.Last.DrawCalls);
    }

    // A tag nothing matches produces no batch and therefore no draw at all: passes are selected by tag,
    // so asking for a pass that does not exist is a no-op rather than a fallback to everything.
    [Fact]
    public void DrawRenderables_TagWithNoMatchingPass_DrawsNothing()
    {
        List<IRenderable> renderables = [new ProbeRenderable()];

        using CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        RenderStats.BeginFrame();
        _pipeline.DrawRenderables(cmd, renderables, "RenderOrder", "NoSuchPass", default, null, updatePreviousMatrices: false);
        RenderStats.EndFrame();

        Assert.Equal(0, new StreamDecoder(cmd).CountOf(CommandOpcode.DrawIndexed));
        Assert.Equal(0, RenderStats.Last.DrawCalls);
    }

    // KNOWN ISSUE: an instanced batch with no sub-mesh draws the whole mesh, and that fallback hardcodes
    // Topology.Triangles instead of using the mesh's own topology - unlike the single-instance path, which
    // reads mesh.MeshTopology. A line or strip mesh drawn through the instanced path is therefore
    // rasterized as triangles. See docs/PLAN_10_DE_10.md Fase 6. (H-RD-40)
    [Fact]
    public void DrawRenderables_InstancedFallback_RecordsTrianglesWhateverTheMeshSays()
    {
        var mesh = Mesh.CreateCube(Float3.One);
        mesh.MeshTopology = Topology.Lines;

        var material = new Material();
        List<IRenderable> renderables = [new ProbeProcedural { Material = material, Mesh = mesh }];

        using CommandBuffer cmd = Graphics.GetCommandBuffer("test");
        _pipeline.DrawRenderables(cmd, renderables, "RenderOrder", "Opaque", default, null, updatePreviousMatrices: false);

        var decoder = new StreamDecoder(cmd);
        Topology recorded = Topology.Points;
        bool sawDraw = false;
        while (!decoder.AtEnd)
        {
            CommandOpcode op = decoder.ReadOpcode();
            if (op == CommandOpcode.DrawIndexedInstanced)
            {
                decoder.ReadObjectIndex();
                recorded = (Topology)decoder.ReadU8();
                sawDraw = true;
                decoder.Skip(StreamDecoder.PayloadSize(op) - 3);
                continue;
            }

            decoder.Skip(StreamDecoder.PayloadSize(op));
        }

        Assert.True(sawDraw, "the procedural renderable recorded no instanced draw");
        Assert.Equal(Topology.Lines, mesh.MeshTopology);
        Assert.Equal(Topology.Triangles, recorded);
    }

    // ================================================================
    //  12. Global uniforms
    // ================================================================

    // KNOWN ISSUE: the delta-time uniform stores both the frame delta and its reciprocal, and the
    // reciprocal is unguarded. Nothing ever calls TimeData.Update in a test run, so SmoothDeltaTime is 0
    // and 1/0 is uploaded - a shader reading it gets an infinity. The same happens on the first frame of
    // the process and on any paused frame. See docs/PLAN_10_DE_10.md Fase 6. (H-RD-43)
    [Fact]
    public void SetupGlobalUniforms_WithNoSmoothDeltaTime_UploadsAnInfiniteReciprocal()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Camera");
        Camera camera = go.AddComponent<Camera>();
        scene.Add(go);

        Assert.True(Time.SmoothDeltaTime == 0f, $"this test is about the zero-delta case, but SmoothDeltaTime was {Time.SmoothDeltaTime}");

        _pipeline.SetupGlobalUniforms(new RenderPipeline.CameraSnapshot(camera));

        Float4 deltaTime = GlobalUniformData().prowl_DeltaTime;
        Assert.Equal(0f, deltaTime.Z);
        Assert.True(float.IsPositiveInfinity(deltaTime.W), $"expected +Infinity, got {deltaTime.W}");
    }

    /// <summary>Reads GlobalUniforms.s_data, which is private because only the setters should touch it.</summary>
    private static GlobalUniformsData GlobalUniformData() =>
        (GlobalUniformsData)typeof(GlobalUniforms)
            .GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
}