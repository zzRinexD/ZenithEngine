// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.Popups;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Puts the editor's process-wide state back to a known state around a test.
/// <para/>
/// The editor keeps its selection, its undo stack and its drag/rename overlays in statics, and this
/// assembly runs without parallelization precisely because of them (see TestAssemblyConfig). Without a
/// reset between tests a leftover selection or an open rename overlay silently changes what the next
/// test observes, which looks exactly like a real failure.
/// <para/>
/// Deliberately separate from <see cref="EditorTestHarness"/>: the harness owns the throwaway project,
/// this owns the statics, and a test that needs neither should not pay for either.
/// </summary>
public static class EditorTestStatics
{
    /// <summary>Clears every global the editor panels keep between frames.</summary>
    public static void Reset()
    {
        Selection.Clear();

        // Undo.Clear exists and drops the pending records; the stack itself is emptied by Clear too.
        Undo.Clear();

        // A drag left open would make the next test's CanAcceptAssetDropInto see a payload that
        // nothing started. Cancel also clears the payload, which EndDrag alone would not guarantee.
        OrigamiUI.DragDrop.Cancel();

        // Cancel is the public way out of an overlay Begin() started; the panel's confirm path is
        // private to RenameOverlay and only reachable through its Draw.
        RenameOverlay.Cancel();

        // Set from ProjectPanel.OnGUI (last drawn wins). Headless no panel ever draws, so a value
        // left by another test would point at a disposed panel.
        ProjectPanel.Instance = null;
    }

    /// <summary>
    /// Resets on construction and again on disposal, so a test that fails partway through cannot
    /// hand its leftovers to the next one.
    /// </summary>
    public sealed class Scope : IDisposable
    {
        public Scope() => Reset();

        public void Dispose()
        {
            Reset();
            GC.SuppressFinalize(this);
        }
    }
}