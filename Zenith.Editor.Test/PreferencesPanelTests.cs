// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.GUI.Panels;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The Preferences panel's tab routing: which category is showing, and how a sidebar id maps back to it.
/// <para/>
/// Nothing here calls OnGUI. The panel is a DockPanel with no constructor of its own, so
/// <c>new PreferencesPanel()</c> is free, and OnGUI returns immediately headless anyway (it bails on
/// EditorTheme.DefaultFont being null). Tab is a private nested enum, so it is parsed and compared by
/// name.
/// </summary>
public class PreferencesPanelTests : EditorTestHarness, IDisposable
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    private readonly PreferencesPanel _panel;
    private readonly EditorTestStatics.Scope _statics;

    public PreferencesPanelTests()
    {
        _statics = new EditorTestStatics.Scope();
        _panel = new PreferencesPanel();
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

    /// <summary>Private field PreferencesPanel._tab (line 33): the category being shown.</summary>
    private string CurrentTab => (string)typeof(PreferencesPanel)
        .GetField("_tab", InstancePrivate)!.GetValue(_panel)!.ToString()!;

    /// <summary>Private static method PreferencesPanel.TabId(Tab) (line 63): enum to sidebar id.</summary>
    private static string TabId(string categoryName) =>
        (string)typeof(PreferencesPanel).GetMethod("TabId", StaticPrivate)!
            .Invoke(null, [Enum.Parse(TabType, categoryName)])!;

    /// <summary>Private static method PreferencesPanel.ParseTab(string) (line 64): sidebar id to enum.</summary>
    private static object ParseTab(string id) =>
        typeof(PreferencesPanel).GetMethod("ParseTab", StaticPrivate)!.Invoke(null, [id])!;

    /// <summary>Private nested enum PreferencesPanel.Tab { General, Theme, Shortcuts } (line 32).</summary>
    private static Type TabType => typeof(PreferencesPanel).GetNestedType("Tab", BindingFlags.NonPublic)
        ?? throw new MissingMemberException(nameof(PreferencesPanel), "Tab");

    // ================================================================
    //  1-2. Tab routing
    // ================================================================

    // The sidebar identifies a category by string, so the two mappings have to be inverses: a sidebar id
    // that survives a round trip is what makes the panel reopen on the category the user left it on.
    // Anything unrecognised falls back to General rather than leaving the panel with no category at all.
    [Fact]
    public void TabIdAndParseTab_AreInversesAndUnknownIdsFallBackToGeneral()
    {
        string[] categories = Enum.GetNames(TabType);
        Assert.Equal(new[] { "General", "Theme", "Shortcuts" }, categories);

        foreach (string category in categories)
        {
            Assert.Equal(category, ParseTab(TabId(category)).ToString());
        }

        Assert.Equal("General", ParseTab("no-such-category").ToString());
        Assert.Equal("General", ParseTab("").ToString());
    }

    // ShowTheme is the entry point the settings menu and the theme editor jump through, so it has to land
    // on the theme category specifically rather than just switching categories somehow.
    [Fact]
    public void ShowTheme_SwitchesToTheThemeCategory()
    {
        Assert.Equal("General", CurrentTab);

        _panel.ShowTheme();

        Assert.Equal("Theme", CurrentTab);
    }
}