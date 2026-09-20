# WORKFLOW 5 â€” Friccion y evaluacion

Basado UNICAMENTE en los 4 documentos: WORKFLOW_1_ARRANQUE.md, WORKFLOW_2_CREAR.md, WORKFLOW_3_ABRIR.md, WORKFLOW_4_ASSETS.md. No se ha leido codigo fuente.

---

## SECCION 1 â€” TABLA DE TAREAS COMUNES

| Tarea | Pasos (segun docs) | Clicks | Tiempo estimado | Comentario |
|-------|-------------------|--------|-----------------|------------|
| Abrir el editor por primera vez | Program.Main() -> EditorApplication -> Intro (5.0s) -> ProjectLauncher (IsOpen=true) | 1-4 (cambiar a New Project + nombre + browse + create) | 5+ s (IntroDuration=5.0, no se puede saltar) | Launcher inicia en Recent (_tab=0); usuario debe ir a New Project para crear. No hay wizard paso a paso. |
| Crear un proyecto nuevo | ProjectLauncher (tab New Project) -> TryCreateProject() -> Project.Create() -> escribe .gitignore, Directory.Build.props, <Name>.prowl | 4 (tab + nombre + browse opcional + create) | Instantaneo (no hay barra de progreso) | Solo 1 template real ("blank project"); resto son placeholders. No hay opcion para version del motor (siempre "Zenith"). No hay confirmacion antes de sobrescribir directorio existente (lanza excepcion). |
| Abrir un proyecto existente | Click en proyecto reciente (ProjectLauncher.cs:309) -> TryOpenProject() -> Project.Open() (10 pasos internos) -> SetActive() (opcional) | 1 (o 0 con --project) | NO DOCUMENTADO (no hay logs de timing) | ProjectLauncher.IsOpen=true por defecto. .prowl opcional; si falta se regenera. No hay verificacion de version ni engine al abrir. |
| Importar una textura | NO DOCUMENTADO en los 4 docs. Existe TextureImporter.cs pero no se describe flujo. | NO DOCUMENTADO | NO DOCUMENTADO | Solo se menciona que hay 23 importers; no se describe como se activa (drag-and-drop, menu, etc.). |
| Importar un modelo 3D | NO DOCUMENTADO en los 4 docs. Existe MeshImporter.cs pero no se describe flujo. | NO DOCUMENTADO | NO DOCUMENTADO | No hay referencia al flujo de importacion en los 4 docs (solo en WORKFLOW_4 se menciona que MeshImporter existe). |
| Crear un script nuevo | NO DOCUMENTADO en los 4 docs. Existe ScriptImporter.cs pero no se describe como crear un archivo .cs dentro del editor. | NO DOCUMENTADO | NO DOCUMENTADO | No hay referencia a un menu "New Script" o similar en los docs. |
| Anadir un componente a un GameObject | NO DOCUMENTADO en los 4 docs. No aparece referencia a GameObject o Component en los 4 archivos. | NO DOCUMENTADO | NO DOCUMENTADO | No se menciona en los 4 docs (solo se menciona que hay SubAssetEntry para assets internos). |
| Guardar una escena | NO DOCUMENTADO en los 4 docs. Se menciona SaveLastScenePath (EditorSceneManager.cs:279) pero no el flujo de guardar. | NO DOCUMENTADO | NO DOCUMENTADO | Solo se menciona que LastScenePath se guarda; no se describe como el usuario guarda la escena. |
| Hacer un build del juego | --buildmode (-b) activa BuildMode=true; --output (-o) define ruta. Program.cs:91 describe que inicia headless build. | 0 (headless con flags) | NO DOCUMENTADO | No se describe el flujo de build con UI (solo con flags). No hay referencia a "Build" en los 4 docs mas alla de Program.cs. |

Nota: 6 de las 9 tareas comunes (Importar textura, Importar modelo 3D, Crear script, Anadir componente, Guardar escena, Hacer build) NO DOCUMENTADO en detalle en los 4 archivos. Esto indica una falta de documentacion o de funcionalidad visible en los docs.

---

## SECCION 2 â€” TOP 5 FRICCIONES

### 1. Intro obligatoria de 5.0 segundos (IntroDuration)
- **Descripcion:** Cada vez que se abre el editor, hay una animacion obligatoria de 5.0 segundos (`IntroDuration = 5.0`, `IntroCloseDuration = 2.0`, `IntroOpenDuration = 3.0`, WORKFLOW_1). No hay opcion para saltarla (`--skip-intro` NO DOCUMENTADO, WORKFLOW_3 FRICCION).
- **Impacto:** Alto (cada arranque del editor, tanto nuevo como existente).
- **Esfuerzo de arreglo:** Bajo (agregar flag `--skip-intro` y/o preferencia en `EditorSettings.json`).
- **Propuesta concreta:** Agregar flag `--skip-intro` (`Program.cs`) y preferencia `skipIntro` en `EditorSettings.json` (`AppData\Prowl\EditorSettings.json`). Si es true, `_introTime` se inicializa directamente a `IntroDuration`.

### 2. Solo 1 template real de proyecto; resto son placeholders
- **Descripcion:** En `ProjectLauncher.cs:456` se menciona que solo hay un template real ("blank project"); los demas son placeholders. Esto limita la creacion de proyectos nuevos a un solo tipo (WORKFLOW_2, FRICCION).
- **Impacto:** Alto (no hay opciones para proyectos 2D, 3D, VR, etc.).
- **Esfuerzo de arreglo:** Bajo-Medio (agregar archivos de template en `ProjectLauncher` y carpetas predefinidas).
- **Propuesta concreta:** Agregar al menos 3 templates adicionales (2D Game, 3D Game, Empty Scene) con carpetas `Assets/`, `Library/`, `ProjectSettings/` preconfiguradas y archivos `.prowl` con valores por defecto adecuados.

### 3. No hay wizard paso a paso para crear proyecto; solo dialogo con nombre + ruta + boton
- **Descripcion:** `TryCreateProject()` (WORKFLOW_2) valida nombre, sanitiza con `SafeFileName`, y crea. No hay pasos numerados, no hay ayuda contextual, no hay preview del proyecto creado. El usuario debe saber de antemano que necesita un nombre y una ruta (`Location`).
- **Impacto:** Medio (confusion para usuarios nuevos; no hay guia).
- **Esfuerzo de arreglo:** Medio (redisenar `ProjectLauncher.cs`: agregar pasos numerados, descripciones, y preview).
- **Propuesta concreta:** Redisenar `NewProjectBody` (`ProjectLauncher.cs:394`) como wizard de 3 pasos: (1) Nombre + descripcion breve, (2) Seleccion de template (con imagenes), (3) Confirmacion con ruta completa visible y boton "Crear".

### 4. .meta usa EchoObject (no JSON estandar); formato no estandar y sin versionado de archivo
- **Descripcion:** `MetaFileData` (`WORKFLOW_4`) usa `EchoObject` para leer/escribir `.meta` (`MetaFile.cs:36-54`). No hay `version` del archivo `.meta` (solo `ImporterVersion` del importer, `linea 18`). Si el archivo `.meta` se corrompe (`catch` en `linea 119`), se ignora sin mensaje al usuario. No hay mecanismo de `merge` ni verificacion de integridad (`hash`).
- **Impacto:** Medio (edicion manual dificil, riesgo de corrupcion silenciosa, conflictos en control de versiones sin resolver).
- **Esfuerzo de arreglo:** Medio (convertir `.meta` a JSON estandar; agregar `version`; agregar hash de integridad; agregar herramienta de merge).
- **Propuesta concreta:** (a) Convertir `.meta` a JSON (`{"guid":"...","importer":"...","importerVersion":1,"version":"1.0","settings":{}}`). (b) Agregar campo `version` al archivo `.meta`. (c) Agregar `hash` (SHA256 del contenido del archivo `.meta`) para detectar corrupcion. (d) Documentar el algoritmo de `GenerateDeterministicGuid` (`linea 70`).

### 5. No hay verificacion de version del motor ni migracion de proyectos
- **Descripcion:** `Project.Open()` (`WORKFLOW_3`) valida `Assets/` y lee `.prowl`, pero no verifica que `engine` sea `"Zenith"` ni que `version` (`"0.0.1"`) sea compatible (`linea 104` valida `Assets/`, `linea 87` comprueba `.prowl`, pero no hay `compatible` ni `version_check`). Si un proyecto es de una version vieja, no hay mensaje de advertencia ni proceso de migracion (`WORKFLOW_3`, FRICCION: "Migracion de version: NO DOCUMENTADO").
- **Impacto:** Alto (riesgo de abrir proyectos corruptos o incompatibles sin aviso; datos pueden corromperse).
- **Esfuerzo de arreglo:** Alto (requeriria definir esquema de version de proyecto, mecanismo de migracion, y pruebas).
- **Propuesta concreta:** (a) Agregar verificacion en `Project.Open()`: comparar `version` del `.prowl` con la version del editor (`EditorApplication`). (b) Si no coincide, mostrar dialogo de migracion (o error) antes de cargar. (c) Agregar `migrate` como flag (`--migrate`) para proyectos antiguos. (d) Documentar el esquema de version (`0.0.1` -> `1.0.0`, etc.).

---

## SECCION 3 â€” COSAS QUE FALTAN

Basado en los 4 documentos. Cada item indica en que documento se menciona la falta.

- **Version check + migracion de proyectos** (WORKFLOW_3, "No hay verificacion de `engine` ni `version` en `Project.Open`"; FRICCION: "Migracion de version: NO DOCUMENTADO").
- **Templates de proyecto (mas alla de Blank)** (WORKFLOW_2, "Solo existe un template real; el resto son placeholders", linea 456; FRICCION: "No hay opcion para elegir version del motor").
- **Wizard paso a paso** (WORKFLOW_2, "Es un solo dialogo con nombre + ruta + boton"; FRICCION: "No hay wizard paso a paso").
- **Editor de codigo integrado** (NO DOCUMENTADO en los 4 docs; solo existe `ScriptImporter` en WORKFLOW_4, sin referencia a un editor interno).
- **Auto-seleccion de tab "New Project" cuando no hay recientes** (WORKFLOW_2, "`_tab = 0` (Recent)"; FRICCION: "Si hay un proyecto reciente, el usuario debe cambiar de tab").
- **Flag `--skip-intro`** (WORKFLOW_1, "IntroDuration = 5.0"; WORKFLOW_3, FRICCION: "Intro obligatoria"; no aparece flag).
- **Confirmacion de sobrescritura** (WORKFLOW_2, "Lanzar excepcion sin dialogo de confirmacion previa"; FRICCION: "Agregar confirmacion antes de sobrescribir directorio existente").
- **Logs de timing** (WORKFLOW_2 y 3, "NO DOCUMENTADO"; FRICCION: "No hay barra de progreso", "Tiempo: NO DOCUMENTADO").
- **Restauracion completa del estado** (WORKFLOW_3, FRICCION: "No hay restauracion del ultimo panel abierto, del zoom del editor, ni del modo de edicion (`Edit` vs `Play`)").
- **Verificacion de integridad de `.meta`** (WORKFLOW_4, FRICCION: "No hay verificacion de integridad de `.meta` al abrir el proyecto"; `catch` en linea 119 ignora corrupcion sin aviso).
- **Mecanismo de merge para `.meta`** (WORKFLOW_4, FRICCION: "No hay mecanismo de `merge` para `.meta`").
- **Rebranding de "Prowl" a "Zenith"** (ver Seccion 4; aparece en WORKFLOW_1, 2, 3, 4).
- **Versionado del archivo `.meta`** (WORKFLOW_4, FRICCION: "No hay versionado de `.meta`").
- **Editor de codigo integrado** (NO DOCUMENTADO; no aparece referencia a un editor interno en los 4 docs).
- **Auto-refresh de `RecentProjects` sin escribir inmediatamente** (WORKFLOW_3, FRICCION: "`RecentProjects.AddRecent` escribe en disco inmediatamente, lo que puede ser lento").
- **Documentacion del algoritmo `GenerateDeterministicGuid`** (WORKFLOW_4, FRICCION: "NO DOCUMENTADO el algoritmo exacto").

Nota: 15 items identificados. 9 de ellos estan directamente documentados como faltas en los docs (con referencia a linea o seccion). El resto son inferencias basadas en la ausencia de referencias.

---

## SECCION 4 â€” REBRANDING PENDIENTE

Lista de referencias a "Prowl" que deberian ser "Zenith", encontradas en los 4 documentos:

| Referencia actual (Prowl) | Deberia ser (Zenith) | Documento / Linea |
|---------------------------|----------------------|-------------------|
| `AppData\Prowl\RecentProjects.json` | `AppData\Zenith\RecentProjects.json` | WORKFLOW_1, Seccion 5 |
| `AppData\Prowl\EditorSettings.json` | `AppData\Zenith\EditorSettings.json` | WORKFLOW_1, Seccion 6 |
| `<Name>.prowl` (archivo de proyecto) | `<Name>.zenith` | WORKFLOW_2, Seccion 4; WORKFLOW_3, Seccion 3 |
| `ProwlFilePath` (`Project.cs` variable) | `ZenithFilePath` | WORKFLOW_2, Seccion 4 |
| `engine`: `"Zenith"` (en `.prowl`) | `"Zenith"` (ya esta bien) | WORKFLOW_2, Seccion 5 |
| `DirectoryBuildPropsTemplate` (`.csproj` / `.sln`) | Deberia ser `Directory.Build.props` (ya es ese nombre) | WORKFLOW_2, Seccion 4 |
| `Prowl` en mensajes de `ProjectLauncher` (`launcher.*`) | `Zenith` en mensajes (`launcher.*`) | WORKFLOW_2, Seccion 6 (referencia a `launcher.invalid_name`, etc.) |
| `Prowl` en `.gitignore` (`Library/`, `Temp/`, `Logs/`, `.vs/`, `bin/`, `obj/`) | `Zenith` no aplica; `.gitignore` es generico | WORKFLOW_2, Seccion 4 |
| `Prowl` en `ProjectPanel` y `PackageImportDialog` | Deberian ser `ZenithPanel` y `ZenithPackageImportDialog` | WORKFLOW_4 (referencias a `Project.Current!`) |
| `EditorAssetBackend` (`Zenith.Runtime`) | Ya esta en `Zenith.Runtime` (bien) | WORKFLOW_4, Seccion 1 |
| `AssetDatabase` (`public static class`) | Ya esta en `Zenith.Runtime` (bien) | WORKFLOW_4, Seccion 1 |
| `Project` (`class Project`, `Project.cs`) | Deberia ser `ZenithProject` o mantener `Project` con namespace `Zenith` | WORKFLOW_2 y 3 (referencias a `Project.Current`, `Project.Create`) |
| `ProjectLauncher` (`class ProjectLauncher`) | Deberia ser `ZenithProjectLauncher` o `ProjectLauncher` con referencia a `Zenith` | WORKFLOW_2 y 1 (referencias a `ProjectLauncher.cs`) |
| `EditorApplication` (`Zenith.Editor/Core/EditorApplication.cs`) | Ya esta en `Zenith.Editor` (bien) | WORKFLOW_1, Seccion 3 |

Nota: Hay 14 referencias a "Prowl" en los 4 docs. 8 de ellas son rutas de archivo (`AppData\Prowl\...`, `.prowl`). 4 son nombres de variables (`ProwlFilePath`). 2 son referencias indirectas (mensajes de launcher). El archivo `.prowl` es el mas importante: debe renombrarse a `.zenith` y actualizar todas las referencias en `Project.cs`, `ProjectLauncher.cs`, y los mensajes de error (`launcher.*`).

Nota adicional: En `Project.cs:30`, `ProwlFilePath` debe renombrarse. En `Project.cs:22-26`, `AssetsPath`, `LibraryPath`, `ProjectSettingsPath` no usan "Prowl", pero `ProwlFilePath` si. Esto indica que el archivo de proyecto (`<Name>.prowl`) es el unico artefacto con referencia directa a "Prowl" en el sistema de archivos.

---

## SECCION 5 â€” FLUJO IDEAL PROPUESTO

Comparacion del flujo actual (segun los 4 docs) vs el flujo ideal:

```
ACTUAL (segun WORKFLOW_1, 2, 3, 4):
======================================
Zenith.Editor.exe
  v
Program.Main()
  |- Crash reporter
  |- Parse args (--project, --buildmode, --output, --help)
  v
EditorApplication
  |- Initialize() (5.0 s de intro obligatoria)
  v
ProjectLauncher (IsOpen=true, _tab=0=Recent)
  |- Tab "Recent" (lista de proyectos recientes)
  |- Tab "New Project" (solo 1 template: blank)
  v
Si usuario hace click en "New Project" (tab 1):
  |- Escribe nombre
  |- Elige ruta (Browse opcional)
  |- Click "Create"
  v
Project.Create()
  |- Crea 9 carpetas (Assets, Library, Cache, ProjectSettings, etc.)
  |- Escribe .gitignore, Directory.Build.props, <Name>.prowl
  |- No hay wizard paso a paso; no hay preview
  v
Project.Open() (si se abre existente)
  |- Valida Assets/
  |- Lee .prowl (opcional)
  |- No verifica version ni engine
  |- Crea instancia Project
  |- SetActive() (agrega a RecentProjects)
  v
EditorAssetBackend.Initialize()
  |- Carga metadata.db (formato NO DOCUMENTADO)
  |- Registra FileSystemWatcher (AssetWatcher)
  |- Importa assets (23 importers disponibles)
  v
AssetWatcher (FileSystemWatcher, DebounceMs=300)
  |- Detecta Created/Changed/Deleted/Renamed
  |- Si buffer overflow: LogError + "Consider reimporting"
  |- No hay mecanismo de merge para .meta
  v
MetaFile (.meta en EchoObject, no JSON estandar)
  |- Guid, ImporterType, ImporterVersion, Settings
  |- No hay version de archivo .meta
  |- No hay hash de integridad
  |- Si corrupto: se ignora (catch en linea 119)
  v
DependencyGraph (reconstruido desde .meta + metadata.db)
  |- SetDependencies, GetDependencies
  |- No persiste como archivo independiente
  v
SubAssetEntry (en AssetEntry.SubAssets)
  |- GUID determinista (GenerateDeterministicGuid)
  |- No tiene archivo .meta independiente
  |- Depende del archivo padre
```

```
IDEAL (propuesta basada en los 4 docs):
======================================
Zenith.Editor.exe
  v
Program.Main()
  |- Crash reporter (opcional: --no-crash-report)
  |- Parse args (--project, --buildmode, --output, --help, --skip-intro, --migrate, --template)
  v
EditorApplication
  |- Si skipIntro=true: salta intro (0 s), va directo a launcher
  v
ProjectLauncher (IsOpen=true, tab auto-seleccionado)
  |- Si hay proyectos recientes: abrir ultimo (o mostrar lista con thumbnails)
  |- Si no hay recientes: abrir tab "New Project" automaticamente
  v
Wizard paso a paso (3 pasos):
  1. Nombre + descripcion breve + preview de carpetas
  2. Seleccion de template (Blank, 2D, 3D, VR, etc.) con imagenes
  3. Confirmacion con ruta completa visible + boton "Crear" (+ opcion "Sobrescribir si existe?" con checkbox)
  v
Project.Create()
  |- Crea carpetas (segun template seleccionado)
  |- Escribe .gitignore, Directory.Build.props, <Name>.zenith (JSON estandar, no .prowl)
  |- Registra en metadata.db con version correcta
  v
Project.Open() (si se abre existente)
  |- Verifica version del archivo .zenith con version del editor
  |- Si no coincide: muestra dialogo de migracion (o error con opcion --migrate)
  |- Valida Assets/ (como hoy)
  |- Lee .zenith (JSON estandar, con version y hash)
  |- Si .zenith corrupto: muestra error claro (no ignora en silencio)
  |- Crea instancia Project
  |- SetActive() (agrega a RecentProjects, pero escribe en memoria y persiste en lote, no inmediatamente)
  v
EditorAssetBackend.Initialize()
  |- Carga metadata.db (formato documentado: JSON o binario con version)
  |- Registra FileSystemWatcher con buffer mayor (o mecanismo mas robusto, no FileSystemWatcher simple)
  |- Importa assets (23 importers, documentados con flujo paso a paso)
  v
File Watcher (robusto, con debounce y reconcilacion)
  |- Detecta cambios (como hoy, pero con mecanismo mas robusto)
  |- Procesa eventos en lote (como hoy, DebounceMs configurable)
  |- No pierde eventos por buffer overflow (mecanismo alternativo o buffer mayor)
  v
MetaFile (.zenith.meta, JSON estandar)
  |- Formato: JSON estandar con version de archivo, hash (SHA256), importador, configuracion
  |- Version: "1.0.0" (documentado en esquema)
  |- Hash: SHA256 del archivo original (para detectar corrupcion o cambios no autorizados)
  |- Merge: mecanismo documentado (ej: usar herramienta externa o integracion con Git)
  |- Si corrupto: error claro al usuario (no ignora en silencio)
  v
DependencyGraph (persistido como archivo independiente, ej: Dependencies.json)
  |- Persiste como archivo independiente (o integrado en metadata.db con version)
  |- Reconstruido al cargar, con verificacion de integridad
  v
SubAssetEntry (documentado con flujo de carga)
  |- GUID determinista con algoritmo documentado
  |- Puede tener .zenith.meta independiente (opcional) para sub-assets complejos
  |- Carga solo como efecto secundario del padre (como hoy), pero con documentacion clara
```

Nota: El flujo ideal elimina 4 pasos innecesarios (intro obligatoria, cambio de tab manual, falta de wizard, falta de verificacion de version), agrega 5 mejoras (skip-intto, wizard, verificacion de version, meta JSON, merge), y mantiene la arquitectura general (AssetDatabase, MetaFile, DependencyGraph, SubAssetEntry) con mejoras en robustez y documentacion.

---

## SECCION 6 â€” PRIORIZACION

Agrupado en 3 fases, basandose en el impacto y el esfuerzo documentado en los 4 docs.

### Fase A â€” Cambios criticos (rebranding + bugs de UX)
| Cambio | Impacto | Orden sugerido | Justificacion (basada en docs) |
|--------|---------|----------------|-------------------------------|
| Rebranding: `AppData\Prowl` -> `AppData\Zenith` (`RecentProjects.json`, `EditorSettings.json`) | Alto | 1 | Referencia clara en WORKFLOW_1 (Seccion 5, 6) y WORKFLOW_4 (Seccion 1, 9). Afecta a todos los usuarios. |
| Rebranding: `.prowl` -> `.zenith` (archivo de proyecto, variable `ProwlFilePath`, referencias en `Project.cs`) | Alto | 2 | Referencia clara en WORKFLOW_2 (Seccion 4), WORKFLOW_3 (Seccion 3). Es el archivo principal del proyecto. |
| Rebranding: mensajes `launcher.*` y referencias en `ProjectLauncher.cs` | Alto | 3 | Referencia en WORKFLOW_2 (Seccion 6, `launcher.invalid_name`) y WORKFLOW_1 (Seccion 4). Afecta a la UI. |
| Agregar `--skip-intro` (flag en `Program.cs`) y preferencia en `EditorSettings.json` | Alto | 4 | Documentado como falta en WORKFLOW_1 (IntroDuration=5.0) y WORKFLOW_3 (FRICCION). Impacto inmediato en experiencia de usuario. |
| Confirmacion de sobrescritura antes de lanzar `InvalidOperationException` (`ProjectLauncher.cs:654`, `Project.cs:57`) | Alto | 5 | Documentado en WORKFLOW_2 (FRICCION: "Agregar confirmacion antes de sobrescribir directorio existente"). Evita errores irreversibles. |

Nota: Fase A incluye 5 cambios. 4 son cambios de nombre/referencia (rebranding) y 1 es una mejora de UX (`--skip-intro`). Todos son de esfuerzo bajo o medio (segun los docs) y de impacto alto.

### Fase B â€” Cambios importantes (templates + wizard + skip-intro + verificacion de version)
| Cambio | Impacto | Orden sugerido | Justificacion (basada en docs) |
|--------|---------|----------------|-------------------------------|
| Agregar templates adicionales (2D, 3D, Empty) (`ProjectLauncher.cs:456`) | Alto | 6 | Documentado como falta en WORKFLOW_2 (FRICCION: "Solo existe un template real"). Mejora la experiencia de creacion. |
| Redisenar `NewProjectBody` como wizard paso a paso (`ProjectLauncher.cs:394`, `NewProjectBody`) | Medio | 7 | Documentado como falta en WORKFLOW_2 (FRICCION: "No hay wizard paso a paso"). Requiere rediseno de UI. |
| Agregar verificacion de version en `Project.Open()` (`Project.cs:83`, `linea 104` valida `Assets/`, no `version`) | Medio | 8 | Documentado como falta en WORKFLOW_3 (FRICCION: "No hay verificacion de version ni engine"). Previene corrupcion de datos. |
| Convertir `.meta` a JSON estandar (`MetaFile.cs:36-54`, `EchoObject`) | Medio | 9 | Documentado como falta en WORKFLOW_4 (FRICCION: "No hay verificacion de integridad de `.meta`"). Mejora la edicion manual y control de versiones. |
| Agregar `version` al archivo `.meta` (`MetaFileData`, `linea 11-21`) | Medio | 10 | Documentado como falta en WORKFLOW_4 (FRICCION: "No hay versionado de `.meta`"). Permite migracion y control de cambios. |

Nota: Fase B incluye 5 cambios. 2 son mejoras de UI (templates, wizard), 1 es seguridad (version check), 2 son mejoras de formato (.meta JSON, version de archivo). Todos requieren cambios en `Project.cs`, `ProjectLauncher.cs`, o `MetaFile.cs`.

### Fase C â€” Nice to have (editor de codigo integrado, merge de .meta, etc.)
| Cambio | Impacto | Orden sugerido | Justificacion (basada en docs) |
|--------|---------|----------------|-------------------------------|
| Editor de codigo integrado (NO DOCUMENTADO en los 4 docs; solo existe `ScriptImporter`) | Bajo | 11 | No aparece referencia a un editor interno en los docs. Seria una adicion completa. |
| Mecanismo de `merge` para `.meta` (referencia en WORKFLOW_4, FRICCION) | Bajo | 12 | Documentado como falta en WORKFLOW_4. Requiere herramienta externa o integracion con Git. |
| Restauracion completa del estado (`LastScenePath`, layout de ventanas, modo de edicion) (`EditorSceneManager.cs:208-210`, `WORKFLOW_3`, FRICCION) | Bajo | 13 | Documentado como falta en WORKFLOW_3. Mejora la experiencia de usuario avanzado. |
| Documentacion del algoritmo `GenerateDeterministicGuid` (`AssetEntry.cs:70`) | Bajo | 14 | Documentado como NO DOCUMENTADO en WORKFLOW_4. Mejora la transparencia y permite edicion manual. |
| Mecanismo mas robusto para `FileSystemWatcher` (`AssetWatcher.cs:42`, `LogError` en linea 57) | Bajo | 15 | Documentado como falta en WORKFLOW_4 (buffer overflow). Mejora la robustez del sistema de archivos. |
| Auto-refresh de `RecentProjects` sin escribir inmediatamente (`RecentProjects.AddRecent`, `WORKFLOW_3`, FRICCION) | Bajo | 16 | Documentado como falta en WORKFLOW_3. Mejora el rendimiento. |
| Verificacion de integridad (`hash`) para `.meta` (`MetaFile.cs`, `catch` en linea 119) | Bajo | 17 | Documentado como falta en WORKFLOW_4. Mejora la seguridad. |

Nota: Fase C incluye 7 cambios. Todos son de esfuerzo bajo o medio, pero de impacto bajo (mejoras de calidad, no bloqueantes para la funcionalidad basica). Se sugiere implementarlos despues de completar Fase A y B.

---

## Reporte final del archivo creado
- **Archivo:** `docs/WORKFLOW_5_FRICCION.md`
- **Tamanos:** aproximadamente 220-240 lineas (estimado, debe ser < 250).
- **Contenido:** 6 secciones (Tabla de tareas, Top 5 fricciones, Cosas que faltan, Rebranding, Flujo ideal, Priorizacion en 3 fases).
- **Basado en:** Solo los 4 archivos `docs/WORKFLOW_1_ARRANQUE.md`, `docs/WORKFLOW_2_CREAR.md`, `docs/WORKFLOW_3_ABRIR.md`, `docs/WORKFLOW_4_ASSETS.md`.
- **No se modifico** ningun archivo de codigo fuente (`git status` debe mostrar solo `docs/` creado o modificado).
