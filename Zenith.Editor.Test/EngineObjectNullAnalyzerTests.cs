// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Analyzers;
using Prowl.Editor.Projects.Scripting;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Verifies the analyzer that refuses null-conditional and null-coalescing operators on an
/// EngineObject: a destroyed object is still a live reference, so '?.'/'??' test the wrong thing.
/// </summary>
[Trait("Category", "Build")]
public class EngineObjectNullAnalyzerTests : EditorTestHarness
{
    // ====================================================================
    // PROWLEO001 - null-conditional '?.' / '?[]'
    // ====================================================================

    [Fact]
    public void ReportsNullConditionalOnMethod()
    {
        WriteScript("EOMethod.cs",
            "public class Use { public static void Run() { Prowl.Runtime.EngineObject obj = null; obj?.Dispose(); } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, result.Errors); // an error, so the compile fails
        Assert.Contains("error " + EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    [Fact]
    public void ReportsNullConditionalOnProperty()
    {
        WriteScript("EOProp.cs",
            "public class Use { public static bool Run() { Prowl.Runtime.EngineObject obj = null; return obj?.IsDisposed ?? false; } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, result.Errors);
        Assert.Contains("error " + EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        // The trailing '??' is on a bool?, not an EngineObject, so it stays quiet.
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    [Fact]
    public void ReportsNullConditionalOnField()
    {
        WriteScript("EOField.cs",
            "public class Use { public static string Run() { Prowl.Runtime.EngineObject obj = null; return obj?.Name; } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, result.Errors);
        Assert.Contains("error " + EngineObjectNullAnalyzer.NullConditionalId, result.Output);
    }

    [Fact]
    public void ReportsNullConditionalOnIndexer()
    {
        // A subclass is needed for an indexer; being a MonoBehaviour also proves the analyzer
        // walks the inheritance chain rather than only matching EngineObject exactly.
        WriteScript("EOIndexer.cs",
            "public class Thing : Prowl.Runtime.MonoBehaviour { public int this[int i] => i; " +
            "public static int Run() { Thing obj = null; return obj?[0] ?? 0; } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, result.Errors);
        Assert.Contains("error " + EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    /// <summary>The field that started it all: an uninitialized EngineObject field guarded with '?.'.</summary>
    [Fact]
    public void ReportsNullConditionalOnAnUninitializedField()
    {
        WriteScript("EOUninitField.cs",
            "public class Holder : Prowl.Runtime.MonoBehaviour " +
            "{ public Prowl.Runtime.EngineObject Target; public int Frame => Target?.Name.Length ?? 0; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, result.Errors);
        Assert.Contains("error " + EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    [Fact]
    public void ReportsNullConditionalOnAGameObject()
    {
        WriteScript("EOGameObject.cs",
            "public class Use { public static string Run() { Prowl.Runtime.GameObject g = null; return g?.Name; } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, result.Errors);
        Assert.Contains("error " + EngineObjectNullAnalyzer.NullConditionalId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutAnExplicitValidityCheck()
    {
        // IsValid() is the sanctioned guard; it cannot be flagged or it would flag its own fix.
        WriteScript("EOValid.cs",
            "using Prowl.Runtime; public class Use { public static bool Run() { EngineObject obj = null; return obj.IsValid(); } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutReferenceEquals()
    {
        // The analyzer's own message points here for a true reference-null test.
        WriteScript("EORefEq.cs",
            "public class Use { public static bool Run(Prowl.Runtime.EngineObject obj) => object.ReferenceEquals(obj, null); }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutAnExplicitNotEqualNullCheck()
    {
        WriteScript("SONotEqual.cs",
            "public class Use { public static bool Run(Prowl.Runtime.EngineObject obj) => obj != null; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutAStringNullConditional()
    {
        WriteScript("SOString.cs",
            "public class Use { public static int Run(string str) => str?.Length ?? 0; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutADirectInvocation()
    {
        WriteScript("SODirect.cs",
            "public class Use { public static void Run(Prowl.Runtime.EngineObject obj) => obj.Dispose(); }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
    }

    // ====================================================================
    // PROWLEO002 - null-coalescing '??' / '??='
    // ====================================================================

    [Fact]
    public void ReportsNullCoalescing()
    {
        WriteScript("EOCoalesce.cs",
            "public class Use { public static Prowl.Runtime.EngineObject Run(Prowl.Runtime.EngineObject obj, " +
            "Prowl.Runtime.EngineObject fallback) => obj ?? fallback; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, result.Errors);
        Assert.Contains("error " + EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
    }

    [Fact]
    public void ReportsNullCoalescingAssignment()
    {
        WriteScript("EOCoalesceAssign.cs",
            "public class Use { public static Prowl.Runtime.EngineObject Run(Prowl.Runtime.EngineObject obj, " +
            "Prowl.Runtime.EngineObject other) { obj ??= other; return obj; } }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success, result.Errors);
        Assert.Contains("error " + EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutAStringCoalescing()
    {
        WriteScript("SOStringCoalesce.cs",
            "public class Use { public static string Run(string s) => s ?? \"default\"; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
    }

    [Fact]
    public void SaysNothingAboutAPrimitiveCoalescing()
    {
        WriteScript("SOPrimitiveCoalesce.cs",
            "public class Use { public static int Run(int? x) => x ?? 42; }");

        var result = ScriptCompiler.CompileAll(Project);

        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullCoalescingId, result.Output);
        Assert.DoesNotContain(EngineObjectNullAnalyzer.NullConditionalId, result.Output);
    }
}
