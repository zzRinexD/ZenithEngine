// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

public class EventSystemTests
{
    /// <summary>Sonda que cuenta los eventos dispatcheados por el EventSystem.</summary>
    private sealed class UiProbe : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        ISubmitHandler, ICancelHandler
    {
        public int EnterCount;
        public int ExitCount;
        public int DownCount;
        public int ClickCount;
        public int BeginCount;
        public int DragCount;
        public int EndCount;
        public int SubmitCount;
        public int CancelCount;
        public int LastClickStreak;

        public void OnPointerEnter(PointerEventData e) => EnterCount++;
        public void OnPointerExit(PointerEventData e) => ExitCount++;
        public void OnPointerDown(PointerEventData e) => DownCount++;
        public void OnPointerClick(PointerEventData e)
        {
            ClickCount++;
            LastClickStreak = e.ClickCount;
        }
        public void OnBeginDrag(PointerEventData e) => BeginCount++;
        public void OnDrag(PointerEventData e) => DragCount++;
        public void OnEndDrag(PointerEventData e) => EndCount++;
        public void OnSubmit() => SubmitCount++;
        public void OnCancel() => CancelCount++;
    }

    private static (UITestHelpers ui, Scene scene, GameObject target, UiProbe probe, EventSystem es) Setup(UITestHelpers ui)
    {
        Scene scene = ui.NewScene(makeCurrent: true);
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameObject target = ui.AddChild(canvasGo, "Target");
        target.RectTransform!.SizeDelta = new Float2(200f, 200f);
        target.AddComponent<UIImage>();
        UiProbe probe = target.AddComponent<UiProbe>();
        EventSystem es = ui.NewEventSystem(scene);
        return (ui, scene, target, probe, es);
    }

    [Fact]
    public void OnEnable_PrimerInstanciaGana_SegundaInerte()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene(makeCurrent: true);
        EventSystem es1 = ui.NewEventSystem(scene);
        EventSystem es2 = ui.NewEventSystem(scene);

        Assert.Same(es1, EventSystem.Current);

        es2.Update(); // la instancia que no es Current no debe reclamar el tick
        Assert.Same(es1, EventSystem.Current);
    }

    [Fact]
    public void OnDisable_LimpiaCurrentYEstado()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene(makeCurrent: true);
        GameObject focus = ui.NewRect("Focus");
        scene.Add(focus);
        EventSystem es = ui.NewEventSystem(scene);

        es.SetSelected(focus);
        es.Left.ClickCount = 5;
        Assert.Same(focus, es.Selected);

        scene.Disable(); // OnDisable -> ClearState

        Assert.Null(EventSystem.Current);
        Assert.Null(es.Selected);
        Assert.Null(es.Hovered);
        Assert.Equal(0, es.Left.ClickCount);
    }

    [Fact]
    public void Hover_EntradaYSalida_DispatchExacto()
    {
        using var ui = new UITestHelpers();
        (_, Scene scene, GameObject target, UiProbe probe, EventSystem es) = Setup(ui);

        // Puntero sobre el target (pantalla 900,600 -> diseño 900,480 dentro del rect 860..1060).
        ui.SetPointer(es, 900f, 600f);
        es.Update();
        Assert.Same(target, es.Hovered);
        Assert.Equal(1, probe.EnterCount);

        es.Update(); // puntero quieto: sin re-disparar Enter
        Assert.Equal(1, probe.EnterCount);
        Assert.Equal(0, probe.ExitCount);

        ui.SetPointer(es, 100f, 100f);
        es.Update();
        Assert.Null(es.Hovered);
        Assert.Equal(1, probe.ExitCount);
        Assert.Equal(1, probe.EnterCount);
    }

    [Fact]
    public void Click_Unico_MismoTarget()
    {
        using var ui = new UITestHelpers();
        (_, Scene scene, GameObject target, UiProbe probe, EventSystem es) = Setup(ui);
        ui.SetPointer(es, 900f, 600f);

        ui.Input.SetMouseButton(0, true);
        es.Update();
        Assert.Equal(1, probe.DownCount);
        Assert.Equal(1, es.Left.ClickCount);
        Assert.Same(target, es.Left.PressedOn);
        // KNOWN ISSUE: los bordes down/up del ratón no caducan solos: hay que limpiarlos a mano
        // por tick para que el botón mantenido no re-dispare Down. See docs/PLAN_10_DE_10.md Fase 6. (H-UI-9)
        ui.Input.ClearMouseButtonTransitions();

        ui.Input.SetMouseButton(0, false);
        es.Update();
        Assert.Equal(1, probe.ClickCount);
        Assert.Equal(1, probe.LastClickStreak);
        Assert.Null(es.Left.PressedOn);
        ui.Input.ClearMouseButtonTransitions();
    }

    [Fact]
    public void MultiClick_Racha_DentroYFueraDeVentana()
    {
        using var ui = new UITestHelpers();
        (_, Scene scene, GameObject target, UiProbe probe, EventSystem es) = Setup(ui);
        ui.SetPointer(es, 900f, 600f);

        ClickOnce(ui, es);
        Assert.Equal(1, probe.ClickCount);
        Assert.Equal(1, probe.LastClickStreak);

        ui.AdvanceTime(0.1f); // dentro de la ventana (0.4s): la racha sube
        ClickOnce(ui, es);
        Assert.Equal(2, probe.ClickCount);
        Assert.Equal(2, probe.LastClickStreak);

        ui.AdvanceTime(0.5f); // fuera de la ventana: la racha vuelve a 1
        ClickOnce(ui, es);
        Assert.Equal(3, probe.ClickCount);
        Assert.Equal(1, probe.LastClickStreak);
    }

    private static void ClickOnce(UITestHelpers ui, EventSystem es)
    {
        ui.Input.SetMouseButton(0, true);
        es.Update();
        ui.Input.SetMouseButton(0, false);
        es.Update();
        ui.Input.ClearMouseButtonTransitions();
    }

    [Fact]
    public void Drag_Umbral_BeginDrag_EndSinClick()
    {
        using var ui = new UITestHelpers();
        (_, Scene scene, GameObject target, UiProbe probe, EventSystem es) = Setup(ui);
        ui.SetPointer(es, 900f, 600f);

        ui.Input.SetMouseButton(0, true);
        es.Update();
        Assert.Same(target, es.Left.PressedOn);
        ui.Input.ClearMouseButtonTransitions();

        ui.SetPointer(es, 906f, 600f);
        es.Update(); // 6px >= umbral 4px -> BeginDrag
        Assert.True(es.Left.IsDragging);
        Assert.Equal(1, probe.BeginCount);
        ui.Input.ClearMouseButtonTransitions();

        ui.SetPointer(es, 916f, 600f);
        es.Update(); // delta (10,0) -> OnDrag
        Assert.Equal(1, probe.DragCount);
        ui.Input.ClearMouseButtonTransitions();

        ui.Input.SetMouseButton(0, false);
        es.Update(); // suelta -> EndDrag y NO Click
        Assert.Equal(1, probe.EndCount);
        Assert.Equal(0, probe.ClickCount);
        Assert.False(es.Left.IsDragging);
        Assert.Null(es.Left.PressedOn);
        ui.Input.ClearMouseButtonTransitions();
    }

    [Fact]
    public void Teclado_SubmitCancel_SobreSeleccionado()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene(makeCurrent: true);
        GameObject focus = ui.NewRect("Focus");
        scene.Add(focus);
        UiProbe probe = focus.AddComponent<UiProbe>();
        EventSystem es = ui.NewEventSystem(scene);

        es.SetSelected(focus);
        Assert.Same(focus, es.Selected);

        ui.Input.PressKey(KeyCode.Enter);
        es.Update();
        Assert.Equal(1, probe.SubmitCount);

        ui.Input.ReleaseKey(KeyCode.Enter);
        ui.Input.ClearKeyTransitions();

        ui.Input.PressKey(KeyCode.Escape);
        es.Update();
        Assert.Equal(1, probe.CancelCount);
        Assert.Null(es.Selected); // Cancel limpia la selección
        ui.Input.ClearKeyTransitions();
    }

    [Fact]
    public void ViewportGated_ReceivesInputFalso_SinHits()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene(makeCurrent: true);
        GameObject canvasGo = ui.CreateCanvas(scene);
        GameObject target = ui.AddChild(canvasGo, "Target");
        target.RectTransform!.SizeDelta = new Float2(200f, 200f);
        target.AddComponent<UIImage>();
        UiProbe probe = target.AddComponent<UiProbe>();

        // Puntero ya posicionado sobre el target, pero el host dice que no recibe input.
        EventSystem es = ui.NewEventSystem(scene, pointer: new Float2(900f, 600f), receivesInput: false);

        es.Update();
        Assert.Null(es.Hovered);
        Assert.Equal(0, probe.EnterCount);

        ui.Input.SetMouseButton(0, true);
        es.Update();
        Assert.Equal(0, probe.DownCount);
        ui.Input.SetMouseButton(0, false);
        es.Update();
        ui.Input.ClearMouseButtonTransitions();

        es.Viewport = new EventSystem.HostViewport
        {
            ReferenceSize = new Float2(1920f, 1080f),
            PointerPosition = new Float2(900f, 600f),
            ReceivesInput = true,
        };
        es.Update();
        Assert.Same(target, es.Hovered);
        Assert.Equal(1, probe.EnterCount);
    }
}
