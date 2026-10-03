// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.GUI.Panels;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The Inspector panel's non-drawing decisions: what counts as a folder in the selection, which asset
/// GUID a selection inspects, and the folder sizes it reports.
/// <para/>
/// Nothing here calls OnGUI. The panel is a DockPanel with no constructor of its own, so
/// <c>new InspectorPanel()</c> is free, and OnGUI returns immediately headless anyway (it bails on
/// EditorTheme.DefaultFont being null). What is left are the type tests and the count cache, which take
/// their inputs as arguments and keep their state in statics that this class resets.
/// </summary>
public class InspectorPanelTests : EditorTestHarness, IDisposable
{
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    private readonly InspectorPanel _panel;
    private readonly EditorTestStatics.Scope _statics;

    public InspectorPanelTests()
    {
        _statics = new EditorTestStatics.Scope();
        _panel = new InspectorPanel();
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
    //  BindingFlags.NonPublic|Static).

    /// <summary>Private static method InspectorPanel.IsFolderSelection(object) (line 64).</summary>
    private static bool IsFolderSelection(object? selected) =>
        (bool)typeof(InspectorPanel).GetMethod("IsFolderSelection", StaticPrivate)!.Invoke(null, [selected])!;

    /// <summary>Private static method InspectorPanel.InspectedAssetGuid(object) (line 90).</summary>
    private static Guid InspectedAssetGuid(object? active) =>
        (Guid)typeof(InspectorPanel).GetMethod("InspectedAssetGuid", StaticPrivate)!.Invoke(null, [active])!;

    /// <summary>Private static method InspectorPanel.GetFolderCounts(string, string) (line 624).</summary>
    private static (int Files, int Folders)? GetFolderCounts(string relativePath, string absolutePath) =>
        ((int Files, int Folders)?)typeof(InspectorPanel)
            .GetMethod("GetFolderCounts", StaticPrivate)!.Invoke(null, [relativePath, absolutePath]);

    /// <summary>Private static field InspectorPanel._folderCounts: the memoised counts, keyed by path (line 621).</summary>
    private static Dictionary<string, (int Files, int Folders)> FolderCounts =>
        (Dictionary<string, (int Files, int Folders)>)typeof(InspectorPanel)
            .GetField("_folderCounts", StaticPrivate)!.GetValue(null)!;

    /// <summary>Private static field InspectorPanel._folderCountsVersion: the ContentVersion it was built at (line 622).</summary>
    private static int FolderCountsVersion =>
        (int)typeof(InspectorPanel).GetField("_folderCountsVersion", StaticPrivate)!.GetValue(null)!;

    // ================================================================
    //  Fixture helpers
    // ================================================================

    /// <summary>Writes a file, creating its folder, and reindexes.</summary>
    private void Write(string relativePath, string content = "x")
    {
        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
        Assets.Refresh();
    }

    // ================================================================
    //  1. What counts as a folder in the selection
    // ================================================================

    // The Project panel puts ContentItems in the shared selection, and the inspector has to tell a
    // folder from an asset before drawing anything. Only a ContentItem that says it is a folder qualifies:
    // anything else in the selection, including another kind of inspectable, is not a folder.
    [Fact]
    public void IsFolderSelection_OnlyAContentItemMarkedAsFolderCounts()
    {
        Assert.True(IsFolderSelection(new ContentItem { Name = "Art", RelativePath = "Art", IsFolder = true }));
        Assert.False(IsFolderSelection(new ContentItem { Name = "a.txt", RelativePath = "a.txt" }));
        Assert.False(IsFolderSelection(null));
    }

    // ================================================================
    //  2. Which asset the inspector is about
    // ================================================================

    // The GUID is what the panel keys its "unapplied edits" bookkeeping on, so it has to resolve the
    // inspected thing to the same GUID the asset database holds, and to nothing at all when there is no
    // asset: a folder and a selection that is not a ContentItem both have no asset behind them.
    [Fact]
    public void InspectedAssetGuid_ResolvesTheAssetAndGivesUpOtherwise()
    {
        // A folder and a non-ContentItem selection inspect no asset.
        Assert.Equal(Guid.Empty, InspectedAssetGuid(new ContentItem { Name = "Art", RelativePath = "Art", IsFolder = true }));
        Assert.Equal(Guid.Empty, InspectedAssetGuid(null));
        Assert.Equal(Guid.Empty, InspectedAssetGuid(new object()));

        // A row that already knows its GUID uses it as-is.
        Guid known = Guid.NewGuid();
        Assert.Equal(known, InspectedAssetGuid(new ContentItem { Name = "a.txt", RelativePath = "a.txt", Guid = known }));

        // A row without one is resolved through the database by path, so the inspector and the database
        // agree on which asset is open.
        Write("notes.xyz");
        Assert.Equal(Assets.PathToGuid("notes.xyz"),
            InspectedAssetGuid(new ContentItem { Name = "notes.xyz", RelativePath = "notes.xyz" }));

        // A path that is not in the database resolves to nothing rather than throwing.
        Assert.Equal(Guid.Empty, InspectedAssetGuid(new ContentItem { Name = "gone.xyz", RelativePath = "gone.xyz" }));
    }

    // ================================================================
    //  3. Folder sizes
    // ================================================================

    // KNOWN ISSUE: the sidecar filter is a bare EndsWith(".meta"), which is both culture-sensitive and
    // case-sensitive, so a file written as "b.META" is counted as content. Windows file systems are
    // case-insensitive, so that file is a perfectly ordinary sidecar as far as the user is concerned, and
    // the inflated count is then cached until the database version moves. See docs/PLAN_10_DE_10.md
    // Fase 6. (H-ED-29)
    [Fact]
    public void GetFolderCounts_SkipsLowercaseMetaFilesButCountsUppercaseOnes()
    {
        Write("Folder/a.txt");
        Write("Folder/b.meta");     // lower case: recognised as a sidecar
        Write("Folder/c.META");     // upper case: not recognised, and counted as content
        Write("Folder/Nested/d.txt");

        (int Files, int Folders)? counts = GetFolderCounts("Folder", AssetAbsolutePath("Folder"));

        Assert.NotNull(counts);
        Assert.Equal(3, counts!.Value.Files);   // a.txt, c.META and Nested/d.txt
        Assert.Equal(1, counts!.Value.Folders);
    }

    // The walk is recursive and cached, because the inspector redraws continuously while a folder stays
    // selected. The cache is keyed by the database's ContentVersion, so it is dropped when the project's
    // content moves rather than going stale until the editor restarts.
    [Fact]
    public void GetFolderCounts_WalksTheWholeTreeOnceAndCachesItAgainstTheContentVersion()
    {
        Write("Folder/a.txt");
        Write("Folder/Nested/d.txt");

        (int Files, int Folders)? first = GetFolderCounts("Folder", AssetAbsolutePath("Folder"));
        Assert.NotNull(first);
        Assert.Equal(2, first!.Value.Files);
        Assert.Equal(1, first!.Value.Folders);

        // Served from the cache on the second call, and recorded under the path it was asked for.
        Assert.Equal(first, GetFolderCounts("Folder", AssetAbsolutePath("Folder")));
        Assert.True(FolderCounts.ContainsKey("Folder"));

        // The cache is stamped with the version it was built at, so the next content change drops it.
        Assert.Equal(Assets.ContentVersion, FolderCountsVersion);

        // A path that does not exist yields no counts rather than throwing.
        Assert.Null(GetFolderCounts("NoSuchFolder", AssetAbsolutePath("NoSuchFolder")));
    }
}