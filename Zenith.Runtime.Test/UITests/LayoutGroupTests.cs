// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

public class LayoutGroupTests
{
    private static GameObject MakeChild(UITestHelpers ui, GameObject parent, string name)
    {
        GameObject child = ui.AddChild(parent, name);
        child.RectTransform!.SizeDelta = new Float2(100f, 50f);
        return child;
    }

    private static void SetMinPreferredAlong(GameObject go, bool vertical)
    {
        LayoutElement le = go.AddComponent<LayoutElement>();
        if (vertical)
        {
            le.MinHeight = 40f;
            le.PreferredHeight = 300f;
        }
        else
        {
            le.MinWidth = 40f;
            le.PreferredWidth = 300f;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Arrange_SkipsChildrenNotTakingPart(bool ignoreLayout)
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();
        GameObject c1 = MakeChild(ui, group, "C1");
        GameObject c2 = MakeChild(ui, group, "C2");
        GameObject c3 = MakeChild(ui, group, "C3");

        if (ignoreLayout)
            c2.AddComponent<LayoutElement>().IgnoreLayout = true;
        else
            c2.Enabled = false;

        layout.Arrange(new Rect(0f, 0f, 600f, 100f));

        UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, 0f, 100f, 100f);
        UITestHelpers.AssertRect(c3.RectTransform!.ComputedRect, 100f, 0f, 200f, 100f);
        UITestHelpers.AssertRect(c2.RectTransform!.ComputedRect, 0f, 0f, 0f, 0f); // untouched
    }

    [Fact]
    public void Arrange_Padding_ShrinksTheContentArea()
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();
        layout.PaddingLeft = 10;
        layout.PaddingRight = 20;
        layout.PaddingTop = 10;
        layout.PaddingBottom = 30;
        GameObject c1 = MakeChild(ui, group, "C1");
        GameObject c2 = MakeChild(ui, group, "C2");

        layout.Arrange(new Rect(0f, 0f, 500f, 100f));

        UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 10f, 30f, 110f, 90f);
        UITestHelpers.AssertRect(c2.RectTransform!.ComputedRect, 110f, 30f, 210f, 90f);
    }

    [Fact]
    public void Arrange_VerticalGroup_FillsTopDownWithSpacing()
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        VerticalLayoutGroup layout = group.AddComponent<VerticalLayoutGroup>();
        layout.Spacing = 5f;
        GameObject c1 = MakeChild(ui, group, "C1");
        GameObject c2 = MakeChild(ui, group, "C2");
        GameObject c3 = MakeChild(ui, group, "C3");

        layout.Arrange(new Rect(0f, 0f, 300f, 500f));

        UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, 450f, 300f, 500f);
        UITestHelpers.AssertRect(c2.RectTransform!.ComputedRect, 0f, 395f, 300f, 445f);
        UITestHelpers.AssertRect(c3.RectTransform!.ComputedRect, 0f, 340f, 300f, 390f);
        Assert.Equal(5f, c1.RectTransform!.ComputedRect.Min.Y - c2.RectTransform!.ComputedRect.Max.Y);
    }

    [Theory]
    [InlineData(TextAlignment.Left, 0f)]
    [InlineData(TextAlignment.Middle, 100f)]
    [InlineData(TextAlignment.Right, 200f)]
    public void Arrange_WhenNotControllingWidth_AlignsAcross(TextAlignment alignment, float expectedX)
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        VerticalLayoutGroup layout = group.AddComponent<VerticalLayoutGroup>();
        layout.ChildControlWidth = false;
        layout.ChildAlignment = alignment;
        GameObject c1 = MakeChild(ui, group, "C1");

        layout.Arrange(new Rect(0f, 0f, 300f, 500f));

        UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, expectedX, 450f, expectedX + 100f, 500f);
    }

    [Theory]
    [InlineData(TextAlignment.Bottom, 0f)]
    [InlineData(TextAlignment.Center, 25f)]
    [InlineData(TextAlignment.Top, 50f)]
    public void Arrange_WhenNotControllingHeight_AlignsAcross(TextAlignment alignment, float expectedY)
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();
        layout.ChildControlHeight = false;
        layout.ChildAlignment = alignment;
        GameObject c1 = MakeChild(ui, group, "C1");

        layout.Arrange(new Rect(0f, 0f, 600f, 100f));

        UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, expectedY, 100f, expectedY + 50f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arrange_ChildForceExpand_SplitsLeftoverSpace(bool vertical)
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        if (vertical)
        {
            VerticalLayoutGroup layout = group.AddComponent<VerticalLayoutGroup>();
            layout.ChildForceExpandHeight = true;
            GameObject c1 = MakeChild(ui, group, "C1");
            GameObject c2 = MakeChild(ui, group, "C2");

            layout.Arrange(new Rect(0f, 0f, 300f, 500f));

            UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, 250f, 300f, 500f);
            UITestHelpers.AssertRect(c2.RectTransform!.ComputedRect, 0f, 0f, 300f, 250f);
        }
        else
        {
            HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();
            layout.ChildForceExpandWidth = true;
            GameObject c1 = MakeChild(ui, group, "C1");
            GameObject c2 = MakeChild(ui, group, "C2");

            layout.Arrange(new Rect(0f, 0f, 600f, 100f));

            UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, 0f, 300f, 100f);
            UITestHelpers.AssertRect(c2.RectTransform!.ComputedRect, 300f, 0f, 600f, 100f);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arrange_ShrunkChildren_StackAtMinSizes(bool vertical)
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        if (vertical)
        {
            VerticalLayoutGroup layout = group.AddComponent<VerticalLayoutGroup>();
            GameObject c1 = MakeChild(ui, group, "C1");
            GameObject c2 = MakeChild(ui, group, "C2");
            SetMinPreferredAlong(c1, vertical: true);
            SetMinPreferredAlong(c2, vertical: true);

            layout.Arrange(new Rect(0f, 0f, 300f, 300f));

            UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, 150f, 300f, 300f);
            UITestHelpers.AssertRect(c2.RectTransform!.ComputedRect, 0f, 0f, 300f, 150f);
        }
        else
        {
            HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();
            GameObject c1 = MakeChild(ui, group, "C1");
            GameObject c2 = MakeChild(ui, group, "C2");
            SetMinPreferredAlong(c1, vertical: false);
            SetMinPreferredAlong(c2, vertical: false);

            layout.Arrange(new Rect(0f, 0f, 300f, 100f));

            UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, 0f, 150f, 100f);
            UITestHelpers.AssertRect(c2.RectTransform!.ComputedRect, 150f, 0f, 300f, 100f);
        }
    }

    [Theory]
    [InlineData(322f, 87f, 132f, 57f, false)]
    [InlineData(212f, 137f, 72f, 117f, true)]
    public void ReportedSizes_IncludeSpacingAndPadding(
        float expPreferredW, float expPreferredH, float expMinW, float expMinH, bool vertical)
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        HorizontalOrVerticalLayoutGroup layout;
        if (vertical)
        {
            VerticalLayoutGroup v = group.AddComponent<VerticalLayoutGroup>();
            v.Spacing = 10f;
            v.PaddingLeft = 5;
            v.PaddingRight = 7;
            v.PaddingTop = 3;
            v.PaddingBottom = 4;
            layout = v;
        }
        else
        {
            HorizontalLayoutGroup h = group.AddComponent<HorizontalLayoutGroup>();
            h.Spacing = 10f;
            h.PaddingLeft = 5;
            h.PaddingRight = 7;
            h.PaddingTop = 3;
            h.PaddingBottom = 4;
            layout = h;
        }

        GameObject k1 = ui.AddChild(group, "K1");
        k1.RectTransform!.SizeDelta = new Float2(50f, 50f);
        LayoutElement l1 = k1.AddComponent<LayoutElement>();
        l1.PreferredWidth = 100f;
        l1.PreferredHeight = 40f;
        l1.MinWidth = 60f;

        GameObject k2 = ui.AddChild(group, "K2");
        k2.RectTransform!.SizeDelta = new Float2(50f, 50f);
        LayoutElement l2 = k2.AddComponent<LayoutElement>();
        l2.PreferredWidth = 200f;
        l2.PreferredHeight = 80f;

        Assert.Equal(expPreferredW, layout.PreferredWidth);
        Assert.Equal(expPreferredH, layout.PreferredHeight);
        Assert.Equal(expMinW, layout.MinWidth);
        Assert.Equal(expMinH, layout.MinHeight);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arrange_WhenNotControllingAlongAxis_KeepsPreferredSize(bool vertical)
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        if (vertical)
        {
            VerticalLayoutGroup layout = group.AddComponent<VerticalLayoutGroup>();
            layout.ChildControlWidth = false;
            GameObject c1 = MakeChild(ui, group, "C1");

            layout.Arrange(new Rect(0f, 0f, 300f, 500f));

            UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, 450f, 100f, 500f);
        }
        else
        {
            HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();
            layout.ChildControlHeight = false;
            GameObject c1 = MakeChild(ui, group, "C1");

            layout.Arrange(new Rect(0f, 0f, 600f, 100f));

            UITestHelpers.AssertRect(c1.RectTransform!.ComputedRect, 0f, 50f, 100f, 100f);
        }
    }

    [Fact]
    public void Arrange_WithoutChildren_DoesNothing()
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Group");
        HorizontalLayoutGroup layout = group.AddComponent<HorizontalLayoutGroup>();

        layout.Arrange(new Rect(0f, 0f, 100f, 100f));

        Assert.Empty(group.Children);
    }
}
