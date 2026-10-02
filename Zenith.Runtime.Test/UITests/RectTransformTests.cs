// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

public class RectTransformTests
{
    [Fact]
    public void ComputeRect_FixedAnchors_CentersRectOnAnchorPoint()
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;

        Rect r = rt.ComputeRect(new Rect(0f, 0f, 1920f, 1080f));

        // Defaults: anchors (0.5,0.5), SizeDelta (100,100), pivot 0.5 -> 100px box centred at 960,540.
        UITestHelpers.AssertRect(r, 910f, 490f, 1010f, 590f);
        UITestHelpers.AssertRect(rt.ComputedRect, 910f, 490f, 1010f, 590f);
    }

    [Fact]
    public void ComputeRect_StretchedAnchors_PadAnchorSpanBySizeDelta()
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;
        rt.AnchorMin = new Float2(0f, 0f);
        rt.AnchorMax = new Float2(1f, 1f);
        rt.SizeDelta = new Float2(-20f, -30f);

        Rect r = rt.ComputeRect(new Rect(0f, 0f, 1920f, 1080f));

        // SizeDelta pads the full anchor span, split by the 0.5 pivot: 10px left/bottom, 10/15px right/top.
        UITestHelpers.AssertRect(r, 10f, 15f, 1910f, 1065f);
    }

    [Fact]
    public void ComputeRect_AppliesAnchoredPositionAndPivot()
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;
        rt.AnchoredPosition = new Float2(50f, 25f);
        rt.Pivot = new Float2(0f, 1f);

        Rect r = rt.ComputeRect(new Rect(0f, 0f, 1920f, 1080f));

        UITestHelpers.AssertRect(r, 1010f, 465f, 1110f, 565f);
    }

    [Theory]
    [InlineData(true, -150f, -80f, 250f, 130f, -25f, -15f, -150f, -80f)]
    [InlineData(false, 150f, 80f, 250f, 130f, 25f, 15f, 150f, 80f)]
    public void OffsetSetters_MoveThatCornerAndResize(
        bool setMin,
        float setX, float setY,
        float expSizeDeltaX, float expSizeDeltaY,
        float expAnchoredX, float expAnchoredY,
        float expReadX, float expReadY)
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;
        rt.SizeDelta = new Float2(200f, 100f); // pivot 0.5 => OffsetMin (-100,-50), OffsetMax (100,50)

        if (setMin)
            rt.OffsetMin = new Float2(setX, setY);
        else
            rt.OffsetMax = new Float2(setX, setY);

        Assert.Equal(expSizeDeltaX, rt.SizeDelta.X);
        Assert.Equal(expSizeDeltaY, rt.SizeDelta.Y);
        Assert.Equal(expAnchoredX, rt.AnchoredPosition.X);
        Assert.Equal(expAnchoredY, rt.AnchoredPosition.Y);
        Float2 read = setMin ? rt.OffsetMin : rt.OffsetMax;
        Assert.Equal(expReadX, read.X);
        Assert.Equal(expReadY, read.Y);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f, 200f, 100f)]
    [InlineData(1f, 1f, -200f, -100f, 0f, 0f)]
    public void Rect_IsPivotCenteredInOwnSpace(
        float pivotX, float pivotY,
        float expMinX, float expMinY, float expMaxX, float expMaxY)
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;
        rt.ComputedRect = new Rect(0f, 0f, 200f, 100f);
        rt.Pivot = new Float2(pivotX, pivotY);

        UITestHelpers.AssertRect(rt.Rect, expMinX, expMinY, expMaxX, expMaxY);
    }

    [Theory]
    [InlineData(RectTransform.Edge.Left, 20f, 300f, 0f, 170f)]
    [InlineData(RectTransform.Edge.Right, 20f, 300f, 1f, -170f)]
    [InlineData(RectTransform.Edge.Top, 15f, 80f, 1f, -55f)]
    [InlineData(RectTransform.Edge.Bottom, 15f, 80f, 0f, 55f)]
    public void SetInsetAndSizeFromParentEdge_CollapsesAnchorsOnThatEdge(
        RectTransform.Edge edge, float inset, float size, float expAnchor, float expAnchored)
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;

        rt.SetInsetAndSizeFromParentEdge(edge, inset, size);

        bool horizontal = edge is RectTransform.Edge.Left or RectTransform.Edge.Right;
        if (horizontal)
        {
            Assert.Equal(expAnchor, rt.AnchorMin.X);
            Assert.Equal(expAnchor, rt.AnchorMax.X);
            Assert.Equal(size, rt.SizeDelta.X);
            Assert.Equal(expAnchored, rt.AnchoredPosition.X);
            Assert.Equal(0.5f, rt.AnchorMin.Y); // other axis keeps its defaults
            Assert.Equal(100f, rt.SizeDelta.Y);
            Assert.Equal(0f, rt.AnchoredPosition.Y);
        }
        else
        {
            Assert.Equal(expAnchor, rt.AnchorMin.Y);
            Assert.Equal(expAnchor, rt.AnchorMax.Y);
            Assert.Equal(size, rt.SizeDelta.Y);
            Assert.Equal(expAnchored, rt.AnchoredPosition.Y);
            Assert.Equal(0.5f, rt.AnchorMin.X);
            Assert.Equal(100f, rt.SizeDelta.X);
            Assert.Equal(0f, rt.AnchoredPosition.X);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetSizeWithCurrentAnchors_SolvesSizeDeltaAgainstAnchorSpan(bool stretched)
    {
        using var ui = new UITestHelpers();
        if (stretched)
        {
            GameObject parent = ui.NewRect("Parent");
            parent.RectTransform!.ComputedRect = new Rect(0f, 0f, 1920f, 1080f);
            GameObject child = ui.AddChild(parent, "Child");
            RectTransform rt = child.RectTransform!;
            rt.AnchorMin = new Float2(0f, 0f);
            rt.AnchorMax = new Float2(1f, 1f);

            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 500f);

            // Parent span is 1920, so SizeDelta = 500 - 1920.
            Assert.Equal(-1420f, rt.SizeDelta.X);
            Rect r = rt.ComputeRect(new Rect(0f, 0f, 1920f, 1080f));
            Assert.Equal(500f, r.Size.X);
        }
        else
        {
            RectTransform rt = ui.NewRect().RectTransform!;

            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 250f);

            Assert.Equal(250f, rt.SizeDelta.X);
        }
    }

    [Fact]
    public void GetLocalCorners_ReturnsCornersClockwiseFromBottomLeft()
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;
        rt.ComputedRect = new Rect(0f, 0f, 200f, 100f); // pivot 0.5 => own-space rect (-100,-50)..(100,50)

        var corners = new Float3[4];
        rt.GetLocalCorners(corners);

        Assert.Equal(new Float3(-100f, -50f, 0f), corners[0]);
        Assert.Equal(new Float3(-100f, 50f, 0f), corners[1]);
        Assert.Equal(new Float3(100f, 50f, 0f), corners[2]);
        Assert.Equal(new Float3(100f, -50f, 0f), corners[3]);
    }

    [Fact]
    public void CornerAccessors_InvalidInputOrMissingCanvas_LeaveArrayUntouched()
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;
        rt.ComputedRect = new Rect(0f, 0f, 200f, 100f);

        rt.GetLocalCorners(null!);
        rt.GetLocalCorners(new Float3[2]);

        var sentinel = new[]
        {
            new Float3(1f, 2f, 3f),
            new Float3(4f, 5f, 6f),
            new Float3(7f, 8f, 9f),
            new Float3(10f, 11f, 12f),
        };
        rt.GetWorldCorners(sentinel); // no GameCanvas above this GameObject

        Assert.Equal(new Float3(1f, 2f, 3f), sentinel[0]);
        Assert.Equal(new Float3(4f, 5f, 6f), sentinel[1]);
        Assert.Equal(new Float3(7f, 8f, 9f), sentinel[2]);
        Assert.Equal(new Float3(10f, 11f, 12f), sentinel[3]);
    }

    [Fact]
    public void AnchoredPosition3D_KeepsZOnTheTransformAndXYOnTheRect()
    {
        using var ui = new UITestHelpers();
        RectTransform rt = ui.NewRect().RectTransform!;
        rt.LocalPosition = new Float3(1f, 2f, 3f);

        Float3 read = rt.AnchoredPosition3D;
        Assert.Equal(0f, read.X); // AnchoredPosition is still (0,0)
        Assert.Equal(0f, read.Y);
        Assert.Equal(3f, read.Z); // Z comes from the Transform

        rt.AnchoredPosition3D = new Float3(7f, 9f, 5f);

        Assert.Equal(7f, rt.AnchoredPosition.X);
        Assert.Equal(9f, rt.AnchoredPosition.Y);
        Assert.Equal(1f, rt.LocalPosition.X); // X/Y of the Transform are layout-owned
        Assert.Equal(2f, rt.LocalPosition.Y);
        Assert.Equal(5f, rt.LocalPosition.Z);
        Assert.Equal(5f, rt.AnchoredPosition3D.Z);
    }
}
