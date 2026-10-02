// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

public class UIRaycasterTests
{
    private static readonly Float2 WindowSize = new(1920f, 1080f);

    private static (Scene scene, GameObject canvasGo, GameObject target) BuildCanvas(UITestHelpers ui)
    {
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameObject target = ui.AddChild(canvasGo, "Target");
        target.RectTransform!.SizeDelta = new Float2(200f, 200f);
        target.AddComponent<UIImage>();
        return (scene, canvasGo, target);
    }

    [Fact]
    public void TryPick_EscenaNula_False()
    {
        using var ui = new UITestHelpers();

        bool hit = UIRaycaster.TryPick(null, new Float2(900f, 600f), WindowSize, out UIRaycaster.Hit result);

        Assert.False(hit);
        Assert.Null(result.GameObject);
        Assert.Equal(Float2.Zero, result.DesignPosition);
    }

    [Fact]
    public void TryPick_Y_Proyeccion_CoordenadasExactas()
    {
        using var ui = new UITestHelpers();
        (Scene scene, GameObject canvasGo, GameObject target) = BuildCanvas(ui);
        GameCanvas canvas = canvasGo.GetComponent<GameCanvas>()!;

        bool hit = UIRaycaster.TryPick(scene, new Float2(900f, 600f), WindowSize, out UIRaycaster.Hit result);

        Assert.True(hit);
        Assert.Same(target, result.GameObject);
        Assert.Same(canvas, result.Canvas);
        Assert.Equal(new Float2(900f, 480f), result.DesignPosition); // y_diseño = 1080 - 600

        Assert.True(UIRaycaster.TryProjectPointer(canvas, new Float2(900f, 600f), WindowSize, out Float2 design));
        Assert.Equal(new Float2(900f, 480f), design);
    }

    [Fact]
    public void TryPick_FueraDelRect_False()
    {
        using var ui = new UITestHelpers();
        (Scene scene, _, _) = BuildCanvas(ui);

        bool hit = UIRaycaster.TryPick(scene, new Float2(500f, 500f), WindowSize, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryPick_RaycastTargetDeshabilitado_False()
    {
        using var ui = new UITestHelpers();
        (Scene scene, _, GameObject target) = BuildCanvas(ui);
        target.GetComponent<UIImage>()!.RaycastTarget = false;

        bool hit = UIRaycaster.TryPick(scene, new Float2(900f, 600f), WindowSize, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryPick_SinGraphic_False()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameObject target = ui.AddChild(canvasGo, "Target");
        target.RectTransform!.SizeDelta = new Float2(200f, 200f);
        target.AddComponent<Selectable>(); // un Selectable sin Graphic no es target de raycast

        bool hit = UIRaycaster.TryPick(scene, new Float2(900f, 600f), WindowSize, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryPick_HermanoSuperiorGana()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);

        GameObject a = ui.AddChild(canvasGo, "A");
        a.RectTransform!.SizeDelta = new Float2(200f, 200f);
        a.AddComponent<UIImage>();

        GameObject b = ui.AddChild(canvasGo, "B"); // después en el orden de hermanos = dibujado encima
        b.RectTransform!.SizeDelta = new Float2(200f, 200f);
        b.AddComponent<UIImage>();

        bool hit = UIRaycaster.TryPick(scene, new Float2(900f, 600f), WindowSize, out UIRaycaster.Hit result);

        Assert.True(hit);
        Assert.Same(b, result.GameObject);
    }

    [Fact]
    public void TryPick_BordeInclusivo()
    {
        using var ui = new UITestHelpers();
        (Scene scene, _, _) = BuildCanvas(ui);

        // Borde inferior-izquierdo del target: pantalla (860,640) -> diseño (860,440) = rect.Min.
        Assert.True(UIRaycaster.TryPick(scene, new Float2(860f, 640f), WindowSize, out _));
        Assert.False(UIRaycaster.TryPick(scene, new Float2(859f, 640f), WindowSize, out _));
    }

    [Fact]
    public void Estaticas_RayHitsRect_Intersect_Contains()
    {
        using var ui = new UITestHelpers();

        RectTransform rt = ui.NewRect("Element").RectTransform!;
        rt.SizeDelta = new Float2(100f, 50f);
        rt.ComputeRect(new Rect(0f, 0f, 200f, 100f)); // rect (50,25)-(150,75), pivot 0.5 -> local (-50..50, -25..25)

        // Modelo identidad: el rayo atraviesa el centro del elemento.
        bool center = UIRaycaster.RayHitsRect(Float4x4.Identity, rt, new Float3(0f, 0f, 10f), new Float3(0f, 0f, -1f), out float t);
        Assert.True(center);
        Assert.Equal(10f, t);

        Assert.False(UIRaycaster.RayHitsRect(Float4x4.Identity, rt, new Float3(60f, 0f, 10f), new Float3(0f, 0f, -1f), out _)); // |x|=60 > medio-ancho 50
        Assert.False(UIRaycaster.RayHitsRect(Float4x4.Identity, rt, new Float3(0f, 0f, 10f), new Float3(1f, 0f, 0f), out _)); // paralelo al plano
        Assert.False(UIRaycaster.RayHitsRect(Float4x4.Identity, rt, new Float3(0f, 0f, -10f), new Float3(0f, 0f, -1f), out _)); // detrás del origen

        RectTransform degenerate = ui.NewRect("Degenerate").RectTransform!;
        degenerate.SizeDelta = Float2.Zero; // rect de tamaño 0 -> nunca golpea
        degenerate.ComputeRect(new Rect(0f, 0f, 200f, 100f));
        Assert.False(UIRaycaster.RayHitsRect(Float4x4.Identity, degenerate, new Float3(0f, 0f, 10f), new Float3(0f, 0f, -1f), out _));

        // IntersectRect: solape y separación (el recorte colapsa al borde cercano).
        UITestHelpers.AssertRect(UIRaycaster.IntersectRect(new Rect(0f, 0f, 10f, 10f), new Rect(5f, 5f, 15f, 15f)), 5f, 5f, 10f, 10f);
        UITestHelpers.AssertRect(UIRaycaster.IntersectRect(new Rect(0f, 0f, 1f, 1f), new Rect(5f, 5f, 6f, 6f)), 5f, 5f, 5f, 5f);

        // RectContainsPoint: bordes inclusivos.
        Rect box = new(0f, 0f, 10f, 10f);
        Assert.True(UIRaycaster.RectContainsPoint(box, new Float2(0f, 0f)));
        Assert.True(UIRaycaster.RectContainsPoint(box, new Float2(10f, 10f)));
        Assert.False(UIRaycaster.RectContainsPoint(box, new Float2(10.5f, 5f)));
        Assert.False(UIRaycaster.RectContainsPoint(box, new Float2(-0.5f, 5f)));
    }
}
