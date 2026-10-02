// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.UI;
using Prowl.Vector;
using Xunit;

namespace Prowl.Runtime.Test.UITests;

public class GridLayoutGroupTests
{
    private static GameObject[] AddKids(UITestHelpers ui, GameObject group, int count)
    {
        var kids = new GameObject[count];
        for (int i = 0; i < count; i++)
            kids[i] = ui.AddChild(group, $"Kid{i}");
        return kids;
    }

    [Fact]
    public void Arrange_FixedColumnCount_FillsRowsTopDown()
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Grid");
        GridLayoutGroup layout = group.AddComponent<GridLayoutGroup>();
        layout.CellSize = new Float2(100f, 50f);
        layout.GridConstraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.ConstraintCount = 3;
        GameObject[] kids = AddKids(ui, group, 5);

        layout.Arrange(new Rect(0f, 0f, 1000f, 1000f));

        UITestHelpers.AssertRect(kids[0].RectTransform!.ComputedRect, 0f, 950f, 100f, 1000f);
        UITestHelpers.AssertRect(kids[1].RectTransform!.ComputedRect, 100f, 950f, 200f, 1000f);
        UITestHelpers.AssertRect(kids[3].RectTransform!.ComputedRect, 0f, 900f, 100f, 950f);
        UITestHelpers.AssertRect(kids[4].RectTransform!.ComputedRect, 100f, 900f, 200f, 950f);
    }

    [Fact]
    public void Arrange_FixedRowCount_DerivesColumnsFromRowCount()
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Grid");
        GridLayoutGroup layout = group.AddComponent<GridLayoutGroup>();
        layout.CellSize = new Float2(100f, 50f);
        layout.GridConstraint = GridLayoutGroup.Constraint.FixedRowCount;
        layout.ConstraintCount = 2;
        GameObject[] kids = AddKids(ui, group, 5);

        layout.Arrange(new Rect(0f, 0f, 1000f, 1000f));

        // ceil(5/2) = 3 columns, so the fill matches the FixedColumnCount=3 layout.
        UITestHelpers.AssertRect(kids[0].RectTransform!.ComputedRect, 0f, 950f, 100f, 1000f);
        UITestHelpers.AssertRect(kids[3].RectTransform!.ComputedRect, 0f, 900f, 100f, 950f);
        Assert.Equal(100f, kids[1].RectTransform!.ComputedRect.Min.X);
    }

    [Theory]
    [InlineData(350f, 200f, 0f)]
    [InlineData(250f, 0f, 100f)]
    public void Arrange_FlexibleConstraint_DerivesColumnsFromContentWidth(
        float rectWidth, float expKid2X, float expKid3X)
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Grid");
        GridLayoutGroup layout = group.AddComponent<GridLayoutGroup>();
        layout.CellSize = new Float2(100f, 50f); // Flexible is the default constraint
        GameObject[] kids = AddKids(ui, group, 4);

        layout.Arrange(new Rect(0f, 0f, rectWidth, 1000f));

        UITestHelpers.AssertRect(kids[0].RectTransform!.ComputedRect, 0f, 950f, 100f, 1000f);
        // 350px fits 3 columns of 100 (kid2 ends row 0, kid3 starts row 1); 250px fits only 2.
        Assert.Equal(expKid2X, kids[2].RectTransform!.ComputedRect.Min.X);
        Assert.Equal(expKid3X, kids[3].RectTransform!.ComputedRect.Min.X);
    }

    [Fact]
    public void ConstraintCount_IsClampedToAtLeastOneAndCenterAlignmentCentersGrid()
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Grid");
        GridLayoutGroup layout = group.AddComponent<GridLayoutGroup>();
        layout.CellSize = new Float2(100f, 50f);

        layout.ConstraintCount = 0;
        Assert.Equal(1, layout.ConstraintCount);

        layout.GridConstraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.ConstraintCount = 2;
        layout.ChildAlignment = TextAlignment.CenterMiddle;
        GameObject[] kids = AddKids(ui, group, 4);

        layout.Arrange(new Rect(0f, 0f, 400f, 400f));

        // 2x2 grid of 100x50 cells centred in 400x400: grid offset (100, 150) from the bottom.
        UITestHelpers.AssertRect(kids[0].RectTransform!.ComputedRect, 100f, 200f, 200f, 250f);
    }

    [Fact]
    public void ReportedSizes_AgreeWithArrangeIncludingPadding()
    {
        using var ui = new UITestHelpers();
        GameObject group = ui.NewRect("Grid");
        GridLayoutGroup layout = group.AddComponent<GridLayoutGroup>();
        layout.CellSize = new Float2(100f, 50f);
        layout.GridConstraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.ConstraintCount = 2;
        layout.PaddingLeft = 5;
        layout.PaddingRight = 7;
        layout.PaddingTop = 3;
        layout.PaddingBottom = 4;
        GameObject[] kids = AddKids(ui, group, 4);

        Assert.Equal(212f, layout.PreferredWidth);  // 5+7 padding + 2*100 cells
        Assert.Equal(212f, layout.MinWidth);
        Assert.Equal(107f, layout.PreferredHeight); // 3+4 padding + 2*50 rows

        layout.Arrange(new Rect(0f, 0f, 500f, 300f));

        UITestHelpers.AssertRect(kids[0].RectTransform!.ComputedRect, 5f, 247f, 105f, 297f);
    }
}
