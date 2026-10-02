// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

/// <summary>
/// Fase 3.1b - tests de render de UI para <see cref="UIRenderItem"/>: estado por defecto,
/// <c>Initialize</c>, la cache de propiedades + uniforms de clip de <c>GetRenderingData</c>, y el
/// culling / refresh de matrices del item.
/// </summary>
/// <remarks>
/// GENERAL OBSERVATION (sin ID nuevo): <c>Initialize</c> sin clip limpia <c>HasClip</c> y
/// <c>ClipSource</c>, pero deja los valores <c>ClipRect/ClipRadius/ClipSoftness/ClipToLocal</c> del
/// anterior. Es inocuo porque <c>GetRenderingData</c> los gatea con <c>HasClip</c>, pero cualquier
/// lector directo de esos campos veria datos viejos. Se documenta en el ultimo test.
/// </remarks>
public class UIRenderItemTests
{
    /// <summary>UIBehaviour minimo: cuenta poblados de propiedades y aporta un material propio.</summary>
    private sealed class QuadProbe : UIBehaviour
    {
        public int PopulateCount;
        private Material? _material;

        public override void GenerateMesh(UIMeshBuilder builder, in UIContext context) { }

        public override void PopulateProperties(PropertyState props, in UIContext context)
        {
            PopulateCount++;
            props.SetInt("_TestMark", 1);
        }

        public override Material GetMaterial() => _material ??= new Material();
    }

    private static Mesh BuildQuadMesh()
    {
        var builder = new UIMeshBuilder();
        builder.AddQuad(new Rect(0f, 0f, 10f, 10f), Color.Red, Float2.Zero, Float2.One);
        Mesh mesh = new Mesh();
        builder.Bake(mesh);
        return mesh;
    }

    // ============================================================
    // Estado por defecto (tabla de Fase A #21)
    // ============================================================

    [Fact]
    public void DefaultCtor_HasEmptyFields_AndReferenceEquality()
    {
        var a = new UIRenderItem();
        var b = new UIRenderItem();

        Assert.Null(a.Owner);
        Assert.Null(a.Canvas);
        Assert.Null(a.Mesh);
        Assert.Null(a.Material);
        Assert.False(a.HasClip);
        Assert.Null(a.ClipSource);
        Assert.NotNull(a.Props);
        Assert.Equal(0L, a.SortKey);
        Assert.Equal(UISurface.World, a.Surface); // default(UISurface) es World (0)
        Assert.Equal(UIDirtyFlags.None, a.PropertyCacheState);

        Assert.Same(a, a);
        Assert.NotSame(a, b);
        Assert.False(a.Equals(b));
    }

    // ============================================================
    // Initialize (tabla #22)
    // ============================================================

    [Fact]
    public void Initialize_PopulatesFields_AndForcesFirstPopulate()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameCanvas canvas = canvasGo.GetComponent<GameCanvas>()!;
        GameObject elem = ui.AddChild(canvasGo, "Elem");
        QuadProbe probe = elem.AddComponent<QuadProbe>();

        Mesh mesh = BuildQuadMesh();
        Material material = new Material();
        Float4x4 model = Float4x4.CreateTranslation(new Float3(7f, 8f, 9f));

        var item = new UIRenderItem();
        item.Initialize(probe, canvas, mesh, material, model, 42L, UISurface.World);

        Assert.Same(probe, item.Owner);
        Assert.Same(canvas, item.Canvas);
        Assert.Same(mesh, item.Mesh);
        Assert.Same(material, item.Material);
        Assert.True(model == item.Model);
        Assert.Equal(42L, item.SortKey);
        Assert.Equal(UISurface.World, item.Surface);
        Assert.True(probe.Transform.LocalToWorldMatrix == item.LastOwnerWorld);
        Assert.Equal(UIDirtyFlags.All, item.PropertyCacheState); // fuerza el primer populate
        Assert.False(item.HasClip);
        Assert.Null(item.ClipSource);
    }

    // ============================================================
    // GetRenderingData + APIs de IRenderable (tabla #23)
    // ============================================================

    [Fact]
    public void RenderingData_CachesProperties_AndRefreshesClipUniformsEveryFrame()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameCanvas canvas = canvasGo.GetComponent<GameCanvas>()!;
        GameObject elem = ui.AddChild(canvasGo, "Elem");
        QuadProbe probe = elem.AddComponent<QuadProbe>();
        GameObject maskGo = ui.AddChild(canvasGo, "Mask");
        QuadProbe mask = maskGo.AddComponent<QuadProbe>();

        Mesh mesh = BuildQuadMesh();
        Material material = new Material();
        Float4x4 model = Float4x4.CreateTranslation(new Float3(1f, 2f, 3f));
        var item = new UIRenderItem();
        item.Initialize(probe, canvas, mesh, material, model, 1L, UISurface.Overlay);

        // ---- primera llamada: puebla Props y cachea mesh/model ----
        item.GetRenderingData(default, out PropertyState p1, out Mesh m1, out _, out _);
        Assert.Equal(1, probe.PopulateCount);
        Assert.Equal(probe.InstanceID, p1.GetInt("_ObjectID"));
        Assert.Equal(1, p1.GetInt("_TestMark"));
        Assert.Equal(0f, p1.GetFloat("_ClipEnable")); // sin clip
        Assert.Same(mesh, m1);

        // ---- estado limpio: no repobula ----
        item.GetRenderingData(default, out PropertyState p2, out _, out _, out _);
        Assert.Equal(1, probe.PopulateCount);
        Assert.Equal(1, p2.GetInt("_TestMark"));

        // ---- Material sucio: repobula una vez mas ----
        item.PropertyCacheState = UIDirtyFlags.Material;
        item.GetRenderingData(default, out PropertyState p3, out _, out _, out _);
        Assert.Equal(2, probe.PopulateCount);
        Assert.Equal(UIDirtyFlags.None, item.PropertyCacheState);
        Assert.Equal(1, p3.GetInt("_TestMark"));

        // ---- APIs de IRenderable del batcher ----
        Assert.Same(material, item.GetMaterial());
        Assert.Equal(probe.GameObject.LayerIndex, item.GetLayer());
        Assert.Equal(-1, item.GetSubMeshIndex());
        item.Model = Float4x4.CreateTranslation(new Float3(3f, 4f, 5f));
        Assert.Equal(new Float3(3f, 4f, 5f), item.GetPosition());

        // ---- con clip: los uniforms se reescriben en cada llamada ----
        UIClip clip = new UIClip(mask, new Float4(-40f, -30f, 40f, 30f), 12f, 1.5f);
        item.Initialize(probe, canvas, mesh, material, model, 1L, UISurface.Overlay, clip);
        Assert.True(item.HasClip);
        Assert.Same(mask, item.ClipSource);
        Assert.Equal(new Float4(-40f, -30f, 40f, 30f), item.ClipRect);
        Assert.Equal(12f, item.ClipRadius);
        Assert.Equal(1.5f, item.ClipSoftness);
        Float4x4 expectedClipToLocal = canvas.BuildItemModel(mask).Invert();
        Assert.True(expectedClipToLocal == item.ClipToLocal);

        item.GetRenderingData(default, out PropertyState c1, out _, out _, out _);
        Assert.Equal(3, probe.PopulateCount); // Initialize volvio a marcar All
        Assert.Equal(1f, c1.GetFloat("_ClipEnable"));
        Assert.Equal(new Float4(-40f, -30f, 40f, 30f), c1.GetVector4("_ClipRect"));
        Assert.Equal(12f, c1.GetFloat("_ClipRadius"));
        Assert.Equal(1.5f, c1.GetFloat("_ClipSoftness"));
        Assert.True(expectedClipToLocal == c1.GetMatrix("_ClipToLocal"));

        // ClipRadius mutado fuera de Initialize: la siguiente llamada lo refleja (vive fuera
        // del bloque cacheado de propiedades).
        item.ClipRadius = 99f;
        item.GetRenderingData(default, out PropertyState c2, out _, out _, out _);
        Assert.Equal(99f, c2.GetFloat("_ClipRadius"));
        Assert.Equal(1f, c2.GetFloat("_ClipEnable"));
        Assert.Equal(3, probe.PopulateCount); // el clip no vuelve a poblar

        // GENERAL OBSERVATION: re-inicializar sin clip deja los valores Clip* antiguos; solo
        // HasClip/ClipSource se limpian y GetRenderingData los gatea por HasClip.
        item.Initialize(probe, canvas, mesh, material, model, 1L, UISurface.Overlay, null);
        Assert.False(item.HasClip);
        Assert.Null(item.ClipSource);
        Assert.Equal(99f, item.ClipRadius);
        Assert.Equal(new Float4(-40f, -30f, 40f, 30f), item.ClipRect);
        item.GetRenderingData(default, out PropertyState n1, out _, out _, out _);
        Assert.Equal(0f, n1.GetFloat("_ClipEnable"));
    }

    // ============================================================
    // Culling + refresh de matrices (tabla #24)
    // ============================================================

    [Fact]
    public void GetCullingData_IsNullSafe_AndRefreshModelIfDirtyIsGated()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameCanvas canvas = canvasGo.GetComponent<GameCanvas>()!;
        GameObject elem = ui.AddChild(canvasGo, "Elem");
        QuadProbe probe = elem.AddComponent<QuadProbe>();
        GameObject maskGo = ui.AddChild(canvasGo, "Mask");
        QuadProbe mask = maskGo.AddComponent<QuadProbe>();

        Material material = new Material();
        Mesh mesh = BuildQuadMesh();
        var item = new UIRenderItem();
        item.Initialize(probe, canvas, mesh, material, Float4x4.Identity, 1L, UISurface.Overlay);

        // Mesh invalida -> ok=false sin NRE (EngineObject.IsValid es null-safe)
        item.Mesh = null!;
        item.GetCullingData(out bool okNull, out AABB boundsNull);
        Assert.False(okNull);

        // Bounds transformados por Model
        item.Mesh = mesh;
        item.Model = Float4x4.CreateTranslation(new Float3(5f, 0f, 0f));
        item.GetCullingData(out bool ok, out AABB bounds);
        Assert.True(ok);
        AABB expected = mesh.bounds.TransformBy(item.Model);
        Assert.Equal(expected.Min, bounds.Min);
        Assert.Equal(expected.Max, bounds.Max);

        // Owner quieto: RefreshModelIfDirty no toca Model
        Float4x4 sentinel = Float4x4.CreateTranslation(new Float3(99f, 99f, 99f));
        item.Model = sentinel;
        item.RefreshModelIfDirty();
        Assert.True(sentinel == item.Model);

        // Con clip y owner en movimiento: repone Model, LastOwnerWorld y ClipToLocal
        UIClip clip = new UIClip(mask, new Float4(-40f, -30f, 40f, 30f), 12f, 1.5f);
        item.Initialize(probe, canvas, mesh, material, Float4x4.Identity, 1L, UISurface.Overlay, clip);
        mask.Transform.Position = new Float3(0f, 0f, 3f);
        probe.Transform.Position = new Float3(5f, 2f, 0f);
        item.RefreshModelIfDirty();
        Assert.True(canvas.BuildItemModel(probe) == item.Model);
        Assert.True(probe.Transform.LocalToWorldMatrix == item.LastOwnerWorld);
        Assert.True(canvas.BuildItemModel(mask).Invert() == item.ClipToLocal);
    }
}
