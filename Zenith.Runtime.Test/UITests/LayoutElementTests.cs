// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

public class LayoutElementTests
{
    [Fact]
    public void NewLayoutElement_ReportsUnspecifiedAndTakesPart()
    {
        using var ui = new UITestHelpers();
        LayoutElement le = ui.NewGameObject("Elem").AddComponent<LayoutElement>();

        Assert.Equal(-1f, le.MinWidth);
        Assert.Equal(-1f, le.MinHeight);
        Assert.Equal(-1f, le.PreferredWidth);
        Assert.Equal(-1f, le.PreferredHeight);
        Assert.Equal(-1f, le.FlexibleWidth);
        Assert.Equal(-1f, le.FlexibleHeight);
        Assert.False(le.IgnoreLayout);
    }

    [Fact]
    public void LayoutElement_RoundTripsEveryProperty()
    {
        using var ui = new UITestHelpers();
        LayoutElement le = ui.NewGameObject("Elem").AddComponent<LayoutElement>();

        le.MinWidth = 10f;
        le.MinHeight = 20f;
        le.PreferredWidth = 30f;
        le.PreferredHeight = 40f;
        le.FlexibleWidth = 50f;
        le.FlexibleHeight = 60f;
        le.IgnoreLayout = true;

        Assert.Equal(10f, le.MinWidth);
        Assert.Equal(20f, le.MinHeight);
        Assert.Equal(30f, le.PreferredWidth);
        Assert.Equal(40f, le.PreferredHeight);
        Assert.Equal(50f, le.FlexibleWidth);
        Assert.Equal(60f, le.FlexibleHeight);
        Assert.True(le.IgnoreLayout);
    }

    [Fact]
    public void GetSizes_WithoutLayoutElement_FallBackToIntrinsicSizeDelta()
    {
        using var ui = new UITestHelpers();
        GameObject go = ui.NewRect();
        go.RectTransform!.SizeDelta = new Float2(137f, 42f);

        Float2 preferred = LayoutUtility.GetPreferredSize(go);
        Float2 min = LayoutUtility.GetMinSize(go);
        Float2 flexible = LayoutUtility.GetFlexible(go);

        Assert.Equal(137f, preferred.X);
        Assert.Equal(42f, preferred.Y);
        Assert.Equal(137f, min.X);
        Assert.Equal(42f, min.Y);
        Assert.Equal(0f, flexible.X);
        Assert.Equal(0f, flexible.Y);
    }

    [Theory]
    [InlineData(250f, -1f, 250f)]
    [InlineData(100f, 300f, 300f)]
    public void GetPreferredSize_TakesMaxOfPreferredAndMin(float preferredWidth, float minWidth, float expectedX)
    {
        using var ui = new UITestHelpers();
        GameObject go = ui.NewRect();
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.PreferredWidth = preferredWidth;
        le.MinWidth = minWidth;

        Assert.Equal(expectedX, LayoutUtility.GetPreferredSize(go).X);
    }

    [Fact]
    public void GetSizes_TakeComponentWiseMaxAcrossLayoutElements()
    {
        using var ui = new UITestHelpers();
        GameObject go = ui.NewRect();
        LayoutElement a = go.AddComponent<LayoutElement>();
        LayoutElement b = go.AddComponent<LayoutElement>();
        go.RectTransform!.SizeDelta = new Float2(50f, 50f);

        a.PreferredWidth = 100f;
        a.MinWidth = 60f;
        a.FlexibleWidth = 1f;
        b.PreferredWidth = 250f;
        b.MinWidth = 40f;
        b.FlexibleWidth = 3f;

        Float2 preferred = LayoutUtility.GetPreferredSize(go);
        Float2 min = LayoutUtility.GetMinSize(go);
        Float2 flexible = LayoutUtility.GetFlexible(go);

        Assert.Equal(250f, preferred.X); // max(100,250)
        Assert.Equal(50f, preferred.Y);  // both -1 => intrinsic SizeDelta.Y
        Assert.Equal(60f, min.X);        // max(60,40)
        Assert.Equal(50f, min.Y);        // intrinsic
        Assert.Equal(3f, flexible.X);    // max(1,3)
        Assert.Equal(0f, flexible.Y);    // both -1 => none
    }

    [Fact]
    public void GetSizes_DisabledLayoutElement_FallsBackToIntrinsic()
    {
        using var ui = new UITestHelpers();
        GameObject go = ui.NewRect();
        go.RectTransform!.SizeDelta = new Float2(77f, 88f);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.PreferredWidth = 250f;
        le.Enabled = false;

        Float2 preferred = LayoutUtility.GetPreferredSize(go);

        Assert.Equal(77f, preferred.X);
        Assert.Equal(88f, preferred.Y);
    }

    [Fact]
    public void LayoutUtility_MemoKeepsStaleSizesUntilInvalidateCache()
    {
        using var ui = new UITestHelpers();
        GameObject go = ui.NewRect();
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.PreferredWidth = 100f;
        le.FlexibleWidth = 2f;

        Assert.Equal(100f, LayoutUtility.GetPreferredSize(go).X);
        Float2 flexible = LayoutUtility.GetFlexible(go);
        Assert.Equal(2f, flexible.X);
        Assert.Equal(0f, flexible.Y);

        le.PreferredWidth = 300f;

        // KNOWN ISSUE: changing a layout property does not drop LayoutUtility's static memo - only a canvas rebuild (GameCanvas.RebuildIfDirty) calls InvalidateCache, so outside a rebuild the new value stays invisible until something else invalidates it. See docs/PLAN_10_DE_10.md Fase 6. (H-UI-5)
        Assert.Equal(100f, LayoutUtility.GetPreferredSize(go).X);

        LayoutUtility.InvalidateCache();
        Assert.Equal(300f, LayoutUtility.GetPreferredSize(go).X);
    }
}
