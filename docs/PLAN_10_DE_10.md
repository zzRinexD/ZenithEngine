# PLAN "ZENITH 10/10"

> Objetivo: llevar el proyecto de **6,5/10 a 10/10**.
> Orden de ejecución: de más urgente (Fase 0) a prescindible (Fase 8).
> Fecha de elaboración: 2026-09-29. Basado en auditoría exhaustiva de Runtime, Editor, Tests/CI e higiene del repo.

---

## Resumen de puntuación actual (baseline)

| Categoría | Nota actual | Meta |
|---|---|---|
| Código y arquitectura | 6,9 | 10 |
| Rendimiento | 6,4 | 10 |
| Pruebas | 6,6 | 10 |
| CI/CD y herramientas | 4,4 | 10 |
| Repositorio e identidad | 5,9 | 10 |
| Funcionalidad construida | 7,8 | 10 |
| **GLOBAL** | **6,5** | **10** |

**Orden de ejecución:** Fase 0 → 1 → 2 → 4 (paralelo con 3) → 5 → 6 → 7 → 8.
Las Fases 0–2 son las que más suben la nota por hora invertida.

---

# FASE 0 — EMERGENCIA: cosas rotas ahora mismo

> **Estimación:** 1–2 horas. **Impacto:** crítico (el proyecto no puede releasearse ni se auto-verifica).

- [ ] **0.1 Arreglar `.github/workflows/release.yml`** (roto en las 5 celdas de la matriz)
  - [ ] `./Prowl.Editor/Prowl.Editor.csproj` → `./Zenith.Editor/Zenith.Editor.csproj` (la ruta `Prowl.Editor` no existe)
  - [ ] Quitar `submodules: true` del checkout (no existe `.gitmodules`)
  - [ ] Renombrar bundle macOS: `Prowl.app` → `Zenith.app`, bundle id `com.prowlengine.editor` → id propio
  - [ ] Actualizar `generate_release_notes`/nombres de artefactos "Prowl" → "Zenith"
- [ ] **0.2 Crear `.github/workflows/ci.yml`**
  - [ ] Trigger: `pull_request` + `push` a `main`
  - [ ] Job build: `dotnet build Zenith.sln -c Debug` (Windows y Ubuntu)
  - [ ] Job test: `dotnet test Zenith.Runtime.Test` + `dotnet test Zenith.Editor.Test`
  - [ ] Subir test results como artefacto en fallo
- [ ] **0.3 Reparar `.gitignore`**
  - [ ] Línea 351: regla `.kilo/` corrupta con **8 bytes NUL** → reescribir el archivo completo en UTF-8 limpio (sin BOM)
  - [ ] `git rm --cached .kilo/kilo.jsonc` (se coló por la regla rota)
  - [ ] Borrar reglas muertas: líneas 172-173 `!Prowl.Editor/Packages/**` (ruta inexistente tras rename)
  - [ ] Añadir regla explícita `Build/` (hoy solo se ignora por coincidencia con `[Dd]ebug/`/`[Rr]elease/`)
  - [ ] Añadir patrones: `tmp_*`, `*_stdout.txt`, `*_stderr.txt`, `s`
  - [ ] Verificar tras arreglar: `git diff` ya no muestra `.gitignore` como `Bin`
- [ ] **0.4 `git rm` de la basura commiteada** (8 archivos en el índice)
  - [ ] `s` (un `git diff` volcado, commit `d08f2dab`)
  - [ ] `editor_stdout.txt`, `editor_stderr.txt` (commit `5c7eb543`)
  - [ ] `tmp_antes_blankcard.txt`, `tmp_antes_emptyslot.txt`, `tmp_despues_blankcard.txt`, `tmp_despues_emptyslot.txt` (commit `d5f036d1`)
- [ ] **0.5 Limpiar disco de leftovers ignorados (~140 MB)**
  - [ ] `inspect.cs`, `InspectClearFocus.cs`, `InspectProj/` (2,6 MB), `InspectClearFocusProj/` (2 MB)
  - [ ] `editor_full.log`, `editor_zenith.log`, `CHANGES.txt` (huérfano: borrado del repo en `737eb5cb`)
  - [ ] `Build/` (135 MB de binarios Debug/Release)
- [ ] **0.6 Arreglar `.vscode/tasks.json`**
  - [ ] 26 tareas apuntan a rutas inexistentes: `Prowl.Editor/Prowl.Editor.csproj`, `Samples/BananaMan`, `Samples/PhysicsTesterDemo`
  - [ ] Actualizar a `Zenith.Editor/Zenith.Editor.csproj` y a los samples reales

**Criterio de cierre:** `release.yml` compila, `ci.yml` pasa en un PR, `git status` limpio sin basura, `git check-ignore` funciona para `.kilo/`.

---

# FASE 1 — IDENTIDAD: el fork parece un clon descuidado

> **Estimación:** medio día. **Impacto:** alto (es lo primero que ve cualquier persona).

- [ ] **1.1 Reescribir `README.md`** (hoy es 100% el de Prowl: 0 "Zenith", 31 "Prowl")
  - [ ] Logo/badges propios (lenguaje, versión, license, issues — apuntar a `zzRinexD/ZenithEngine`)
  - [ ] Descripción de Zenith como fork de Prowl (atribución clara)
  - [ ] Instrucciones de build de **este** repo (`Zenith.sln`, .NET 10)
  - [ ] Cifra real de tests (~1.500 métodos, no "450+")
  - [ ] Quitar enlace a `CONTRIBUTING.md` del upstream (no existe aquí — o crearlo, ver Fase 7)
  - [ ] Sección de licencia → enlazar el `LICENSE` local, no el del upstream
- [ ] **1.2 Completar el rename Prowl → Zenith**
  - [ ] `.editorconfig`: override `EMBA002` con ruta obsoleta `Prowl.Runtime/Audio/Native/*.cs` → `Zenith.Runtime/Audio/Native/*.cs`
  - [ ] Verificar `RootNamespace`/`AssemblyName` en todos los `.csproj` (commit `5c7eb543` los restauró a `Prowl.*` — decidir política y unificar)
  - [ ] Buscar restos: `grep -r "Prowl\.Editor/" --include="*.json" --include="*.yml"` en configs
- [ ] **1.3 `LICENSE`**: añadir línea de copyright del fork ("portions Copyright (c) 2026 Zenith"), **mantener** la de Michael Sakharov (MIT obliga)
- [ ] **1.4 Reubicar documentación duplicada**
  - [ ] `REPORTE_INICIALIZACION.md` (raíz) → `docs/`
  - [ ] Fusionar con `docs/WORKFLOW_1_ARRANQUE.md` (se solapan)

**Criterio de cierre:** `grep -ri "prowl" README.md` → 0 enlaces al upstream como si fueran propios; rename verificado en configs.

---

# FASE 2 — ESTABILIDAD: bugs reales de datos y rendimiento

> **Estimación:** 2–3 días. **Impacto:** alto (bugs latentes que afectan a usuarios reales).

- [ ] **2.1 `EditorSettings.Save()` atómico + debounce** (RIESGO DE PÉRDIDA DE DATOS)
  - [ ] Escritura no atómica en `EditorSettings.cs:129` (`File.WriteAllText`) → patrón temp-file+rename ya usado en `MetaFile.cs:57`
    - *Riesgo:* crash a mitad de escritura → settings corruptos → `Load()` cae a defaults → siguiente `Save()` **sobrescribe el tema del usuario**
  - [ ] Debounce de escrituras en `EditorApplication.cs:254-274`: handlers `Move`/`Resize`/`StateChanged` llaman `Save()` en **cada evento** (arrastrar la ventana = escritura continua)
  - [ ] Debounce en `PreferencesPanel.cs`: 28 llamadas `s.Save()` + `ApplyTheme()` (que reconstruye fuentes) **por cada tick de slider**
  - [ ] Debounce en `GUI/EditorGUI.cs:145-167`: `SaveSettings()` en cada cambio de valor
- [ ] **2.2 Fix clave de caché de variantes de shader** — `Rendering/Shaders/ShaderPass.cs:97-110`
  - [ ] La clave concatena keywords en el orden de enumeración del `Dictionary` → materiales con el mismo set en distinto orden generan claves distintas → **compilan el mismo programa 2 veces**
  - [ ] Normalizar (orden fijo o HashSet) + cachear la string por material
  - [ ] Revisar `Insert(0, ...)` O(n²) en `:117-128` (copias completas del shader por keyword en cache-miss)
- [ ] **2.3 Fix asignación GC por batch** — `Rendering/RenderPipeline.cs:667`
  - [ ] `new[] { TextureImageFormat.Color44b }` se materializa **por batch con GrabTexture, por pase, por cámara, por frame** → mover a `static readonly`
- [ ] **2.4 Fix `AfterOpaques` + `ReplaceSceneColor`** — `Rendering/DefaultRenderPipeline.cs:358-371`
  - [ ] El bloque `AfterOpaques` nunca llama `GetReplacedRTs()` (solo lo hace `PostProcess` en `:404-410`) → RT huérfano → a los 3 frames: *"RenderTexture leak detected!"* **cada frame** (`RenderTexture.cs:271`) + `LogError` **cada frame** (`:161-162`)
  - [ ] `DefaultRenderPipeline.cs:161-162`: `Debug.LogError` → `Debug.LogErrorOnce` (consistente con `Scene.cs:907,931`)
  - [ ] Documentar ownership en `RenderContext.cs:38` ("Only allowed during PostProcess stage" no dice quién libera)
- [ ] **2.5 Vaciar los 46 `catch {}` vacíos** (21% de los 214 `catch` del Editor)
  - [ ] Mínimo: `Debug.LogError` en cada uno; revisar especialmente los de rutas de persistencia (`EditorApplication.cs:1376`, `Undo.cs:918,926`)
- [ ] **2.6 `Undo.FindGO` O(registros × objetos)** — `Core/Undo.cs:940-950`
  - [ ] Recorre todos los `RootObjects` por cada lookup, **dentro de bucles** (`PerformUndo:594-604`, `EndContinuous:491-524`) → diccionario por GUID
- [ ] **2.7 `_undoSkipFields` frágil** — `Undo.cs:869-881`
  - [ ] `HashSet<string>` con nombres de campo del runtime (`"_identifier"`, `"<IsDisposed>k__BackingField"`) → renombrar un campo en `Zenith.Runtime` rompe el undo **en silencio** → reemplazar por atributo `[UndoIgnore]` o contrato explícito
- [ ] **2.8 Documentar el invariante FIFO** en `RenderTexture.ReleaseTemporaryRT` (`RenderTexture.cs:227-255`)
  - [ ] La reutilización en el mismo frame es segura solo por el orden FIFO del render thread; hoy está documentado en `Game.cs:199-203` y `RenderPipeline.cs:682-687` pero **no en el método que rompe la abstracción**
- [ ] **2.9 Revisar doble catch** en `MonoBehaviour.cs:418-483` + `SceneDispatcher.cs:313-340` (mensaje casi idéntico impreso 2 veces; el catch exterior solo es útil si el interior lanza)

**Criterio de cierre:** 0 `catch {}` vacíos, settings a prueba de crashes, sin leaks de RT alcanzables, sin allocs por batch.

---

# FASE 3 — TESTING: de 6,6 a 10

> **Estimación:** 1–2 semanas. **Impacto:** alto (es la mitad de la nota global).

- [ ] **3.1 Tests de UI runtime** (hoy **0 tests** en 53 archivos, ~300 KB de código)
  - [ ] Prioridad: `RectTransform` (13 KB), `EventSystem` (20 KB), `UIInputField` (19 KB), `UIRaycaster` (13 KB), `UIScrollRect` (20 KB), `LayoutGroup` (10 KB), `UIMeshBuilder` (27 KB)
- [ ] **3.2 Tests de Input Actions** (hoy **0 tests** en 14 archivos)
  - [ ] `InputAction` (19 KB), `InputActionMap` (14 KB), composites (WASD → Float2), action phases, bindings/processors
- [ ] **3.3 Tests del pipeline de render** (hoy **0 tests**; los más pesados del Runtime)
  - [ ] `RenderPipeline` (38 KB), `DefaultRenderPipeline` (31 KB), `CommandExecutor` (46 KB), `CommandBuffer` (32 KB)
  - [ ] Enfoque headless: orden de pasos, state sorting, comandos — sin GPU (existe precedente: `HeadlessGraphicsTests`)
  - [ ] Image effects: SSR, GTAO, VolumetricFog, Bloom (lógica CPU de configuración)
- [ ] **3.4 Tests de GUI del editor** (hoy ~0 de 103 archivos)
  - [ ] Extender `EditorTestHarness.cs` (hoy se excluye *por diseño*: "never instantiate EditorApplication... Graphics.GL is unguarded")
  - [ ] Modo "sin GPU" para paneles puros: búsqueda/filtrado/orden en `ProjectPanel` y `HierarchyPanel`
- [ ] **3.5 Analyzers sin tests**
  - [ ] `PROWLEO001`/`PROWLEO002` (`EngineObjectNullAnalyzer`): **0 tests** — añadir (los `PROWLMB*` ya tienen 11 end-to-end)
  - [ ] Aplicar los analyzers a los proyectos de test (hoy no los referencian)
- [ ] **3.6 Medir cobertura** (hoy: **0** de cualquier tipo)
  - [ ] `coverlet.collector` + `.runsettings` + badge en README
  - [ ] Umbrales explícitos por proyecto
- [ ] **3.7 Rehabilitar paralelismo de tests**
  - [ ] Hoy `DisableTestParallelization = true` en **ambos** ensamblados (estado global estático) → suite completa en serie
  - [ ] Aislar estado o paralelizar por categoría/colección
- [ ] **3.8 Ampliar alcance**
  - [ ] Tests de regresión visual (golden images) para post-procesado: hoy **0** apariciones de `screenshot`/`golden`
  - [ ] `PROWLEO*` benchmarks: hoy 0, solo 3 `Stopwatch`
  - [ ] Subsistemas huérfanos: `NavMeshPrimitives` (53 KB, 0 tests pese a ser NavMesh lo más testeado), `MultiValueDictionary` (53 KB), `ShaderParser` (38 KB), `ClayBackedImporter` (33 KB), partículas (`CollisionModule`, `EmissionModule`)

**Criterio de cierre:** cobertura medida ≥70% en Runtime, UI/input/render con tests, gate de cobertura en CI.

---

# FASE 4 — CI/CD MADURO: de 4,4 a 10

> **Estimación:** 2–3 días (ejecutable en paralelo con Fase 3). **Impacto:** alto.

- [ ] **4.1 `ci.yml` en cada PR** (si no se hizo en 0.2): build + test en Windows y Ubuntu
- [ ] **4.2 Nullable de verdad**
  - [ ] `TreatWarningsAsErrors=true` en `Zenith.Runtime` y `Zenith.Editor`
  - [ ] **Retirar progresivamente `NoWarn 8600;8601;8618;8602;8603;8604;8625`** (hoy anulan los avisos de null en ambos proyectos grandes)
  - [ ] Quitar `1591` (XML docs ausentes) solo si se decide no generar docs, no como silencio permanente
- [ ] **4.3 Configuración de build reproducible**
  - [ ] `global.json` con pin de SDK (`10.0.100`)
  - [ ] `Directory.Packages.props` (versionado centralizado de paquetes; hoy solo existe `Directory.Build.props` con un número)
- [ ] **4.4 Gate de cobertura en CI** (fallar PR bajo umbral)
- [ ] **4.5 Endurecer `.editorconfig`** (hoy casi todo `suggestion`, solo 2 reglas duras de 231 líneas)
  - [ ] Subir reglas de estilo/importación/naming clave a `warning`
  - [ ] `EnforceCodeStyleInBuild=true`
- [ ] **4.6 Seguridad en CI**: CodeQL o `dotnet format --verify-no-changes`
- [ ] **4.7 Actualizar README** con badges de CI/cobertura

**Criterio de cierre:** ningún PR se mergea sin build+test+cobertura verdes; 0 warnings.

---

# FASE 5 — RENDIMIENTO DEL EDITOR: de 6,4 a 10

> **Estimación:** 3–4 días. **Impacto:** medio-alto (sensación de fluidez).

- [ ] **5.1 `HierarchyPanel`** — el mayor costo por frame
  - [ ] `OnGUI` = **432 líneas** que reconstruyen el árbol **cada frame** (`:295-298` crea `new List<TreeNode>` por frame)
  - [ ] `BuildNodeList` es **O(n²)** con búsqueda activa (`:512-560` llama `GetChildrenDeep().Any(...)` por nodo)
  - [ ] `go.Identifier.ToString()` por nodo por frame (`:521` y otra vez en `:366`)
  - [ ] Solución: caché invalidado por versión de escena/selección (modelo ya usado en `ProjectPanel.cs:1527-1549`)
- [ ] **5.2 Reducir churn GC en `GUI\`**
  - [ ] 252 operaciones LINQ (top: `PrefabUtility` 43, `HierarchyPanel` 27, `ProjectPanel` 24)
  - [ ] **1.505 interpolaciones `$"..."`** dentro de `GUI\`
  - [ ] 89 `new List<>`/`.ToList()` dentro de `GUI\`
- [ ] **5.3 `EditorGUI.Settings*` con debounce** de `EditorSettings.Save()` (ver 2.1)
- [ ] **5.4 Persistencia de estado de paneles** (hoy solo **3 de 13** implementan `SerializeState/RestoreState`)
  - [ ] Persistir: carpeta abierta, filtros de búsqueda, expansión del árbol, scroll, tamaño de thumbnail, pestaña activa
  - [ ] Existentes: `InspectorPanel` (selección), `SceneViewPanel` (cámara), `SpriteEditorWindow`

**Criterio de cierre:** sin I/O de disco por frame, sin O(n²) en la jerarquía, estado de paneles sobrevive al reinicio.

---

# FASE 6 — DEUDA DE ARQUITECTURA: de 6,9 a 10

> **Estimación:** 1–2 semanas, incremental. **Impacto:** medio (mantenibilidad a largo plazo).

- [ ] **6.1 Romper el objeto dios `Core/EditorApplication.cs`** (1.802 líneas, ~60 métodos, **9 responsabilidades**)
  - [ ] Extraer: `Core/EditorResources.cs` (líneas 807-859, usadas 20/16/13 veces por el resto)
  - [ ] Extraer: `Core/EditorDialogs.cs` (`OpenFileDialog`)
  - [ ] Extraer: `Core/PlayModeService.cs` (líneas 1414-1668, play mode completo)
  - [ ] Extraer: `Core/LayoutPersistence.cs` (líneas 1315-1400)
  - [ ] Extraer: intro/animación (líneas 859-1027)
  - [ ] *Justificación:* 66 archivos dependen de `EditorApplication` por estas utilidades mal ubicadas
- [ ] **6.2 Partir archivos gigantes**
  - [ ] `ProjectPanel.cs` (1.669 líneas, **50 métodos**: árbol, grid, tabla, drag&drop, menús, caché)
  - [ ] `GUI/CustomEditors/GameObjectInspector.cs` (1.603 líneas)
  - [ ] `HierarchyPanel.cs` (1.389), `TerrainEditor.cs` (1.079), `SpriteEditorWindow.cs` (1.051)
- [ ] **6.3 Eliminar singletons `static` de paneles** (bug de estado compartido **ya ocurrido**)
  - [ ] Evidencia: `ConsolePanel.cs:49-51` documenta *"a shared static cache lets two open tabs stomp each other's filtered view"*
  - [ ] Riesgos vivos: `ProjectPanel.Instance` (asignado `:152`, nunca limpiado), `SceneViewPanel.ActiveCamera` (cada frame en `:87`), `TerrainEditor.ActiveInstance` (12 campos `public static`), `BuildSettingsPanel.Instance`
  - [ ] Nota: 1.791 campos `static` en total — revisar los de UI al menos
- [ ] **6.4 Romper ciclos de namespace** (invisibles al compilador, todo en 1 ensamblado)
  - [ ] `GUI↔Core` (42 y 4 refs), `Projects↔Core`, `Projects↔GUI` (19 y 8 refs)
- [ ] **6.5 Deduplicación concreta**
  - [ ] Constantes de tema duplicadas: `RowHeight/FontSize/Spacing/Padding/Roundness/TabBarHeight` existen en `EditorTheme.cs:211-273` **y** `EditorThemeData.cs:122-141`
  - [ ] Chromo de barra de búsqueda copiado en **8 sitios** con métricas distintas (130/150/160 px × 24/25 px): `AssetDatabasePanel:242`, `ConsolePanel:191`, `ProjectPanel:292`, `HierarchyPanel:503`, `PreferencesPanel:653`, `WidgetPlaygroundPanel:284`, `SelectorModal:145`, `MenuTreePopup:134`
  - [ ] `ToolbarHeight` inconsistente: `HierarchyPanel:39` = 30f vs `ProjectPanel:100` = 34
  - [ ] ~30 líneas casi idénticas entre `CaptureCreatedObject` (`Undo.cs:359-397`) y `RegisterDestroyObject` (`:417-455`)
  - [ ] Los 2 `TODO` duplicados `Destroy vs Dispose` (`Undo.cs:370,453` y `HierarchyPanel.cs:1181`)
- [ ] **6.6 Tokenizar valores hardcodeados**
  - [ ] **95 `Color.FromArgb` fuera de `Theming\`** (de 110 totales): `ProjectLauncher` 17, `ProjectPanel` 16, `EditorGuide` 6, `InputActionMapEditor` 6 → bypass del sistema de tokens
  - [ ] 49 tamaños de fuente hardcodeados `.FontSize(8..12f)`
  - [ ] 379 tamaños mágicos `.Width(n)/.Height(n)` (top: `GameObjectInspector` 30, `ProjectPanel` 29, `ProjectLauncher` 26)
  - [ ] Tokens de profundidad hardcodeados: `Neutral100/200/500`, `Ink100/200` (`EditorTheme.cs:293-351`) junto a tokens delegados
  - [ ] Niveles fantasma del ramp: `Purple500` y `Purple700` mapean al mismo stop (igual en Blue/Red/Green/Amber)
- [ ] **6.7 Limpieza de API/comentario**
  - [ ] `RenderContext.Dispose()` → **0 llamadores** en todo el repo (API muerta; contexts se crean sin `using` en `DefaultRenderPipeline.cs:360,391`)
  - [ ] Comment rot: `EditorTheme.cs:244` dice `"(Gradient, Solid)"` pero `EditorBackgroundStyle` solo tiene `Color`
  - [ ] `EditorSettings.ApplyTheme()` escribe ~30 campos `static` mutables (`:95-114`)
  - [ ] `EditorSettings.Instance => _instance ??= Load()` lazy **sin lock** (hoy salva que el primer acceso es main thread)
- [ ] **6.8 Métodos >120 líneas**: 29 en total (ver lista: `HierarchyPanel.OnGUI` 432, `WidgetPlaygroundPanel.OnGUI` 314, `SceneViewPanel.DrawViewport` 277...)

**Criterio de cierre:** ningún archivo >1.000 líneas sin justificación, 0 singletons de panel, 0 ciclos de namespace, 0 valores fuera de tokens.

---

# FASE 7 — DOCUMENTACIÓN Y COMUNIDAD: de 5,9 a 10

> **Estimación:** 3–4 días. **Impacto:** medio.

- [ ] **7.1 Documentación ausente**
  - [ ] `CONTRIBUTING.md` (el README lo enlaza pero no existe)
  - [ ] `ARCHITECTURE.md` (mapa de proyectos: Runtime/Editor/Analyzers/Players/Samples)
  - [ ] `CHANGELOG.md` (keep-a-changelog desde el fork)
  - [ ] `docs/README.md` (índice: hoy los 5 `WORKFLOW_*` no tienen entrada)
- [ ] **7.2 Convertir `WORKFLOW_1-5` de auditorías congeladas a docs vivas**
  - [ ] Contienen referencias a líneas que **ya cambiaron** tras los 31 commits locales
  - [ ] `WORKFLOW_5_FRICCION.md` declara explícitamente: "Basado UNICAMENTE en los 4 documentos… No se ha leido codigo fuente" → rehacer con código
  - [ ] `WORKFLOW_5` (24 KB) mezcla diagnóstico temporal con documentación
- [ ] **7.3 Unificar convenciones**
  - [ ] Commits: hoy se mezclan `feat:`/`Feat:`/`Fix:`/`Chore:`/`Tweak:` y español+inglés → Conventional Commits estricto, 1 idioma
  - [ ] Comentarios: ~54 en español mezclados con inglés (`EditorPaths.cs` **completo en español**, `EditorApplication.cs:1776-1793`, layout por defecto)
  - [ ] 3 commits históricos metieron basura con código (`d08f2dab`, `5c7eb543`, `d5f036d1`) → regla en CONTRIBUTING: nunca commitear artefactos de debug
- [ ] **7.4 Issue templates con branding Zenith** (hoy son los genéricos de GitHub)

**Criterio de cierre:** docs con índice, sin referencias rotas, convención única.

---

# FASE 8 — PRESCINDIBLE / BONIFICACIÓN (opcional)

> No afecta a la nota; solo si sobra tiempo.

- [ ] Tests de plataforma con `[SkippableFact]` por SO (hoy **0** atributos de skip/condición)
- [ ] BenchmarkDotNet formal de hot paths (hoy 0 benchmarks)
- [ ] Semantic Release / changelog automático
- [ ] Sitio web y Discord propios
- [ ] Backends gráficos adicionales (Vulkan/DX) — hoy solo OpenGL vía Silk.NET
- [ ] Optimizar `EditorIcons.cs` (189 KB) — **es código generado** (`IconFontCppHeaders.py`), no cuenta como deuda

---

# TABLA FINAL: criterios de "10/10" por área

| Área | Hoy | Meta (10/10) | Fase |
|---|---|---|---|
| CI/CD | 2 | PR build+test+cobertura + warnings-as-errors | 0, 4 |
| README/identidad | 2 | README propio, rename 100% completo | 1 |
| Higiene repo | 3 | 0 archivos basura, `.gitignore` sano | 0 |
| Cobertura de tests | 5 | UI, input, render y GUI con tests; ≥70% medida | 3 |
| Config build | 4 | `global.json` + `Directory.Packages.props` + nullable real | 4 |
| Rendimiento | 6,4 | Sin I/O por frame, sin O(n²), sin allocs por batch | 2, 5 |
| Código | 6,9 | 0 `catch {}` vacíos, sin god objects, sin ciclos | 2, 6 |
| Tests (volumen/calidad) | 6,6 | Paralelismo + golden tests + benchmarks | 3 |
| Persistencia | 6,5 | Settings atómicos + estado de los 13 paneles | 2, 5 |
| Documentación | 5,5 | CONTRIBUTING/ARCHITECTURE/CHANGELOG + docs vivas | 7 |
| Repo/commits | 5,9 | Convención única, historial limpio | 7 |
| Funcionalidad | 7,8 | Undo sin O(n²), sin API muerta | 2, 6 |
| Licencia | 9 | + copyright del fork | 1 |
| Localización | 9 | (mantener; cerrar los 2 títulos sin `Loc.Get`) | — |
| **GLOBAL** | **6,5** | **10** | |

---

## Quick wins (máxima subida de nota por hora)

1. **Fase 0 completa** (~2 h) → sube CI y higiene de golpe
2. **README propio** (~2 h) → sube identidad de 2 a ~9
3. **`EditorSettings` atómico + debounce** (~4 h) → elimina el peor riesgo de datos
4. **`ci.yml` con tests** (~3 h) → CI de 2 a ~7 de inmediato
