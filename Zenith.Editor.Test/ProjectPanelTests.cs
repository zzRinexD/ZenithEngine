// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;
using System.Reflection;

using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.OrigamiUI;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The Project panel's decision logic, tested without a window: which items the content view holds and
/// in what order, how a name is displayed, whether a drop target is legal, and how the panel's own
/// cache treats those inputs.
/// <para/>
/// Nothing here calls OnGUI. The panel is a DockPanel with no constructor of its own, so
/// <c>new ProjectPanel()</c> is free, and OnGUI returns immediately headless anyway (it bails out on
/// EditorTheme.DefaultFont being null). That leaves the model-building methods, which either take their
/// database as a parameter or read plain fields, reachable.
/// <para/>
/// The folder *tree* builder is deliberately not called: it paints each node with EditorTheme colours,
/// whose getter builds an Origami theme and loads fonts, and fonts are exactly what the headless
/// harness exists to avoid.
/// </summary>
public class ProjectPanelTests : EditorTestHarness, IDisposable
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    private readonly ProjectPanel _panel;
    private readonly EditorTestStatics.Scope _statics;

    public ProjectPanelTests()
    {
        // The editor's selection, undo stack, drag payload and rename overlay are all process-wide, so
        // they are cleared before and after every test in this class.
        _statics = new EditorTestStatics.Scope();
        _panel = new ProjectPanel();
    }

    public void Dispose()
    {
        _statics.Dispose();
        GC.SuppressFinalize(this);
    }

    // ================================================================
    //  Reflection into the panel
    // ================================================================
    //  Every accessor below names the field or method it reaches, so a rename in the panel surfaces as
    //  a MissingFieldException/MissingMethodException here rather than as a silently skipped assertion.
    //  Pattern follows ScriptCompilationTests (GetMethod("Open", BindingFlags.NonPublic|Static)).

    /// <summary>Private field ProjectPanel._currentFolder: the folder the content view shows.</summary>
    private string CurrentFolder
    {
        get => (string)GetField("_currentFolder");
        set => SetField("_currentFolder", value);
    }

    /// <summary>Private field ProjectPanel._searchText: the content filter.</summary>
    private string SearchText
    {
        get => (string)GetField("_searchText");
        set => SetField("_searchText", value);
    }

    /// <summary>Private field ProjectPanel._showHidden: include dot-prefixed items.</summary>
    private bool ShowHidden
    {
        get => (bool)GetField("_showHidden");
        set => SetField("_showHidden", value);
    }

    /// <summary>Private field ProjectPanel._showExtensions: labels keep their extension.</summary>
    private bool ShowExtensions
    {
        get => (bool)GetField("_showExtensions");
        set => SetField("_showExtensions", value);
    }

    /// <summary>Private field ProjectPanel._groupByType.</summary>
    private bool GroupByType
    {
        get => (bool)GetField("_groupByType");
        set => SetField("_groupByType", value);
    }

    /// <summary>Private field ProjectPanel._contentCache: the memoised content view.</summary>
    private List<ContentItem>? ContentCache => (List<ContentItem>?)GetField("_contentCache");

    /// <summary>Writes private field ProjectPanel._sortBy, typed by the private nested enum SortMode.</summary>
    private void SetSortBy(string sortModeName)
    {
        Type sortMode = typeof(ProjectPanel).GetNestedType("SortMode", BindingFlags.NonPublic)
            ?? throw new MissingMemberException(nameof(ProjectPanel), "SortMode");
        SetField("_sortBy", Enum.Parse(sortMode, sortModeName));
    }

    /// <summary>Private field ProjectPanel._navBack: the folders behind the current one.</summary>
    private Stack<string> NavBack => (Stack<string>)GetField("_navBack");

    /// <summary>Private field ProjectPanel._navForward.</summary>
    private Stack<string> NavForward => (Stack<string>)GetField("_navForward");

    /// <summary>Private static method ProjectPanel.FormatSize(long).</summary>
    private string FormatSize(long bytes) => (string)CallStatic("FormatSize", bytes)!;

    /// <summary>Private static method ProjectPanel.CanRename(ContentItem).</summary>
    private bool CanRename(ContentItem item) => (bool)CallStatic("CanRename", item)!;

    /// <summary>Private static method ProjectPanel.CanAcceptAssetDropInto(string).</summary>
    private bool CanAcceptAssetDropInto(string destination) => (bool)CallStatic("CanAcceptAssetDropInto", destination)!;

    /// <summary>Private static method ProjectPanel.IsFolderEmpty(string).</summary>
    private bool IsFolderEmpty(string relativePath) => (bool)CallStatic("IsFolderEmpty", relativePath)!;

    /// <summary>Private instance method ProjectPanel.DisplayName(ContentItem).</summary>
    private string DisplayName(ContentItem item) => (string)CallInstance("DisplayName", item)!;

    /// <summary>Private instance method ProjectPanel.BuildContentEntries(EditorAssetBackend).</summary>
    private List<ContentItem> BuildContentEntries() => (List<ContentItem>)CallInstance("BuildContentEntries", Assets)!;

    /// <summary>Private instance method ProjectPanel.GetContentEntries(EditorAssetBackend), behind the cache.</summary>
    private List<ContentItem> GetContentEntries() => (List<ContentItem>)CallInstance("GetContentEntries", Assets)!;

    /// <summary>Private instance method ProjectPanel.NavBack() / NavForward().</summary>
    private void CallNav(string name) => CallInstance(name);

    private object? GetField(string name) => Field(name).GetValue(_panel);

    private void SetField(string name, object? value) => Field(name).SetValue(_panel, value);

    private static FieldInfo Field(string name) =>
        typeof(ProjectPanel).GetField(name, InstancePrivate | BindingFlags.Public)
        ?? throw new MissingFieldException(nameof(ProjectPanel), name);

    private object? CallStatic(string name, params object?[] args) =>
        typeof(ProjectPanel).GetMethod(name, StaticPrivate)!.Invoke(null, args);

    private object? CallInstance(string name, params object?[] args) =>
        typeof(ProjectPanel).GetMethod(name, InstancePrivate)!.Invoke(_panel, args);

    // ================================================================
    //  Fixture helpers
    // ================================================================

    /// <summary>
    /// Writes text files with a chosen size and last-write time, then reindexes. .txt goes through
    /// DefaultImporter, which resolves to no runtime asset, so nothing here touches graphics. Sizes and
    /// times are pinned because the Size and Modified sorts are only meaningful when they differ.
    /// </summary>
    private void WriteFile(string relativePath, int sizeBytes, DateTime lastWriteUtc)
    {
        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(abs, lastWriteUtc);
        Assets.Refresh();
    }

    /// <summary>Creates directories and reindexes.</summary>
    private void WriteFolders(params string[] relativePaths)
    {
        foreach (string relative in relativePaths)
            Directory.CreateDirectory(AssetAbsolutePath(relative));
        Assets.Refresh();
    }

    /// <summary>A fixed point in time, so modified-time ordering does not depend on the clock.</summary>
    private static DateTime Utc(int minute) => new(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc);

    private static ContentItem Item(string name, bool folder = false, bool subAsset = false) =>
        new() { Name = name, RelativePath = name, IsFolder = folder, IsSubAsset = subAsset };

    private static string[] Names(IEnumerable<ContentItem> items) => items.Select(i => i.Name).ToArray();

    private void StartAssetDrag(params string[] assetPaths)
    {
        // AssetDragPayload(guid, name, type, allGuids, allPaths): the panel hands over the full
        // selection so a multi-asset drag knows what else it is carrying, and that allPaths list is
        // what CanAcceptAssetDropInto walks.
        Guid[] guids = assetPaths.Select(_ => Guid.NewGuid()).ToArray();
        DragDrop.StartDrag(new AssetDragPayload(guids[0], "dragged", null, guids, assetPaths));
    }

    // ================================================================
    //  1. ContentItem identity
    // ================================================================

    // A content item is identified by its asset GUID *and* its relative path. The panel keeps
    // ContentItems in the global Selection across frames, so if identity ignored the path, a file that
    // was renamed or dragged elsewhere would still compare equal to the row now occupying its old path.
    // With no GUID (a virtual or not-yet-imported row) the path has to carry the hash on its own.
    [Fact]
    public void ContentItem_Identity_IsGuidPlusRelativePath()
    {
        Guid guid = Guid.NewGuid();
        var same = new ContentItem { Name = "a.txt", RelativePath = "dir/a.txt", Guid = guid };
        var renamedButSamePath = new ContentItem { Name = "b.txt", RelativePath = "dir/a.txt", Guid = guid };
        var movedToAnotherPath = new ContentItem { Name = "a.txt", RelativePath = "elsewhere/a.txt", Guid = guid };

        Assert.Equal(same, renamedButSamePath);
        Assert.Equal(same.GetHashCode(), renamedButSamePath.GetHashCode());
        Assert.NotEqual(same, movedToAnotherPath);

        var noGuid = new ContentItem { Name = "ghost", RelativePath = "dir/ghost.txt", Guid = Guid.Empty };
        var noGuidTwin = new ContentItem { Name = "ghost", RelativePath = "dir/ghost.txt", Guid = Guid.Empty };
        Assert.Equal(noGuid, noGuidTwin);
        Assert.Equal(noGuid.RelativePath.GetHashCode(), noGuid.GetHashCode());
    }

    // ================================================================
    //  2. File size formatting
    // ================================================================

    // The footer prints asset sizes with no GPU and no I/O. Below 1 KB it prints whole bytes; above, one
    // decimal. The unit ladder stops at GB, so a terabyte keeps dividing but must stay labelled GB
    // rather than fall off the end of the array or print an empty unit.
    [Fact]
    public void FormatSize_WholeBytesBelowAKbOneDecimalAboveAndStopsAtGigabytes()
    {
        Assert.Equal("-", FormatSize(0));
        Assert.Equal("-", FormatSize(-1));
        Assert.Equal("1 B", FormatSize(1));
        Assert.Equal("1023 B", FormatSize(1023));
        Assert.Equal("1 KB", FormatSize(1024));
        Assert.Equal("1.5 KB", FormatSize(1536));
        Assert.Equal("1 MB", FormatSize(1024L * 1024));
        Assert.Equal("1.5 MB", FormatSize(1024L * 1024 * 3 / 2));
        Assert.Equal("1024 GB", FormatSize(1024L * 1024 * 1024 * 1024));
    }

    // KNOWN ISSUE: the one-decimal branch formats with "0.#" and no IFormatProvider, so the separator is
    // whatever CurrentCulture says. The same byte count prints "1.5 KB" in an en-US editor and "1,5 KB"
    // in a comma-decimal one. See docs/PLAN_10_DE_10.md Fase 6. (H-ED-43)
    [Fact]
    public void FormatSize_OneDecimalDigits_FollowTheCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            Assert.Equal("1,5 KB", FormatSize(1536));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ================================================================
    //  3-4. Display names
    // ================================================================

    // With "Show Extensions" off a file is labelled by its stem, but a folder has no extension to hide
    // and a sub-asset lives inside its parent's file, so both keep their full name.
    [Fact]
    public void DisplayName_HidesTheExtension_ForFilesOnly()
    {
        ShowExtensions = false;

        Assert.Equal("scene", DisplayName(Item("scene.scene")));
        Assert.Equal("Materials", DisplayName(Item("Materials", folder: true)));
        Assert.Equal("Albedo", DisplayName(Item("Albedo", subAsset: true)));

        ShowExtensions = true;
        Assert.Equal("scene.scene", DisplayName(Item("scene.scene")));
    }

    // KNOWN ISSUE: the "hide extensions" rule is Path.GetFileNameWithoutExtension, which has nothing to
    // return for a name that is *entirely* an extension: ".gitignore" comes back as the empty string. So
    // with hidden files shown and extensions hidden, that row renders blank. Reachable only in that
    // combination, since dotfiles are filtered out of the content view otherwise.
    // See docs/PLAN_10_DE_10.md Fase 6. (H-ED-11)
    [Fact]
    public void DisplayName_ADotfileWithExtensionsHidden_IsEmpty()
    {
        ShowExtensions = false;

        Assert.Equal("", DisplayName(Item(".gitignore")));
    }

    // ================================================================
    //  5. Rename rules
    // ================================================================

    // Renaming a script would break the type name inside it, so the menu entry is disabled for .cs.
    // Folders have no such coupling and everything else is fair game.
    [Fact]
    public void CanRename_RefusesScriptsAllowsFoldersAndOtherAssets()
    {
        Assert.False(CanRename(Item("Player.cs")));
        Assert.True(CanRename(Item("Materials", folder: true)));
        Assert.True(CanRename(Item("scene.scene")));
    }

    // ================================================================
    //  6. Folder navigation history
    // ================================================================

    // Browsing forward drops whatever was ahead: the history is a linear trail, not a tree of branches.
    // Each step records the folder it came *from*, so the very first navigation already puts the root on
    // the back stack, which is what makes Back from the first folder return to the root rather than
    // nowhere. Navigating to the folder already shown is not a step at all, or a repeated click would
    // fill the trail with the same folder and Back would appear to do nothing. Back and Forward walk the
    // trail and swap entries between the two stacks; on an empty trail both are no-ops rather than
    // throwing, because the toolbar enables the buttons from the stack counts but the shortcut and
    // drop-frame paths can still land here.
    // <para/>
    // Both stacks are asserted top-first, which is the order Stack.ToArray hands them back.
    [Fact]
    public void NavigateTo_PushesTheFolderItCameFromAndClearsTheForwardTrail()
    {
        _panel.NavigateTo("Materials");
        Assert.Equal(new[] { "" }, NavBack.ToArray());
        Assert.Equal("Materials", CurrentFolder);

        // Already there: not a step, or the trail would fill with the folder already shown.
        _panel.NavigateTo("Materials");
        Assert.Equal(new[] { "" }, NavBack.ToArray());

        _panel.NavigateTo("Materials/Textures");
        Assert.Equal(new[] { "Materials", "" }, NavBack.ToArray());

        _panel.NavigateTo("Props");
        Assert.Equal(new[] { "Materials/Textures", "Materials", "" }, NavBack.ToArray());
        Assert.Empty(NavForward);

        CallNav("NavBack");
        Assert.Equal("Materials/Textures", CurrentFolder);
        Assert.Equal(new[] { "Props" }, NavForward.ToArray());

        CallNav("NavBack");
        Assert.Equal("Materials", CurrentFolder);
        Assert.Equal(new[] { "Materials/Textures", "Props" }, NavForward.ToArray());

        // Forward retraces the trail in reverse, and re-arms the back trail with the folder it came from,
        // so the two steps behind it are on the back stack again.
        CallNav("NavForward");
        Assert.Equal("Materials/Textures", CurrentFolder);
        Assert.Equal(new[] { "Materials", "" }, NavBack.ToArray());
        Assert.Equal(new[] { "Props" }, NavForward.ToArray());

        CallNav("NavForward");
        Assert.Equal("Props", CurrentFolder);
        Assert.Equal(new[] { "Materials/Textures", "Materials", "" }, NavBack.ToArray());

        // Draining the trail and then asking again changes nothing instead of throwing.
        CallNav("NavForward");
        Assert.Equal("Props", CurrentFolder);

        CallNav("NavBack");
        Assert.Equal("Materials/Textures", CurrentFolder);

        CallNav("NavBack");
        Assert.Equal("Materials", CurrentFolder);

        CallNav("NavBack");
        Assert.Equal("", CurrentFolder);
        Assert.Empty(NavBack);

        CallNav("NavBack");
        Assert.Equal("", CurrentFolder);
        Assert.Empty(NavBack);
    }

    // ================================================================
    //  7, 8, 11, 12. Ordering, grouping, sub-assets and virtual rows
    // ================================================================

    // The default sort is by name, case-insensitively, and folders always come before files: a folder is
    // a container, not an entry of the same kind. Size and Modified are descending (biggest and newest
    // first) and never mix folders in, because folders are collected separately and prepended. "Group by
    // type" keeps the primary sort and adds the extension as the outer key, and is skipped when the
    // primary sort is already Type, where it would be redundant. Sub-assets belong to the file holding
    // them, so they sort and group as one unit and are never rows of their own. A create-in-progress
    // asset is a row held in memory rather than on disk, so it is interleaved with the real files.
    [Fact]
    public void ContentEntries_SortGroupSubAssetsAndVirtualRows()
    {
        WriteFile("beta.txt", 30, Utc(30));
        WriteFile("Alpha.txt", 10, Utc(10));
        WriteFile("Gamma.txt", 20, Utc(20));
        WriteFolders("zFolder", "AFolder");

        // By name, ignoring case: AFolder < zFolder < Alpha.txt < beta.txt < Gamma.txt.
        Assert.Equal(new[] { "AFolder", "zFolder", "Alpha.txt", "beta.txt", "Gamma.txt" },
            Names(BuildContentEntries()));

        // By size, descending: beta(30) > Gamma(20) > Alpha(10).
        SetSortBy("Size");
        Assert.Equal(new[] { "AFolder", "zFolder", "beta.txt", "Gamma.txt", "Alpha.txt" },
            Names(BuildContentEntries()));

        // By modification date, descending: beta(30) > Gamma(20) > Alpha(10).
        SetSortBy("Modified");
        Assert.Equal(new[] { "AFolder", "zFolder", "beta.txt", "Gamma.txt", "Alpha.txt" },
            Names(BuildContentEntries()));

        // Group by type makes the extension the outer key (ASSET before TXT) while keeping the primary
        // name order inside each group. A .asset has no importer here, so its label is the extension.
        SetSortBy("Name");
        GroupByType = true;
        WriteFile("first.txt", 5, Utc(40));
        WriteFile("second.asset", 5, Utc(40));
        Assert.Equal(
            new[] { "AFolder", "zFolder", "second.asset", "Alpha.txt", "beta.txt", "first.txt", "Gamma.txt" },
            Names(BuildContentEntries()));

        // Already sorting by type, so grouping is a no-op rather than a second pass on the same key.
        SetSortBy("Type");
        Assert.Equal(
            new[] { "AFolder", "zFolder", "second.asset", "Alpha.txt", "beta.txt", "first.txt", "Gamma.txt" },
            Names(BuildContentEntries()));

        // Nothing that belongs inside a file shows up as a row of its own. (Producing a real sub-asset
        // needs an importer this harness deliberately does not run, so the invariant is asserted over
        // what the panel was given.)
        Assert.DoesNotContain(BuildContentEntries(), i => i.IsSubAsset);

        // A virtual row is listed with the files rather than replacing them, and it sits after the
        // folders and before the real files.
        _panel.VirtualContentItems.Add(new ContentItem { Name = "New Asset", RelativePath = "" });
        Assert.Equal(
            new[] { "AFolder", "zFolder", "New Asset", "second.asset", "Alpha.txt", "beta.txt", "first.txt", "Gamma.txt" },
            Names(BuildContentEntries()));
    }

    // ================================================================
    //  9. Hidden entries
    // ================================================================

    // Dot-prefixed entries are hidden by default in both the folder list and the file list, and the
    // toggle brings both back in name order.
    [Fact]
    public void ContentEntries_DotEntriesAreHiddenUntilTheToggleIsOn()
    {
        WriteFile("visible.txt", 1, Utc(1));
        WriteFile(".hidden.txt", 1, Utc(1));
        WriteFolders("Visible", ".HiddenFolder");

        Assert.Equal(new[] { "Visible", "visible.txt" }, Names(BuildContentEntries()));

        ShowHidden = true;
        Assert.Equal(new[] { ".HiddenFolder", "Visible", ".hidden.txt", "visible.txt" },
            Names(BuildContentEntries()));
    }

    // KNOWN ISSUE: that toggle reaches the content view but not the folder tree. BuildFolderNodes drops
    // dot-folders unconditionally, and the tree's cache is keyed on ContentVersion alone, so showing
    // hidden entries adds the folder to the list beside the content view and never to the tree, and
    // never even invalidates what the tree already built. Asserted here on the half that is reachable
    // headless: the content view honours the toggle, and the emptiness check the tree paints each node
    // with keeps treating a dot-folder as nothing at all even when hidden entries are being shown.
    // See docs/PLAN_10_DE_10.md Fase 6. (H-ED-8)
    [Fact]
    public void ContentEntries_HiddenEntries_DoNotCountTowardsAFoldersEmptiness()
    {
        WriteFile(".hidden.txt", 1, Utc(1));
        WriteFolders("Folder");

        ShowHidden = true;
        Assert.Contains(BuildContentEntries(), i => i.Name == ".hidden.txt");

        // The folder holds only a dot-file, which is invisible unless hidden entries are shown; with them
        // shown it is no longer empty, but the check skips dot-prefixed names regardless of the toggle.
        Assert.True(IsFolderEmpty("Folder"));
    }

    // ================================================================
    //  10. Search
    // ================================================================

    // Search is a case-insensitive substring match over the item name, applied after folders, files and
    // virtual rows have all been gathered. An empty search keeps everything.
    [Fact]
    public void ContentEntries_SearchFiltersBySubstringIgnoringCase()
    {
        WriteFile("Alpha.txt", 1, Utc(1));
        WriteFile("beta.txt", 1, Utc(1));
        WriteFile("alphabet.txt", 1, Utc(1));
        WriteFolders("Alphabet");

        SearchText = "alpha";
        Assert.Equal(new[] { "Alphabet", "Alpha.txt", "alphabet.txt" }, Names(BuildContentEntries()));

        // "bet" is a substring of "Alphabet" and "alphabet.txt" too, so those come along: the search
        // matches names anywhere, not whole words.
        SearchText = "BET";
        Assert.Equal(new[] { "Alphabet", "alphabet.txt", "beta.txt" }, Names(BuildContentEntries()));

        SearchText = "no-such-name";
        Assert.Empty(BuildContentEntries());

        SearchText = "";
        Assert.Equal(new[] { "Alphabet", "Alpha.txt", "alphabet.txt", "beta.txt" }, Names(BuildContentEntries()));
    }

    // ================================================================
    //  13. Content cache
    // ================================================================

    // The content view walks the whole folder, so it is built once and served from a cache after that.
    // The same inputs have to hand back the very same list instance; changing the search has to rebuild
    // it. A new asset bumps the database's ContentVersion, which is the panel's cue that what it cached
    // is stale even though nothing the user did to the panel changed.
    [Fact]
    public void ContentCache_SameInputs_ReuseTheListAndAnyChangeRebuildsIt()
    {
        WriteFile("first.txt", 1, Utc(1));

        List<ContentItem> first = GetContentEntries();
        Assert.Equal(new[] { "first.txt" }, Names(first));
        Assert.Same(first, GetContentEntries());
        Assert.Same(first, ContentCache);

        // A changed search rebuilds.
        SearchText = "second";
        Assert.Empty(GetContentEntries());
        Assert.NotSame(first, ContentCache);

        // So does a new asset on disk.
        SearchText = "";
        Assert.NotSame(first, GetContentEntries());

        WriteFile("second.txt", 1, Utc(2));
        Assert.Equal(new[] { "first.txt", "second.txt" }, Names(GetContentEntries()));
    }

    // ================================================================
    //  14. Drop target validation
    // ================================================================

    // A drop is refused onto the folder the drag came from (nothing would move) and onto the dragged
    // folder itself or anything beneath it (a folder cannot be moved inside itself). A parent, a sibling
    // or any unrelated folder is fine, and the comparisons are case-insensitive.
    [Fact]
    public void CanAcceptAssetDropInto_RefusesSameFolderSelfAndDescendants()
    {
        StartAssetDrag("Materials/textures.png");
        Assert.False(CanAcceptAssetDropInto("Materials"));
        Assert.False(CanAcceptAssetDropInto("materials"));
        Assert.True(CanAcceptAssetDropInto("Props"));
        Assert.True(CanAcceptAssetDropInto(""));

        StartAssetDrag("Materials");
        Assert.False(CanAcceptAssetDropInto("Materials"));
        Assert.False(CanAcceptAssetDropInto("Materials/Textures"));
        Assert.True(CanAcceptAssetDropInto("Props"));
    }

    // KNOWN ISSUE: the cycle check reads the folder off the dragged path, and a sub-asset's path carries
    // its "#Sub" suffix. Path.GetDirectoryName stops at the last separator either way, so the guard
    // passes and the folder lights up as a valid target, while the move itself later looks for a file
    // literally named "Material.mat#Albedo" and finds nothing: the drag ends having moved nothing, with
    // no warning. See docs/PLAN_10_DE_10.md Fase 6. (H-ED-6)
    [Fact]
    public void CanAcceptAssetDropInto_ASubAssetDrag_IsAcceptedByAFolderButWouldMoveNothing()
    {
        StartAssetDrag("Material.mat#Albedo");

        Assert.True(CanAcceptAssetDropInto("Materials"));
    }
}