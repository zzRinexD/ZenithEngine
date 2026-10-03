// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The Hierarchy panel's structural decisions, tested without a window: which objects the tree model
/// keeps and at what depth, which selected objects a structural command should skip, and where a drop
/// actually puts them.
/// <para/>
/// Nothing here calls OnGUI. The panel is a DockPanel with no constructor of its own, so
/// <c>new HierarchyPanel()</c> is free, and OnGUI returns immediately headless anyway (it bails on
/// EditorTheme.DefaultFont being null). The context menus and the tree widget itself need a live Paper
/// frame, so what is left is the node-list builder and the drop math, which take their inputs as
/// arguments.
/// </summary>
public class HierarchyPanelTests : EditorTestHarness, IDisposable
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    private readonly HierarchyPanel _panel;
    private readonly EditorTestStatics.Scope _statics;

    public HierarchyPanelTests()
    {
        // Selection, the undo stack, the drag payload and the rename overlay are process-wide, so they
        // are cleared before and after every test in this class.
        _statics = new EditorTestStatics.Scope();
        _panel = new HierarchyPanel();
    }

    public void Dispose()
    {
        _statics.Dispose();
        GC.SuppressFinalize(this);
    }

    // ================================================================
    //  Reflection into the panel
    // ================================================================
    //  Every accessor below names the field or method it reaches with the line it sits on, so a rename in
    //  the panel surfaces as a MissingFieldException/MissingMethodException here rather than as a
    //  silently skipped assertion. Pattern follows ScriptCompilationTests (GetMethod with
    //  BindingFlags.NonPublic|Static). DropPosition is a private nested enum, so it is parsed by name.

    /// <summary>Private field HierarchyPanel._searchText (line 52): the tree filter.</summary>
    private string SearchText
    {
        get => (string)GetField("_searchText");
        set => SetField("_searchText", value);
    }

    /// <summary>Private field HierarchyPanel._forceExpandedIds: node ids forced open by a ping (line 61).</summary>
    private HashSet<string> ForceExpandedIds => (HashSet<string>)GetField("_forceExpandedIds");

    /// <summary>Private field HierarchyPanel._expandState: the per-node expanded flags (line 61).</summary>
    private Dictionary<string, bool> ExpandState => (Dictionary<string, bool>)GetField("_expandState");

    /// <summary>Private static method HierarchyPanel.IsDescendantOf(GameObject, GameObject) (line 698).</summary>
    private bool IsDescendantOf(GameObject child, GameObject parent) =>
        (bool)CallStatic("IsDescendantOf", child, parent)!;

    /// <summary>Private instance method HierarchyPanel.GetDisplayRoots(Scene) (line 1198).</summary>
    private List<GameObject> GetDisplayRoots(Scene scene) =>
        (List<GameObject>)CallInstance("GetDisplayRoots", scene)!;

    /// <summary>Private instance method HierarchyPanel.IsTargetExpanded(GameObject, string) (line 567).</summary>
    private bool IsTargetExpanded(GameObject target) =>
        (bool)CallInstance("IsTargetExpanded", target, target.Identifier.ToString())!;

    /// <summary>Private instance method HierarchyPanel.BuildNodeList(GameObject, int, List&lt;TreeNode&gt;, List&lt;object&gt;) (line 512).</summary>
    private List<TreeNode> BuildNodeList(GameObject root)
    {
        List<TreeNode> nodes = [];
        List<object> flat = [];
        CallInstance("BuildNodeList", root, 0, nodes, flat);
        LastFlatObjects = flat;
        return nodes;
    }

    /// <summary>The flat object list the last BuildNodeList call filled, index-aligned with its nodes.</summary>
    private List<object> LastFlatObjects { get; set; } = [];

    /// <summary>Private instance method HierarchyPanel.ProcessGODropCore(List&lt;GameObject&gt;, GameObject, string, DropPosition, int) (line 585).</summary>
    private void DropOn(List<GameObject> dragged, GameObject target, string dropPosition, int insertIndex = -1) =>
        CallInstance("ProcessGODropCore", dragged, target, target.Identifier.ToString(),
            Enum.Parse(DropPositionType, dropPosition), insertIndex);

    /// <summary>Private nested enum HierarchyPanel.DropPosition { Into, Above, Below } (line 43).</summary>
    private static Type DropPositionType => typeof(HierarchyPanel).GetNestedType("DropPosition", BindingFlags.NonPublic)
        ?? throw new MissingMemberException(nameof(HierarchyPanel), "DropPosition");

    private object? GetField(string name) => Field(name).GetValue(_panel);

    private void SetField(string name, object? value) => Field(name).SetValue(_panel, value);

    private static FieldInfo Field(string name) =>
        typeof(HierarchyPanel).GetField(name, InstancePrivate | BindingFlags.Public)
        ?? throw new MissingFieldException(nameof(HierarchyPanel), name);

    private object? CallStatic(string name, params object?[] args) =>
        typeof(HierarchyPanel).GetMethod(name, StaticPrivate)!.Invoke(null, args);

    private object? CallInstance(string name, params object?[] args) =>
        typeof(HierarchyPanel).GetMethod(name, InstancePrivate)!.Invoke(_panel, args);

    // ================================================================
    //  Fixture helpers
    // ================================================================

    /// <summary>
    /// Puts the given roots into a fresh Scene and makes it current. Every test needs one: the drop math
    /// reads Scene.Current for root indices, and the undo records resolve objects through it. Only real
    /// roots go in - the scene derives its root list from the parent links, so adding a parented object
    /// as well would just be noise.
    /// </summary>
    private Scene LoadScene(params GameObject[] roots)
    {
        var scene = new Scene();
        foreach (GameObject root in roots)
            scene.Add(root);

        Scene.Load(scene);
        Scene.ProcessPendingLoad();
        return scene;
    }

    /// <summary>A GameObject with the named direct children. One level only: the names are siblings.</summary>
    private static GameObject ParentOf(string name, params string[] childNames)
    {
        var parent = new GameObject(name);
        foreach (string child in childNames)
            new GameObject(child).SetParent(parent);

        return parent;
    }

    /// <summary>
    /// A chain of one GameObject per name, each parented to the one before, and returns them outermost
    /// first. SetParent to the same parent again is a no-op, so the links have to be built in order.
    /// </summary>
    private static GameObject[] Chain(params string[] names)
    {
        var nodes = names.Select(n => new GameObject(n)).ToArray();
        for (int i = 1; i < nodes.Length; i++)
            nodes[i].SetParent(nodes[i - 1]);

        return nodes;
    }

    private static string[] ChildNames(GameObject parent) => parent.Children.Select(c => c.Name).ToArray();

    // ================================================================
    //  1-2. Excluding nested selections
    // ================================================================

    // A structural command (delete, reparent, pose) applied to an ancestor already cascades to its
    // descendants, so re-applying it to a separately-selected descendant either double-serializes it in
    // the undo record or flattens it out of its parent. Only the top of each selected branch may act.
    [Fact]
    public void ExcludeNestedSelections_KeepsOnlyTheTopOfEachSelectedBranch()
    {
        GameObject[] chain = Chain("Root", "Child", "GrandChild");
        GameObject root = chain[0], child = chain[1], grandChild = chain[2];
        var other = new GameObject("Other");

        // Parent and descendant together: only the parent may act.
        Assert.Equal(new[] { root }, HierarchyPanel.ExcludeNestedSelections([root, child]));

        // Three levels deep, handed over in reverse: still only the top.
        Assert.Equal(new[] { root }, HierarchyPanel.ExcludeNestedSelections([grandChild, child, root]));

        // A descendant whose own parent is not selected is not nested relative to the selection, so it
        // stays, and so does the unrelated object: the filter only looks inside the set it was given.
        Assert.Equal(new[] { child, other }, HierarchyPanel.ExcludeNestedSelections([child, other]));
    }

    // Objects that are not related at all are all kept, in the order they were handed over, and nothing
    // is duplicated: the result is what the caller is about to mutate.
    [Fact]
    public void ExcludeNestedSelections_KeepsDisjointObjectsInOrderAndNothingTwice()
    {
        var first = new GameObject("First");
        var second = new GameObject("Second");
        var parent = ParentOf("Parent", "Child");

        List<GameObject> kept = HierarchyPanel.ExcludeNestedSelections([second, parent, first, parent.Children[0]]);

        Assert.Equal(new[] { second, parent, first }, kept);
        Assert.Equal(kept.Count, kept.Distinct().Count());
    }

    // ================================================================
    //  3. Ancestry
    // ================================================================

    // Ancestry is transitive, and nothing is its own descendant: the walk starts at the parent and stops
    // when the chain runs out, so it answers for grandparents as readily as for parents.
    [Fact]
    public void IsDescendantOf_IsTransitiveAndNeverMatchesTheObjectItself()
    {
        GameObject[] chain = Chain("Root", "Child", "GrandChild");
        GameObject root = chain[0], child = chain[1], grandChild = chain[2];
        var unrelated = new GameObject("Unrelated");

        Assert.True(IsDescendantOf(child, root));
        Assert.True(IsDescendantOf(grandChild, root));
        Assert.True(IsDescendantOf(grandChild, child));

        Assert.False(IsDescendantOf(root, child));
        Assert.False(IsDescendantOf(root, grandChild));
        Assert.False(IsDescendantOf(unrelated, root));
        Assert.False(IsDescendantOf(root, root));
    }

    // ================================================================
    //  4-5. The node list
    // ================================================================

    // Objects hidden from the editor never reach the model, and neither does their subtree. Depth counts
    // from zero at the root passed in and rises by one per level, and the node list and the flat object
    // list are appended to in lockstep so one can index the other - which is what a click on row N does
    // when it looks up the object it is about to select.
    [Fact]
    public void BuildNodeList_SkipsHiddenNodes_AndKeepsDepthAndFlatObjectsInStep()
    {
        var visible = ParentOf("Visible", "ShownChild");
        var hiddenParent = ParentOf("HiddenParent", "BuriedChild");
        hiddenParent.HideFlags = HideFlags.Hide;

        // Hidden, but its parent is not: the parent survives and the child is pruned out of it.
        var hiddenChild = new GameObject("HiddenChild");
        hiddenChild.HideFlags = HideFlags.Hide;
        hiddenChild.SetParent(visible);

        LoadScene(visible, hiddenParent);

        List<TreeNode> nodes = BuildNodeList(visible);

        Assert.Equal(new[] { "Visible", "ShownChild" }, nodes.Select(n => n.Label));
        Assert.Equal(new[] { 0, 1 }, nodes.Select(n => n.Depth));
        Assert.Equal(new[] { "Visible", "ShownChild" }, LastFlatObjects.Cast<GameObject>().Select(g => g.Name));

        // A hidden node contributes nothing at all, not even itself: the walk returns before adding it, so
        // its whole subtree goes with it.
        Assert.Empty(BuildNodeList(hiddenParent));
    }

    // KNOWN ISSUE: the search keeps an object when it matches or when something below it matches, so an
    // ancestor of a match does reach the model. Nothing, though, marks that node as open: only a ping
    // sets OverrideExpanded (line 551), and the tree widget skips the children of a collapsed node
    // entirely. So the matches exist in the model and never get drawn, which reads as "the search found
    // nothing". See docs/PLAN_10_DE_10.md Fase 6. (H-ED-16)
    [Fact]
    public void BuildNodeList_SearchKeepsAncestorsOfMatches_ButDoesNotOpenThem()
    {
        GameObject[] chain = Chain("Root", "Middle", "Needle");
        GameObject needle = chain[2];

        SearchText = "Needle";
        List<TreeNode> nodes = BuildNodeList(chain[0]);

        // All three are in the model: the two ancestors are there because of the match at the bottom.
        Assert.Equal(new[] { "Root", "Middle", "Needle" }, nodes.Select(n => n.Label));
        Assert.DoesNotContain(nodes, n => n.OverrideExpanded == true);

        // The one thing that does set it is a ping, which is what shows the search path is the one
        // missing it: same nodes, same search, only the forced id differs.
        ForceExpandedIds.Add(needle.Identifier.ToString());
        List<TreeNode> pinged = BuildNodeList(chain[0]);

        Assert.Equal(new[] { "Root", "Middle", "Needle" }, pinged.Select(n => n.Label));
        Assert.Equal(true, pinged.Single(n => n.Label == "Needle").OverrideExpanded);
    }

    // ================================================================
    //  6. The first-child slot
    // ================================================================

    // The gap under an expanded node is drawn as its first child's slot, so a drop there has to mean
    // "into this node, at index 0". That needs both a child to put it in and the node to actually be
    // open, so neither condition alone is enough.
    [Fact]
    public void IsTargetExpanded_NeedsBothAChildAndAnOpenNode()
    {
        var parent = ParentOf("Parent", "Child");
        var leaf = new GameObject("Leaf");
        LoadScene(parent, leaf);

        // Nothing marked open yet.
        Assert.False(IsTargetExpanded(parent));

        // Marked open, and it has a child: the slot is being shown.
        ExpandState[parent.Identifier.ToString()] = true;
        Assert.True(IsTargetExpanded(parent));

        // Marked open but childless: no slot is drawn, so a drop below it has to stay a sibling drop.
        ExpandState[leaf.Identifier.ToString()] = true;
        Assert.False(IsTargetExpanded(leaf));
    }

    // ================================================================
    //  7. Roots
    // ================================================================

    // The tree lists the scene's roots minus the ones hidden from the editor, in scene order, and hands
    // back a filtered list rather than the scene's own.
    [Fact]
    public void GetDisplayRoots_SkipsHiddenRootsAndKeepsSceneOrder()
    {
        var first = new GameObject("First");
        var hidden = new GameObject("Hidden");
        hidden.HideFlags = HideFlags.HideAndDontSave;
        var last = new GameObject("Last");

        Scene scene = LoadScene(first, hidden, last);

        Assert.Equal(new[] { "First", "Last" }, GetDisplayRoots(scene).Select(g => g.Name));
    }

    // ================================================================
    //  8-9. Drop targets
    // ================================================================

    // Dropping onto a node reparents into it, appending in the order the payload lists. Dropping above or
    // below a sibling reorders among the children, adjusting for the dragged object being pulled out of
    // the list first, so it lands where the gap was rather than one slot short of it.
    [Fact]
    public void ProcessGODropCore_IntoAppendsInPayloadOrder_AndAboveBelowReorderSiblings()
    {
        GameObject target = ParentOf("Target", "A", "B", "C");
        var grand = new GameObject("Grand");
        target.SetParent(grand);

        GameObject[] source = Chain("Source", "X", "Y");
        LoadScene(grand, source[0]);

        DropOn([source[1], source[2]], target, "Into");
        Assert.Equal(new[] { "A", "B", "C", "X", "Y" }, ChildNames(target));

        // Below B (index 1): X is at index 3 and is pulled out of the list before being reinserted.
        DropOn([source[1]], target.Children[1], "Below");
        Assert.Equal(new[] { "A", "B", "X", "C", "Y" }, ChildNames(target));

        // Above B (now index 1): Y is already after it, so it moves up one.
        DropOn([source[2]], target.Children[1], "Above");
        Assert.Equal(new[] { "A", "Y", "B", "X", "C" }, ChildNames(target));
    }

    // KNOWN ISSUE: the gap under an expanded node is dropped as "into it at index 0", and the loop does
    // that one object at a time: SetParent appends to the end of the child list and SetSiblingIndex(0)
    // then pushes that object back to the front. Each object therefore lands in front of the one handled
    // before it, so the result is the reverse of the order they were dragged in - and the plain "Into"
    // case, which only appends, is the one that keeps the payload order.
    // See docs/PLAN_10_DE_10.md Fase 6. (H-ED-18)
    [Fact]
    public void ProcessGODropCore_DroppingAsFirstChild_ReversesTheOrderOfThePayload()
    {
        GameObject target = ParentOf("Target", "Existing");
        GameObject[] source = Chain("Source", "P", "Q");
        LoadScene(target, source[0]);

        // Appended in payload order...
        DropOn([source[1], source[2]], target, "Into");
        Assert.Equal(new[] { "Existing", "P", "Q" }, ChildNames(target));

        // ...but dropped into the first-child slot they come out the other way round, and land ahead of
        // the children the node already had.
        DropOn([source[1], source[2]], target, "Into", insertIndex: 0);
        Assert.Equal(new[] { "Q", "P", "Existing" }, ChildNames(target));
    }

    // ================================================================
    //  10. Undo, and the commands that share the panel's entry points
    // ================================================================

    // A reparent captures the old parent and sibling index before the move and restores both on undo,
    // which is why the record is taken per object rather than read back afterwards. Creation and deletion
    // go through the same internal entry points the menus use, so neither can skip the prefab guard.
    [Fact]
    public void ProcessGODropCore_UndoRestoresTheOldParentAndSiblingIndex()
    {
        var parent = ParentOf("Parent", "A");
        GameObject[] from = Chain("From", "Moved");
        LoadScene(parent, from[0]);

        DropOn([from[1]], parent, "Into", insertIndex: 0);
        Assert.Same(parent, from[1].Parent);
        Assert.Equal(new[] { "Moved", "A" }, ChildNames(parent));

        // Pending records are flushed as a group, the way the editor does at the end of a frame.
        Undo.IncrementGroup();
        Undo.PerformUndo();

        Assert.Same(from[0], from[1].Parent);
        Assert.Equal(new[] { "Moved" }, ChildNames(from[0]));

        Undo.PerformRedo();
        Assert.Same(parent, from[1].Parent);

        // The shared entry points: created under a parent, then removed again through the same method the
        // context menu and the scene viewport both call.
        GameObject created = HierarchyPanel.CreateGameObject("Created", parent, beginRename: false);
        Assert.Same(parent, created.Parent);
        Assert.Contains("Created", ChildNames(parent));

        HierarchyPanel.DeleteGameObject(created);
        Assert.DoesNotContain("Created", ChildNames(parent));
    }
}