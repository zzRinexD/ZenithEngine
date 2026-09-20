REPORTE DE INICIALIZACION - EDITOR ZENITH (Prowl)
=====================================================
Fecha: 2026-09-20
No se modifico ningun archivo fuente del proyecto.

# 1. ORDEN EXACTO: Main -> Run -> Initialize (con lineas)

Paso  Archivo                           Linea   Metodo / Accion
--------------------------------------------------------------------------------
  1   Zenith.Editor/Program.cs           93      public static void Main(string[] args)
  2   Zenith.Editor/Program.cs          132      var editor = new EditorApplication();
  3   Zenith.Editor/Program.cs          133      editor.Run("Zenith Engine", 1920, 1080);
  4   Zenith.Runtime/Game.cs            51      public void Run(string title, int width, int height)
  5   Zenith.Runtime/Game.cs            67      InitializeWindow(title, width, height);
  6   Zenith.Editor/Core/EditorApplication.cs  63  public override void InitializeWindow(...)
  7   Zenith.Runtime/Game.cs            69-84   Window.Load += () => Load();
  8   Zenith.Runtime/Game.cs            86-107  void Load() { ... Initialize(); ... }
  9   Zenith.Editor/Core/EditorApplication.cs  74  public override void Initialize()

Dentro de Initialize() (EditorApplication.cs:74-290):
  10  76  Instance = this;
  11  77-78  Application.IsEditor = true; Application.IsPlaying = false;
  12  80  InitializeFont();
  13  82  Resize(...);
  14  87-89  EditorTheme.FontIconOutline / Solid (LoadFallbackFont)
  15  91  LoadSystemFallbackFonts();
  16  94  _ = EditorSettings.Instance;  // carga y aplica tema
  17  96  ApplyFramePacing();
  18  98  _dockSpace = new DockSpace(...);
  19  103-123  Si Program.StartupProjectPath != null:
        107  Project.Open(...)
        108  SetActive()
        111  ScriptAssemblyManager.LoadAssemblies(project)
        114  ScriptAssemblyManager.RequestRecompile()
  20  126-132  Prowl.Rosetta.Loc.Configure(...)
  21  134  EditorRegistries.Initialize();
  22  137-140  Input.OnCursorLocked / OnCursorLockFailed
  23  142  RegisterMenus();
  24  144-166  Si projectAlreadyInitialized:
        147  new EditorAssetBackend(Project.Current!); db.Initialize();
        151  EditorRegistries.OnProjectOpened();
        154-156  LoadDockLayout();
        158  EditorSceneManager.EnsureSceneLoaded();
        161-165  SkipIntro / CloseLauncher / SetTime
  25  167-171  Si NO hay proyecto:
        170  ProjectLauncher.Initialize();
  26  174  InitializeStatusBar();
  27  177-233  PropertyGridConfig (origen, handlers, drawers)
  28  235-256  SaveManager.OnSave += handlers
  29  259  ApplyDarkTitleBar();
  30  262-288  Window event handlers (Move, Resize, StateChanged, Closing)
  31  289  Window.FileDrop += ExternalAssetDrop.Enqueue;

# 2. SINCRONO (bloquea main thread) vs ASINCRONO

SINCRONOS (bloquean main thread durante Initialize o cada frame):
------------------------------------------------------------------
- Main(), InitializeWindow(), Load(), Initialize()
- InitializeFont(), EditorSettings.Instance, DockSpace init
- Project.Open(), Project.SetActive()
- ScriptAssemblyManager.LoadAssemblies()  -> lee DLLs, carga en contexto (bloqueante)
- EditorRegistries.Initialize()          -> escaneo de tipos (bloqueante)
- EditorAssetBackend.Initialize()        -> escaneo, importacion, cache (bloqueante)
- EditorSceneManager.EnsureSceneLoaded() -> carga o crea escena (bloqueante)
- RequestRecompile()                     -> solo marca flag (instantaneo, bloqueante por ser llamada directa)
- ProjectLauncher.Initialize()           -> bloqueante
- BeginGui(), EndGui(), OnUpdate(), OnRender(), OnGui() -> cada frame, bloqueantes
- GraphicsProgram.CompileShader()        -> bloqueante en render thread (sinc)
- SimulationStep()                       -> bloqueante (Update + FixedUpdate)

ASINCRONOS:
------------
- ScriptAssemblyManager.Update()         -> consume compilaciones terminadas (cada frame)
- ScriptCompiler.CompileAll()            -> lanzado en Task.Run() (linea 127)
- EditorAssetBackend.ProcessFileChanges() -> puede ser pesado, pero se llama cada frame en BeginGui (sincronico por ser invocado desde main thread, aunque internamente usa watchers)
- ThumbnailGenerator.ProcessOne()        -> procesado incremental (frame a frame)
- ScriptAssemblyManager.RequestRecompile() -> no bloquea (marca flag), pero el compilador real es asincrono
- Refresh() (AssetBackend)              -> llamado en foco; bloqueante en ese momento

# 3. METODOS QUE PROBABLEMENTE TARDAN > 100 ms

Basado en el codigo y las operaciones involucradas:
------------------------------------------------------------------
1. Initialize() completo (linea 74):
   - EditorRegistries.Initialize() (escanea todos los ensamblados cargados)
   - EditorAssetBackend.Initialize() (escaneo de archivos, importacion de assets sucios, construccion de indices)
   - ScriptAssemblyManager.LoadAssemblies() (carga de DLLs en contexto nuevo)
   - EditorSceneManager.EnsureSceneLoaded() (deserializa escena o crea una con objetos por defecto)

2. ScriptCompiler.CompileAll() (Task.Run):
   - Compila todos los archivos .cs del proyecto. Puede durar segundos.

3. GraphicsProgram.CompileShader() (linea 68-131 de GraphicsProgram.cs):
   - Compilacion de shaders en la GPU. Cada shader puede tardar > 100 ms dependiendo de complejidad.

4. Project.Open() + LoadAssemblies() + EditorRegistries.Initialize():
   - Cuando se lanza con --project, todo esto ocurre dentro de Initialize() antes de que se muestre el launcher.

5. EditorSceneManager.CreateAndLoadDefaultScene() (lineas 119-124):
   - Crea objetos por defecto (camara, luz, cubos) y carga la escena. Probablemente > 100 ms si hay muchos assets referenciados.

6. ReinitializeRegistries() (llamada dentro de BeginGui al detectar apertura de proyecto, linea 452):
   - Re-escanea tipos de los nuevos ensamblados cargados.

# 4. METODOS DENTRO DEL CICLO DE VIDA POR FRAME

Según Zenith.Runtime/Game.cs (lineas 109-222) y EditorApplication.cs:

CADA FRAME - ACTUALIZACION (Window.Update en Game.cs:109):
----------------------------------------------------------
- UpdatePaperInput()
- AudioContext.Update()
- Time.Update(), Time.TimeStack.Clear()/Push()
- Input.UpdateActions()
- BeginUpdate()         (virtual, vacio por defecto)
- SimulationStep()
  - Tasks.MainThreadContext.Current?.Pump()
  - AudioContext.SuspendedByPause
  - FixedUpdate loop (si ShouldRunGameplay)
  - OnUpdate(scene)     -> EditorApplication.OnUpdate (1863)
      - Application.IsGameplayExecuting = true (si aplica)
      - scene.Update()
      - scene.DrawGizmos()
      - Selection.GetSelected<GameObject>() -> DrawGizmosSelected()
  - OnUpdate termina
- EndUpdate()           (virtual, vacio por defecto)
- FrameCounter++ y Console.Title cada 60 frames

CADA FRAME - RENDER (Window.Render en Game.cs:144):
--------------------------------------------------
- Graphics: Frame Start (clear, viewport, raster)
- Rendering.ShadowAtlas.TryInitialize() / Clear()
- BeginRender()         (virtual, vacio)
- OnRender(scene)       -> EditorApplication.OnRender (1904) -> NO hace nada (editor no renderiza escena directamente)
- EndRender()           (virtual, vacio)
- Graphics: Pre-GUI (clear de comandos)
- PreparePaperFrame()   (EditorApplication:327)
- Paper.BeginFrame()
- BeginGui(paper)       -> EditorApplication.BeginGui (362)
  - Undo.FlushFrame()
  - Input.Escape (cursor)
  - ShortcutManager (SaveAs, NewScene, Undo, Redo)
  - TickPerfStats(), Selection.UpdatePing(), EditorTheme.TickOrigami()
  - PushOrigami(), Origami.BeginFrame()
  - EditorTheme.DropShadows / Glows
  - Paper.Canvas.SetAntiAlias()
  - SaveManager.Update()
  - Detectar apertura de proyecto (launcher cerrado) -> InitializeAssetDB, LoadAssemblies, RequestRecompile, ReinitializeRegistries, RestoreLayout, EnsureSceneLoaded (lineas 430-464)
  - Focus change -> Refresh(), ApplyFramePacing()
  - ExternalAssetDrop.ProcessPending()
  - EditorAssetBackend.ProcessFileChanges()
  - ScriptAssemblyManager.Update()
  - ThumbnailGenerator.ProcessOne()
  - EditorAssetBackend.TickIdleSweep()
  - Mostrar launcher / intro (ProjectLauncher.Draw / DrawIntro)
  - DrawHeader(), DrawStatusBar(), _dockSpace.Draw()
  - EditorGuide.TryAutoStart()
- OnGui(scene, paper)   -> EditorApplication.OnGui (1912) -> NO hace nada (UI controlada por editor)
- EndGui(paper)         -> EditorApplication.EndGui (701)
  - EditorGuide.Draw()
  - OrigamiUI.Origami.EndFrame()
  - DrawIntro()
  - ProjectLauncher.DrawTipStrip()
  - Pop Origami theme
- Paper.EndFrame()
- AfterGui(scene)       (virtual, vacio)
- RenderTexture.UpdatePool()
- Graphics.FlushDeferredDisposes()
- Debug.ClearGizmos()
- EngineObject.ProcessDestroyed()
- Scene.ProcessPendingLoad()

Nota: EditorApplication NO override Update, Draw, BeginUpdate, EndUpdate, BeginRender, EndRender, AfterGui (usa los virtuales por defecto de Game, vacios o con comportamiento base).

# 5. DIAGRAMA ASCII DEL FLUJO COMPLETO

                                    +------------------+
                                    |  Program.Main()  |  Zenith.Editor/Program.cs:93
                                    +--------+---------+
                                             |
                                             v
                                    +------------------+
                                    | new EditorApp()   |  Zenith.Editor/Program.cs:132
                                    +--------+---------+
                                             |
                                             v
                                    +------------------+
                                    | editor.Run(...)  |  Zenith.Editor/Program.cs:133
                                    +--------+---------+
                                             |
                                             v
                           +------------------------------+
                           |  Game.Run()                 |  Zenith.Runtime/Game.cs:51
                           +--------------+---------------+
                                          |
                    +---------------------+---------------------+
                    |                                           |
                    v                                           v
         +-------------------+                       +-------------------+
         | InitializeWindow()|                       | Window.Load += () |  Game.cs:69
         | (linea 63)        |                       +---------+---------+
         +---------+---------+                                 |
                   |                                         v
                   v                               +-------------------+
         +-------------------+                     | Load()            |  Game.cs:86
         | Window.Init...    |                     |  - AudioContext...|
         +-------------------+                     |  - Initialize()    |  Game.cs:106
                                                   +---------+---------+
                                                             |
                                                             v
                                                   +-------------------+
                                                   | Initialize()      | EditorApp.cs:74
                                                   |  (bloqueante)     |
                                                   +---------+---------+
                                                             |
                      +--------------------------------------+--------------------------------------+
                      |                                      |                                      |
                      v                                      v                                      v
         +--------------------+                +--------------------+                +--------------------+
         | InitializeFont()   |                | EditorRegistries.  |                | Project.Open()     | (si --project)
         | (linea 296)        |                | Initialize() (134)|                | (linea 107)        |
         +--------------------+                +--------------------+                +--------+-----------+
                      |                                      |                                      |
                      v                                      v                                      v
         +--------------------+                +--------------------+                +--------------------+
         | Load System Fonts  |                | LoadAssemblies()   |                | RequestRecompile() |
         | (linea 91)         |                | (linea 111)        |                | (linea 114)        |
         +--------------------+                +--------------------+                +--------+-----------+
                      |                                      |                                      |
                      v                                      v                                      v
         +--------------------+                +--------------------+                +--------------------+
         | EditorAssetBackend |                | InitializeAssetDB  |                | EditorSceneManager |
         | Initialize() (147) |                | (inicio de frame)  |                | EnsureSceneLoaded() |
         +--------------------+                +--------------------+                | (linea 158)        |
                      |                                      |                      +--------+-----------+
                      v                                      v                               |
         +--------------------+                +--------------------+                      v
         | Restore Layout     |                | ScriptCompiler...  |                +--------------------+
         | LoadDockLayout()   |                | (Task.Run async)   |                | CreateDefaultScene |
         | (lineas 154-156)   |                | (linea 127)        |                | (linea 120)         |
         +--------------------+                +--------------------+                +--------------------+
                      |                                      |                      |
                      v                                      v                      v
         +--------------------+                +--------------------+        +--------------------+
         | SkipIntro/Intro    |                | Update() cada frame|        | Escena cargada    |
         | ProjectLauncher     |                | (consume resultados)|        +--------------------+
         | Initialize() (170)  |                +--------------------+               |
         +--------------------+                                                     |
         | ProjectLauncher.Draw| (cada frame)                                         |
         +--------------------+                                                     v
                                                                               [EDITADO POR FRAME]
                                                                               +--------------------+
                                                                               | Window.Update      | Game.cs:109
                                                                               |  - BeginUpdate()    |
                                                                               |  - SimulationStep() |
                                                                               |      OnUpdate() ->  |
                                                                               |        scene.Update()|
                                                                               |        DrawGizmos()  |
                                                                               |  - EndUpdate()      |
                                                                               +--------+-----------+
                                                                                        |
                                                                                        v
                                                                               +--------------------+
                                                                               | Window.Render      | Game.cs:144
                                                                               |  - BeginRender()    |
                                                                               |  - OnRender() ->    |
                                                                               |      (vacio editor) |
                                                                               |  - EndRender()      |
                                                                               |  - PreGui           |
                                                                               |  - Paper.BeginFrame |
                                                                               |  - BeginGui()       |
                                                                               |      EditorApp.cs:362 |
                                                                               |  - OnGui() (vacio)   |
                                                                               |  - EndGui()         |
                                                                               |      EditorApp.cs:701 |
                                                                               |  - Paper.EndFrame    |
                                                                               |  - AfterGui()        |
                                                                               |  - Flush/Destroy/    |
                                                                               |    ProcessPendingLoad|
                                                                               +--------------------+

Nota: El bloque "Initialize()" (EditorApp.cs:74) es el que mas tarda (segundos) porque combina
escaneo de registries, carga de ensamblados, inicializacion del backend de assets y apertura
de proyecto/escena. Todo eso ocurre ANTES de que se dibuje el primer frame con ProjectLauncher.

FIN DEL REPORTE
