// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

/// <summary>
/// Fase 3.1b - tests de render de UI para <see cref="UIGizmos"/>. 100% CPU: el gizmo builder
/// acumula lineas en buffers managed y <c>Debug.GetGizmoDrawData()</c> los vuelca a un
/// <see cref="Mesh"/> de tipo <c>Lines</c> sin subir nada a GPU.
/// </summary>
public class UIGizmosTests
{
    /// <summary>UIBehaviour sin geometria: solo aporta el RectTransform que los gizmos leen.</summary>
    private sealed class QuadProbe : UIBehaviour
    {
        public override void GenerateMesh(UIMeshBuilder builder, in UIContext context) { }
    }

    private static Mesh? TryGetWire() => Debug.GetGizmoDrawData().wire;

    private static Mesh GetWire()
        => Debug.GetGizmoDrawData().wire ?? throw new InvalidOperationException("Se esperaba geometria wire de gizmo");

    private static int LineCount(Mesh wire) => wire.VertexCount / 2; // AddLine emite 2 vertices + 2 indices

    // Los gizmos acumulan los colores en un List<Color32> (GizmoBuilder.MeshData), asi que el
    // round-trip a Mesh.Colors vuelve a Color pasando por byte: 0.95f -> 242 -> 0.9490196f.
    private static Color WireColor(Color c) => (Color)(Color32)c;

    // ============================================================
    // DrawCanvasRect (tabla de Fase A #25)
    // ============================================================

    [Fact]
    public void DrawCanvasRect_EmitsRootRectOutline_ForBothRenderModes()
    {
        // KNOWN ISSUE: con GameCanvas.ScreenSizeOverride == null, DrawCanvasRect ->
        // canvas.RebuildIfDirty -> ResolveScreenSize/ComputeRootRect dereferencian
        // Window.InternalWindow.FramebufferSize (null en headless) y revienta con NRE antes de
        // dibujar. El harness fija el override para no reproducirlo. See docs/PLAN_10_DE_10.md
        // Fase 6. (H-UI-7)
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameCanvas canvas = canvasGo.GetComponent<GameCanvas>()!;

        bool prevEditorOverride = GameCanvas.EditorWorldSpaceOverride;
        GameCanvas.EditorWorldSpaceOverride = false;
        try
        {
            // 1) Overlay: root rect = pantalla / ScaleFactor (1920x1080 del harness, escala 1)
            Debug.ClearGizmos();
            UIGizmos.DrawCanvasRect(canvas, UIGizmos.SelectedColor);
            Mesh overlayWire = GetWire();
            Assert.Equal(4, LineCount(overlayWire));
            Assert.Equal(8, overlayWire.IndexCount);
            // orden de emision: tl, tr, tr, br, br, bl, bl, tl
            Assert.Equal(new Float3(0f, 1080f, 0f), overlayWire.Vertices[0]);
            Assert.Equal(new Float3(1920f, 1080f, 0f), overlayWire.Vertices[1]);
            Assert.Equal(new Float3(1920f, 0f, 0f), overlayWire.Vertices[3]);
            Assert.Equal(new Float3(0f, 0f, 0f), overlayWire.Vertices[5]);
            Assert.Equal(WireColor(UIGizmos.SelectedColor), overlayWire.Colors[0]);

            // 2) WorldSpace: root rect = ReferenceResolution (1280x720) con transform identidad
            canvas.RenderMode = RenderMode.WorldSpace;
            Debug.ClearGizmos();
            UIGizmos.DrawCanvasRect(canvas, UIGizmos.UnselectedColor);
            Mesh worldWire = GetWire();
            Assert.Equal(4, LineCount(worldWire));
            Assert.Equal(new Float3(0f, 720f, 0f), worldWire.Vertices[0]);
            Assert.Equal(new Float3(1280f, 720f, 0f), worldWire.Vertices[1]);
            Assert.Equal(new Float3(1280f, 0f, 0f), worldWire.Vertices[3]);
            Assert.Equal(new Float3(0f, 0f, 0f), worldWire.Vertices[5]);
            Assert.Equal(WireColor(UIGizmos.UnselectedColor), worldWire.Colors[0]);

            // 3) Surface 0x0 -> root rect vacio -> no emite nada
            canvas.RenderMode = RenderMode.ScreenSpaceOverlay;
            GameCanvas.ScreenSizeOverride = new Float2(0f, 0f);
            Debug.ClearGizmos();
            UIGizmos.DrawCanvasRect(canvas, UIGizmos.SelectedColor);
            Assert.Null(TryGetWire());
        }
        finally
        {
            GameCanvas.EditorWorldSpaceOverride = prevEditorOverride;
            GameCanvas.ScreenSizeOverride = new Float2(1920f, 1080f);
        }
    }

    // ============================================================
    // DrawRect: salida por modo (tabla #26)
    // ============================================================

    [Fact]
    public void DrawRect_SkipsOverlay_EmitsOutlineInWorldSpace()
    {
        // KNOWN ISSUE: en modo WorldSpace, DrawRect llama a canvas.RebuildIfDirty() y por eso
        // hereda el mismo NRE headless de H-UI-7 si ScreenSizeOverride no esta fijado.
        // See docs/PLAN_10_DE_10.md Fase 6. (H-UI-7)
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameCanvas canvas = canvasGo.GetComponent<GameCanvas>()!;
        GameObject elem = ui.AddChild(canvasGo, "Elem");
        QuadProbe probe = elem.AddComponent<QuadProbe>();
        elem.RectTransform!.SizeDelta = new Float2(100f, 50f);

        bool prevEditorOverride = GameCanvas.EditorWorldSpaceOverride;
        GameCanvas.EditorWorldSpaceOverride = false;
        try
        {
            // Overlay: sale antes de tocar el layout -> ninguna geometria
            Debug.ClearGizmos();
            UIGizmos.DrawRect(probe, UIGizmos.SelectedColor, drawPivot: false, drawAnchors: false);
            Assert.Null(TryGetWire());

            // WorldSpace: outline del rect calculado (1280x720 de referencia, anchors/pivot al centro)
            canvas.RenderMode = RenderMode.WorldSpace;
            Debug.ClearGizmos();
            UIGizmos.DrawRect(probe, UIGizmos.SelectedColor, drawPivot: false, drawAnchors: false);
            Mesh wire = GetWire();
            Assert.Equal(4, LineCount(wire));
            Assert.Equal(8, wire.IndexCount);
            // rect = (590,335)-(690,385); orden tl, tr, tr, br, br, bl, bl, tl
            Assert.Equal(new Float3(590f, 385f, 0f), wire.Vertices[0]);
            Assert.Equal(new Float3(690f, 385f, 0f), wire.Vertices[1]);
            Assert.Equal(new Float3(690f, 335f, 0f), wire.Vertices[3]);
            Assert.Equal(new Float3(590f, 335f, 0f), wire.Vertices[5]);
            Assert.Equal(WireColor(UIGizmos.SelectedColor), wire.Colors[0]);
        }
        finally
        {
            GameCanvas.EditorWorldSpaceOverride = prevEditorOverride;
        }
    }

    // ============================================================
    // DrawRect: pivot + asas de ancla (tabla #27)
    // ============================================================

    [Fact]
    public void DrawRect_EmitsPivotAndAnchorHandles_ForNestedElements()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameCanvas canvas = canvasGo.GetComponent<GameCanvas>()!;
        canvas.RenderMode = RenderMode.WorldSpace;

        GameObject panel = ui.AddChild(canvasGo, "Panel");
        panel.RectTransform!.SizeDelta = new Float2(400f, 300f);
        GameObject elem = ui.AddChild(panel, "Elem");
        elem.RectTransform!.SizeDelta = new Float2(100f, 50f);
        QuadProbe probe = elem.AddComponent<QuadProbe>();

        Debug.ClearGizmos();
        UIGizmos.DrawRect(probe, UIGizmos.SelectedColor, drawPivot: true, drawAnchors: true);
        Mesh wire = GetWire();

        // outline (4 lineas) + pivot (2 lineas + circulo de 16 segmentos)
        // + 4 triángulos de ancla (3 lineas c/u) = 34 lineas
        Assert.Equal(34, LineCount(wire));
        Assert.Equal(68, wire.VertexCount);
        Assert.Equal(68, wire.IndexCount);
        Assert.Contains(wire.Colors, c => c == WireColor(UIGizmos.SelectedColor));
        Assert.Contains(wire.Colors, c => c == WireColor(UIGizmos.PivotColor));
        Assert.Contains(wire.Colors, c => c == WireColor(UIGizmos.AnchorColor));

        // GENERAL OBSERVATION (sin ID nuevo): DrawAnchorHandles lee el ComputedRect del padre, y
        // RectTransform.ComputeRect solo lo escribe BuildRecursive sobre los *hijos* - el
        // GameObject raiz del canvas nunca pasa por ComputeRect, asi que su ComputedRect.Size es 0
        // y un hijo directo del canvas nunca recibe asas de ancla (hace falta anidar:
        // canvas -> panel -> elem). Documentado aqui con el conteo exacto: 4 + 2 + 16 = 22 lineas.
        GameObject direct = ui.AddChild(canvasGo, "Direct");
        QuadProbe directProbe = direct.AddComponent<QuadProbe>();
        direct.RectTransform!.SizeDelta = new Float2(60f, 60f);
        Debug.ClearGizmos();
        UIGizmos.DrawRect(directProbe, UIGizmos.SelectedColor, drawPivot: true, drawAnchors: true);
        Mesh directWire = GetWire();
        Assert.Equal(22, LineCount(directWire));
        Assert.DoesNotContain(directWire.Colors, c => c == WireColor(UIGizmos.AnchorColor));
    }
}
