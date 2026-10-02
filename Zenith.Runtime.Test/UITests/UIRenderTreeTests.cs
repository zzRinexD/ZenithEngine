// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

/// <summary>
/// Fase 3.1b - tests de render de UI para <see cref="UIRenderTree"/>: pool de items, orden por
/// <c>SortKey</c>, refresh de matrices y el entry point estatico <c>CollectFor</c>.
/// </summary>
public class UIRenderTreeTests
{
    /// <summary>
    /// UIBehaviour minimo: emite un quad cuando el test lo pide (para que el canvas genere un
    /// UIRenderItem real) y cuenta poblados de propiedades. El material se crea aqui para no
    /// depender del shader DefaultUI en headless.
    /// </summary>
    private sealed class QuadProbe : UIBehaviour
    {
        public bool EmitGeometry;
        public int PopulateCount;
        private Material? _material;

        public override void GenerateMesh(UIMeshBuilder builder, in UIContext context)
        {
            if (!EmitGeometry) return;
            builder.AddQuad(new Rect(0f, 0f, 10f, 10f), Color.Red, Float2.Zero, Float2.One);
        }

        public override void PopulateProperties(PropertyState props, in UIContext context)
        {
            PopulateCount++;
            props.SetInt("_TestMark", 1);
        }

        public override Material GetMaterial() => _material ??= new Material();
    }

    // ============================================================
    // Pool de items (tabla de Fase A #17)
    // ============================================================

    [Fact]
    public void RentItem_PoolsFromFreeList_AndClearMovesActiveToFreeList()
    {
        var tree = new UIRenderTree();

        UIRenderItem a = tree.RentItem();
        UIRenderItem b = tree.RentItem();
        Assert.NotSame(a, b);
        Assert.Equal(0, tree.Count);
        Assert.Empty(tree.Items);

        a.Props.SetInt("_Mark", 7);
        a.HasClip = true;
        a.SortKey = 20L;

        tree.Add(a);
        tree.Add(b);
        Assert.Equal(2, tree.Count);
        Assert.Same(a, tree.Items[0]);
        Assert.Same(b, tree.Items[1]);

        tree.Clear();
        Assert.Equal(0, tree.Count);
        Assert.Empty(tree.Items);

        // La pila es LIFO: el ultimo soltado sale primero, con las referencias limpias
        // (asi un item deshabilitado no retiene meshes/materials mientras esta en el free list).
        UIRenderItem first = tree.RentItem();
        UIRenderItem second = tree.RentItem();
        Assert.Same(b, first);
        Assert.Same(a, second);

        Assert.Null(a.Owner);
        Assert.Null(a.Canvas);
        Assert.Null(a.Mesh);
        Assert.Null(a.Material);
        Assert.False(a.HasClip);
        Assert.Null(a.ClipSource);
        Assert.False(a.Props.HasInt("_Mark"));
    }

    // ============================================================
    // Orden + refresh de matrices (tabla #18)
    // ============================================================

    [Fact]
    public void SortHierarchical_OrdersByKey_AndRefreshTransformsPatchesDirtyModels()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameCanvas canvas = canvasGo.GetComponent<GameCanvas>()!;
        GameObject elem = ui.AddChild(canvasGo, "Elem");
        QuadProbe probe = elem.AddComponent<QuadProbe>();

        var builder = new UIMeshBuilder();
        builder.AddQuad(new Rect(0f, 0f, 10f, 10f), Color.Red, Float2.Zero, Float2.One);
        Mesh mesh = new Mesh();
        builder.Bake(mesh);
        Material material = new Material();

        var tree = new UIRenderTree();
        UIRenderItem last = tree.RentItem();
        last.Initialize(probe, canvas, mesh, material, canvas.BuildItemModel(probe), 20L, UISurface.Overlay);
        UIRenderItem first = tree.RentItem();
        first.Initialize(probe, canvas, mesh, material, canvas.BuildItemModel(probe), 10L, UISurface.Overlay);
        UIRenderItem middle = tree.RentItem();
        middle.Initialize(probe, canvas, mesh, material, canvas.BuildItemModel(probe), 15L, UISurface.Overlay);
        tree.Add(last);
        tree.Add(first);
        tree.Add(middle);

        tree.SortHierarchical();
        Assert.Equal(new long[] { 10L, 15L, 20L }, tree.Items.Select(i => i.SortKey).ToArray());

        // Sin movimientos: RefreshTransforms no debe tocar Model.
        Float4x4 sentinel = Float4x4.CreateTranslation(new Float3(99f, 99f, 99f));
        first.Model = sentinel;
        tree.RefreshTransforms();
        Assert.True(sentinel == first.Model);

        // Owner movido: el proximo pase repone Model y LastOwnerWorld.
        probe.Transform.Position = new Float3(5f, 2f, 0f);
        tree.RefreshTransforms();
        Float4x4 expected = canvas.BuildItemModel(probe);
        Assert.True(expected == first.Model);
        Assert.True(expected == middle.Model);
        Assert.True(expected == last.Model);
        Assert.True(probe.Transform.LocalToWorldMatrix == first.LastOwnerWorld);
        Assert.True(probe.Transform.LocalToWorldMatrix == last.LastOwnerWorld);
    }

    // ============================================================
    // Entry point del pipeline (tabla #19)
    // ============================================================

    [Fact]
    public void CollectFor_AppendsMatchingSurfaceOnly_AndSkipsDisabledCanvases()
    {
        // KNOWN ISSUE: con GameCanvas.ScreenSizeOverride == null, RebuildIfDirty ->
        // ResolveScreenSize/ComputeRootRect dereferencian Window.InternalWindow.FramebufferSize, que
        // es null en headless y revienta con NRE. Este test fija el override (harness) para no
        // reproducirlo; cualquier caller que olvide fijarlo cae aqui. See docs/PLAN_10_DE_10.md
        // Fase 6. (H-UI-7)
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();

        GameObject overlayGo = ui.CreateCanvas(scene, "Overlay");
        QuadProbe overlayProbe = ui.AddChild(overlayGo, "Elem").AddComponent<QuadProbe>();
        overlayProbe.EmitGeometry = true;

        GameObject worldGo = ui.CreateCanvas(scene, "World");
        worldGo.GetComponent<GameCanvas>()!.RenderMode = RenderMode.WorldSpace;
        QuadProbe worldProbe = ui.AddChild(worldGo, "Elem").AddComponent<QuadProbe>();
        worldProbe.EmitGeometry = true;

        GameObject disabledGo = ui.CreateCanvas(scene, "Disabled");
        QuadProbe disabledProbe = ui.AddChild(disabledGo, "Elem").AddComponent<QuadProbe>();
        disabledProbe.EmitGeometry = true;
        disabledGo.Enabled = false;

        var overlay = new List<IRenderable>();
        UIRenderTree.CollectFor(scene, UISurface.Overlay, overlay);
        Assert.Single(overlay); // el canvas deshabilitado (tambien overlay) queda fuera
        UIRenderItem overlayItem = Assert.IsType<UIRenderItem>(overlay[0]);
        Assert.Same(overlayProbe, overlayItem.Owner);
        Assert.Equal(UISurface.Overlay, overlayItem.Surface);
        Assert.True(overlayProbe.Transform.LocalToWorldMatrix == overlayItem.LastOwnerWorld);

        var world = new List<IRenderable>();
        UIRenderTree.CollectFor(scene, UISurface.World, world);
        Assert.Single(world);
        UIRenderItem worldItem = Assert.IsType<UIRenderItem>(world[0]);
        Assert.Same(worldProbe, worldItem.Owner);
        Assert.Equal(UISurface.World, worldItem.Surface); // refleja RenderMode en el rebuild

        // El canvas deshabilitado no aporta items a ninguna superficie.
        Assert.DoesNotContain(overlay, r => ReferenceEquals(((UIRenderItem)r).Owner, disabledProbe));
        Assert.DoesNotContain(world, r => ReferenceEquals(((UIRenderItem)r).Owner, disabledProbe));

        // Escena nula: retorno temprano sin throw.
        var none = new List<IRenderable>();
        UIRenderTree.CollectFor(null!, UISurface.Overlay, none);
        Assert.Empty(none);
    }

    // ============================================================
    // Mapeo de superficies (tabla #20)
    // ============================================================

    [Fact]
    public void ToSurface_MapsRenderModeToPipelineSurface()
    {
        Assert.Equal(UISurface.World, UIRenderTree.ToSurface(RenderMode.WorldSpace));
        Assert.Equal(UISurface.Overlay, UIRenderTree.ToSurface(RenderMode.ScreenSpaceOverlay));
    }
}
