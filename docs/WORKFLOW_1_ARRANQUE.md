# WORKFLOW 1 — Arranque del editor

## 1. Entry point
`Zenith.Editor/Program.cs:92` — `public static void Main(string[] args)`.

## 2. Flags de linea de comandos
| Flag | Alias | Proposito |
|------|-------|-----------|
| `--project` | `-p` | Abre un proyecto directamente (salta launcher) |
| `--buildmode` | `-b` | Modo headless: construye y no inicia UI |
| `--output` | `-o` | Ruta de salida para `--buildmode` |
| `--help` | `-h` | Muestra comandos disponibles |

Nota: `--buildmode` activa `BuildMode = true` y `StartupProjectPath`.

## 3. Ciclo de arranque (ASCII)
```
Zenith.Editor.exe
       |
       v
Program.Main() (linea 92)
  - Instala crash reporter
  - Parsea args (--project, --buildmode, --output, --help)
  - Si BuildMode == true: headless build (no UI)
  - Si no: crea EditorApplication (linea 126)
       |
       v
editor.Run("Zenith Engine", 1920, 1080)
       |
       v
InitializeWindow -> Initialize -> BeginGui
  - Carga IntroDuration = 5.0 s (Close 2.0 + Open 3.0)
  - Muestra ProjectLauncher (IsOpen = true por defecto)
       |
       +-- Si Project.Current != null y _introTime >= 5.0 s -> cierra launcher
       |
       v
Editor visible (status bar, menu bar, dock panels)
```

## 4. ProjectLauncher
- Se muestra al arrancar (`IsOpen = true`, linea 29 de `ProjectLauncher.cs`).
- Se cierra al cargar un proyecto (`ProjectLauncher.Close()`, EditorApplication.cs:157) o cuando `_introTime >= IntroDuration` (linea 537).
- Tabs (linea 34):
  - `0` = Recent (lista de proyectos recientes)
  - `1` = New Project (crear proyecto nuevo)
- Campos de New Project (lineas 31, 421, 432): `ProjectName` (`_newProjectName`), `Location` (`BrowseLocation`), `Create` (`TryCreateProject`).
- Campos de Recent (lineas 12-15 de `RecentProjects.cs`): `Path`, `Name`, `LastOpened`, `Favorite`.

## 5. Proyectos recientes
- Archivo: `AppData\Prowl\RecentProjects.json` (Windows: `Environment.SpecialFolder.ApplicationData` + `"Prowl"` + `"RecentProjects.json"`).
- Formato: JSON.
- Clase: `RecentProjectEntry` (`Zenith.Editor/Projects/RecentProjects.cs:10`).
- Campos por entrada:
  - `Path` (string)
  - `Name` (string)
  - `LastOpened` (DateTime)
  - `Favorite` (bool)
- Limite: 20 entradas (`MaxRecent = 20`).

## 6. Settings del editor
- Archivo: `AppData\Prowl\EditorSettings.json` (Windows: `ApplicationData` + `"Prowl"` + `"EditorSettings.json"`).
- Formato: JSON (deserializa con `JsonSerializer.Deserialize<EditorSettings>`).
- Clase: `EditorSettings` (`Zenith.Editor/Theming/EditorSettings.cs:15`).
- Se carga en demanda (`Instance => Load()`); si no existe, usa valores por defecto (`new EditorSettings()`).

## 7. FRICCION
- El usuario debe tocar 2 archivos/decisiones antes de ver el editor: elegir o crear un proyecto en `ProjectLauncher`. Si usa `--project`, salta el launcher (0 decisiones).
- Conocimiento previo: si quiere crear un proyecto nuevo, debe saber un nombre y una ruta (`Location`). No hay wizard guiado mas alla del campo de texto y el boton de browse.
- Posible mejora: documentar que `--project` salta todo el launcher; agregar una opcion `--recent` o un indice para abrir el ultimo proyecto sin UI intermedia; aclarar que `AppData` es la ruta por defecto (no configurable por el usuario).

---

## Reporte de comandos ejecutados
1. `git grep ... Main` -> `Zenith.Editor/Program.cs:92`
2. `git grep ... --project` -> 4 flags encontradas
3. `git grep ... EditorApplication` -> `new EditorApplication()` y `editor.Run(...)`
4. `git grep ... IntroDuration` -> 5.0 / 3.0 / 2.0 s
5. `git grep ... public override` -> 30 lineas (InitializeWindow, Initialize, BeginGui, EndGui, etc.)
6. `git grep ... ProjectLauncher` -> usado en Close/Initialize/Draw (lineas 157-734)
7. `git grep ... RecentProjects.json` -> archivo `RecentProjects.json`
8. `git grep ... EditorSettings` -> archivo `EditorSettings.json`
9. `git grep ... GetFolderPath` -> `ApplicationData` (AppData) para ambos
10. `git grep ... RecentProjectEntry` -> Path, Name, LastOpened, Favorite
