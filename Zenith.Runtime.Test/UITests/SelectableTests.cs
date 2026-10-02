// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

public class SelectableTests
{
    private static PointerEventData Left => new() { Button = MouseButton.Left };

    [Fact]
    public void Defaults_EstadoInicialNormal()
    {
        using var ui = new UITestHelpers();
        Selectable sel = ui.NewRect("Btn").AddComponent<Selectable>();

        Assert.Equal(SelectionState.Normal, sel.CurrentState);
        Assert.False(sel.IsHovered);
        Assert.False(sel.IsPressed);
        Assert.True(sel.Interactable);
        Assert.Equal(SelectableTransition.ColorTint, sel.Transition);
        Assert.Equal(0.08f, sel.TransitionDuration);
        Assert.Equal(new Color(1f, 1f, 1f, 1f), sel.NormalColor);
        Assert.Equal(new Color(0.96f, 0.96f, 0.96f, 1f), sel.HighlightedColor);
        Assert.Equal(new Color(0.78f, 0.78f, 0.78f, 1f), sel.PressedColor);
        Assert.Equal(new Color(0.96f, 0.96f, 0.96f, 1f), sel.SelectedColor);
        Assert.Equal(new Color(0.78f, 0.78f, 0.78f, 0.5f), sel.DisabledColor);
    }

    [Fact]
    public void PointerEnter_PasaAHighlighted()
    {
        using var ui = new UITestHelpers();
        Selectable sel = ui.NewRect("Btn").AddComponent<Selectable>();

        sel.OnPointerEnter(Left);

        Assert.True(sel.IsHovered);
        Assert.Equal(SelectionState.Highlighted, sel.CurrentState);
    }

    [Fact]
    public void PointerExit_VuelveANormal()
    {
        using var ui = new UITestHelpers();
        Selectable sel = ui.NewRect("Btn").AddComponent<Selectable>();

        sel.OnPointerEnter(Left);
        sel.OnPointerExit(Left);

        Assert.False(sel.IsHovered);
        Assert.Equal(SelectionState.Normal, sel.CurrentState);
    }

    [Fact]
    public void PointerDown_Izquierdo_Pulsa()
    {
        using var ui = new UITestHelpers();
        Selectable sel = ui.NewRect("Btn").AddComponent<Selectable>();

        sel.OnPointerDown(Left);

        Assert.True(sel.IsPressed);
        Assert.Equal(SelectionState.Pressed, sel.CurrentState);
    }

    [Fact]
    public void PointerDown_Derecho_Ignorado()
    {
        using var ui = new UITestHelpers();
        Selectable sel = ui.NewRect("Btn").AddComponent<Selectable>();

        sel.OnPointerDown(new PointerEventData { Button = MouseButton.Right });

        Assert.False(sel.IsPressed);
        Assert.Equal(SelectionState.Normal, sel.CurrentState);
    }

    [Fact]
    public void PointerDown_NoInteractuable_Ignorado()
    {
        using var ui = new UITestHelpers();
        Selectable sel = ui.NewRect("Btn").AddComponent<Selectable>();

        sel.Interactable = false;
        Assert.Equal(SelectionState.Disabled, sel.CurrentState);

        sel.OnPointerDown(Left);

        Assert.False(sel.IsPressed);
        Assert.Equal(SelectionState.Disabled, sel.CurrentState);
    }

    [Fact]
    public void PointerUp_ConHover_Highlighted_SinHover_Normal()
    {
        using var ui = new UITestHelpers();
        Selectable sel = ui.NewRect("Btn").AddComponent<Selectable>();

        // Suelte con el puntero encima: queda en Highlighted.
        sel.OnPointerEnter(Left);
        sel.OnPointerDown(Left);
        sel.OnPointerUp(Left);
        Assert.False(sel.IsPressed);
        Assert.Equal(SelectionState.Highlighted, sel.CurrentState);

        // Prensa, sale del widget con el botón apretado (conserva el visual de Pressed) y suelta: Normal.
        sel.OnPointerDown(Left);
        sel.OnPointerExit(Left);
        Assert.False(sel.IsHovered);
        Assert.Equal(SelectionState.Pressed, sel.CurrentState);
        sel.OnPointerUp(Left);
        Assert.Equal(SelectionState.Normal, sel.CurrentState);
    }

    [Fact]
    public void Prioridad_Estados_TienePrecedenciaSobreHoverYSeleccion()
    {
        using var ui = new UITestHelpers();
        Selectable sel = ui.NewRect("Btn").AddComponent<Selectable>();

        sel.OnSelect();
        Assert.Equal(SelectionState.Selected, sel.CurrentState);

        sel.OnPointerEnter(Left); // hover gana a seleccionado
        Assert.Equal(SelectionState.Highlighted, sel.CurrentState);

        sel.OnPointerDown(Left); // pressed gana a hover
        Assert.Equal(SelectionState.Pressed, sel.CurrentState);

        sel.Interactable = false; // disabled gana a todo
        Assert.Equal(SelectionState.Disabled, sel.CurrentState);
    }

    [Fact]
    public void OnEnable_EscenaActiva_AplicaColorInmediato()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();

        GameObject go = ui.NewRect("Btn");
        UIImage img = go.AddComponent<UIImage>();
        img.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), img.Color);

        scene.Add(go); // el GO entra en una escena activa...
        Selectable sel = go.AddComponent<Selectable>(); // ...y AddComponent dispara OnEnable síncrono

        Assert.Equal(SelectionState.Normal, sel.CurrentState);
        Assert.Equal(new Color(1f, 1f, 1f, 1f), img.Color);
    }

    [Fact]
    public void Update_AplicaColorObjetivo_Deterministico()
    {
        using var ui = new UITestHelpers();
        GameObject go = ui.NewRect("Btn");
        UIImage img = go.AddComponent<UIImage>();
        img.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
        Selectable sel = go.AddComponent<Selectable>(); // sin escena: sin OnEnable, color intacto

        sel.OnPointerEnter(Left);
        Assert.Equal(SelectionState.Highlighted, sel.CurrentState);
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), img.Color); // la transición es diferida

        ui.Time.DeltaTime = 1f; // dt >= duración (0.08) -> t=1 -> Lerp exacto al objetivo
        sel.Update();

        Assert.Equal(new Color(0.96f, 0.96f, 0.96f, 1f), img.Color);
    }

    [Fact]
    public void TransitionSetter_AplicaColorSincrono()
    {
        using var ui = new UITestHelpers();
        GameObject go = ui.NewRect("Btn");
        UIImage img = go.AddComponent<UIImage>();
        img.Color = new Color(0.5f, 0.5f, 0.5f, 1f);
        Selectable sel = go.AddComponent<Selectable>();

        sel.OnPointerEnter(Left);
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), img.Color); // aún sin Update

        sel.Transition = SelectableTransition.ColorTint; // setter con RefreshState(immediate: true)

        Assert.Equal(new Color(0.96f, 0.96f, 0.96f, 1f), img.Color);
    }

    [Fact]
    public void Navigation_BusquedaDireccionalPorAlineacion()
    {
        using var ui = new UITestHelpers();
        Scene scene = ui.NewScene();
        GameObject canvasGo = ui.CreateCanvas(scene);

        GameObject aGo = ui.AddChild(canvasGo, "A");
        aGo.RectTransform!.AnchoredPosition = new Float2(-200f, 0f);
        Selectable a = aGo.AddComponent<Selectable>();

        GameObject bGo = ui.AddChild(canvasGo, "B");
        bGo.RectTransform!.AnchoredPosition = new Float2(200f, 0f);
        Selectable b = bGo.AddComponent<Selectable>();

        canvasGo.GetComponent<GameCanvas>()!.RebuildIfDirty();

        Assert.Same(b, a.FindSelectableOnRight());
        Assert.Same(a, b.FindSelectableOnLeft());
        Assert.Null(a.FindSelectableOnUp()); // B queda exactamente a la derecha: alineación 0 en Y
        Assert.Null(a.FindSelectableOnDown());

        Navigation nav = a.Navigation;
        nav.Mode = NavigationMode.None;
        a.Navigation = nav;
        Assert.Null(a.FindSelectableOnRight());

        Selectable lonely = ui.NewRect("Lonely").AddComponent<Selectable>(); // sin escena -> null
        Assert.Null(lonely.FindSelectableOnLeft());
    }
}
