// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

public class UIScrollRectTests
{
    // root: centro (0.5,0.5), SizeDelta (1000,500) -> ComputedRect (460,290)-(1460,790) en una
    // ventana de 1920x1080. content: hijo centrado, SizeDelta (1500,2000) -> (210,-460)-(1710,1540).
    // Rangos de scroll: X en [-250,250], Y en [-750,750]; normalizados en 0: H=0.5, V=0.5.
    private static (UIScrollRect scroll, RectTransform rootRt, RectTransform contentRt) BuildScroll(UITestHelpers ui)
    {
        GameObject root = ui.NewRect("Scroll");
        RectTransform rootRt = root.RectTransform!;
        rootRt.AnchorMin = rootRt.AnchorMax = new Float2(0.5f, 0.5f);
        rootRt.SizeDelta = new Float2(1000f, 500f);
        rootRt.AnchoredPosition = Float2.Zero;
        rootRt.ComputeRect(new Rect(0f, 0f, 1920f, 1080f));

        GameObject contentGo = ui.AddChild(root, "Content");
        RectTransform contentRt = contentGo.RectTransform!;
        contentRt.AnchorMin = contentRt.AnchorMax = new Float2(0.5f, 0.5f);
        contentRt.SizeDelta = new Float2(1500f, 2000f);
        contentRt.AnchoredPosition = Float2.Zero;
        contentRt.ComputeRect(rootRt.ComputedRect);

        UIScrollRect scroll = root.AddComponent<UIScrollRect>();
        scroll.Content = contentRt;
        return (scroll, rootRt, contentRt);
    }

    private static void Layout(RectTransform rootRt, RectTransform contentRt)
    {
        rootRt.ComputeRect(new Rect(0f, 0f, 1920f, 1080f));
        contentRt.ComputeRect(rootRt.ComputedRect);
    }

    [Fact]
    public void Defaults_Y_SinContent_NoOp()
    {
        using var ui = new UITestHelpers();
        GameObject root = ui.NewRect("Scroll");
        UIScrollRect scroll = root.AddComponent<UIScrollRect>();

        Assert.Equal(UIScrollRect.ScrollMovementType.Elastic, scroll.MovementType);
        Assert.Equal(0.1f, scroll.Elasticity);
        Assert.True(scroll.Horizontal);
        Assert.True(scroll.Vertical);
        Assert.True(scroll.Inertia);
        Assert.Equal(30f, scroll.ScrollSensitivity);
        Assert.Equal(0.135f, scroll.DecelerationRate);
        Assert.Null(scroll.HorizontalScrollbar);
        Assert.Null(scroll.VerticalScrollbar);
        Assert.Null(scroll.Content);

        // Sin Content: los handlers no hacen nada ni lanzan.
        scroll.OnScroll(new PointerEventData { ScrollDelta = 1f });
        scroll.OnBeginDrag(new PointerEventData());
        scroll.OnDrag(new PointerEventData());
        scroll.Update();
        Assert.Equal(Float2.Zero, scroll.Overshoot);
    }

    [Fact]
    public void OnScrollVertical_Clamps_Y_NormalizadosRoundTrip()
    {
        using var ui = new UITestHelpers();
        (UIScrollRect scroll, RectTransform rootRt, RectTransform contentRt) = BuildScroll(ui);
        int fired = 0;
        scroll.OnValueChanged += () => fired++;

        scroll.OnScroll(new PointerEventData { ScrollDelta = 1f }); // +30px
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(0f, 30f), contentRt.AnchoredPosition);

        scroll.OnScroll(new PointerEventData { ScrollDelta = 25f }); // +750px -> borde exacto
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(0f, 750f), contentRt.AnchoredPosition);
        Assert.Equal(1f, scroll.VerticalNormalized());

        scroll.SetVerticalNormalized(0.25f);
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(0f, -375f), contentRt.AnchoredPosition);
        Assert.Equal(0.25f, scroll.VerticalNormalized());

        scroll.SetHorizontalNormalized(1f);
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(-250f, -375f), contentRt.AnchoredPosition);
        Assert.Equal(1f, scroll.HorizontalNormalized());

        Assert.Equal(4, fired);
    }

    [Fact]
    public void OnScroll_RespetoDeEjes()
    {
        using var ui = new UITestHelpers();
        (UIScrollRect scroll, RectTransform rootRt, RectTransform contentRt) = BuildScroll(ui);

        scroll.Vertical = false;
        scroll.OnScroll(new PointerEventData { ScrollDelta = 1f });
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(30f, 0f), contentRt.AnchoredPosition);

        scroll.Vertical = true;
        contentRt.AnchoredPosition = Float2.Zero;
        Layout(rootRt, contentRt);
        scroll.OnScroll(new PointerEventData { ScrollDelta = 1f });
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(0f, 30f), contentRt.AnchoredPosition);
    }

    [Fact]
    public void OnDragUnrestricted_SigueElDeltaDelDiseno()
    {
        using var ui = new UITestHelpers();
        (UIScrollRect scroll, RectTransform rootRt, RectTransform contentRt) = BuildScroll(ui);
        scroll.MovementType = UIScrollRect.ScrollMovementType.Unrestricted;

        PointerEventData e = new() { DesignPosition = new Float2(100f, 100f) };
        scroll.OnBeginDrag(e);

        e.DesignPosition = new Float2(150f, 60f);
        scroll.OnDrag(e);
        Assert.Equal(new Float2(50f, -40f), contentRt.AnchoredPosition);
        Assert.True(e.Used);

        e.DesignPosition = new Float2(1000f, 1000f); // sigue al delta, sin tocar límites
        scroll.OnDrag(e);
        Assert.Equal(new Float2(900f, 900f), contentRt.AnchoredPosition);
    }

    [Fact]
    public void OnDragClamped_SeDetieneEnElLimite()
    {
        using var ui = new UITestHelpers();
        (UIScrollRect scroll, RectTransform rootRt, RectTransform contentRt) = BuildScroll(ui);
        scroll.MovementType = UIScrollRect.ScrollMovementType.Clamped;

        PointerEventData e = new() { DesignPosition = new Float2(100f, 100f) };
        scroll.OnBeginDrag(e);

        e.DesignPosition = new Float2(100f, 900f); // delta (0,800) -> borde 750
        scroll.OnDrag(e);
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(0f, 750f), contentRt.AnchoredPosition);

        e.DesignPosition = new Float2(-400f, 100f); // delta relativo al inicio (-500,0) -> borde -250
        scroll.OnDrag(e);
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(-250f, 0f), contentRt.AnchoredPosition);
    }

    [Fact]
    public void OnDragElastic_RubberDeltaNoAlcanzaElBorde()
    {
        using var ui = new UITestHelpers();
        (UIScrollRect scroll, RectTransform rootRt, RectTransform contentRt) = BuildScroll(ui);

        PointerEventData e = new() { DesignPosition = new Float2(100f, 100f) };
        scroll.OnBeginDrag(e);

        e.DesignPosition = new Float2(100f, 900f); // delta (0,800): 750 de borde + 50 de overshoot
        scroll.OnDrag(e);

        // Mismo RubberDelta que aplica la producción sobre los 50px de overshoot de un viewport de 500.
        float viewSize = 500f;
        float rubber = (1f - 1f / (50f * 0.55f / viewSize + 1f)) * viewSize * MathF.Sign(50f);
        Assert.True(rubber < 50f);
        Assert.Equal(new Float2(0f, 750f + rubber), contentRt.AnchoredPosition);

        Layout(rootRt, contentRt);
        // Precisión 3: Overshoot se recalcula desde ComputedRect a magnitud ~2300 (ulp ~2.4e-4),
        // así que el resultado difiere de la fórmula exacta en ~7e-5.
        Assert.Equal(rubber, scroll.Overshoot.Y, 3);
        Assert.Equal(0f, scroll.Overshoot.X);
    }

    [Fact]
    public void UpdateElasticityCero_SnapExatoSinInercia()
    {
        using var ui = new UITestHelpers();
        (UIScrollRect scroll, RectTransform rootRt, RectTransform contentRt) = BuildScroll(ui);

        contentRt.AnchoredPosition = new Float2(0f, 780f); // 30px fuera del borde 750
        Layout(rootRt, contentRt);
        scroll.Elasticity = 0f;

        scroll.Update();
        Layout(rootRt, contentRt);
        Assert.Equal(new Float2(0f, 750f), contentRt.AnchoredPosition);
        Assert.Equal(Float2.Zero, scroll.Overshoot);

        scroll.Update(); // sin offset no hay muelle, y la velocidad quedó a cero: nada cambia
        Assert.Equal(new Float2(0f, 750f), contentRt.AnchoredPosition);
    }
}
