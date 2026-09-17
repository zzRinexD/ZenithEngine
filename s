[1mdiff --git a/Zenith.Editor/Core/EditorApplication.cs b/Zenith.Editor/Core/EditorApplication.cs[m
[1mindex 3026d887..17db61ae 100644[m
[1m--- a/Zenith.Editor/Core/EditorApplication.cs[m
[1m+++ b/Zenith.Editor/Core/EditorApplication.cs[m
[36m@@ -110,7 +110,7 @@[m [mpublic class EditorApplication : Game[m
                 ScriptAssemblyManager.RequestRecompile();[m
 [m
                 projectAlreadyInitialized = true;[m
[31m-                Window.InternalWindow.Title = $"Prowl Editor - {project.Name}";[m
[32m+[m[32m                Window.InternalWindow.Title = $"Zenith Engine | {project.Name}";[m
             }[m
             catch (Exception ex)[m
             {[m
[36m@@ -309,7 +309,7 @@[m [mpublic class EditorApplication : Game[m
 [m
     private static Prowl.Scribe.FontFile? LoadBundledFont(string fileName)[m
     {[m
[31m-        using var stream = GetEmbeddedResource(fileName);[m
[32m+[m[32m        using Stream? stream = GetEmbeddedResource(fileName);[m
         if (stream == null)[m
         {[m
             Runtime.Debug.LogWarning($"Missing bundled font: {fileName}");[m
[36m@@ -430,7 +430,7 @@[m [mpublic class EditorApplication : Game[m
             GUI.EditorGuide.ArmAutoStart(); // let the tour play once for this freshly-opened project[m
             if (Project.Current != null)[m
             {[m
[31m-                Window.InternalWindow.Title = $"Prowl Editor - {Project.Current.Name}";[m
[32m+[m[32m                Window.InternalWindow.Title = $"Zenith Engine | {Project.Current.Name}";[m
 [m
                 // Initialize the asset database for the opened project[m
                 var db = new EditorAssetBackend(Project.Current);[m
[36m@@ -629,7 +629,7 @@[m [mpublic class EditorApplication : Game[m
         int fps = _dispFps;[m
         string fpsNum = fps.ToString();[m
         string msText = $"{_dispMs:F1}ms";[m
[31m-        var dotColor = fps >= 50 ? EditorTheme.Green400 : (fps >= 25 ? EditorTheme.Amber400 : EditorTheme.Red400);[m
[32m+[m[32m        System.Drawing.Color dotColor = fps >= 50 ? EditorTheme.Green400 : (fps >= 25 ? EditorTheme.Amber400 : EditorTheme.Red400);[m
 [m
         string version = Assembly.GetExecutingAssembly()[m
             .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion[m
