# WORKFLOW 3 — Abrir proyecto existente

## 1. Trigger
Click en un proyecto reciente (`ProjectLauncher.cs:309`) o en el boton de abrir (`ProjectLauncher.cs:367`, `543`) -> `TryOpenProject(path)` (`linea 624`). También se puede abrir con `--project` (`Program.cs:19` y `46`).

## 2. Flujo interno (ASCII)
```
ProjectLauncher.TryOpenProject(path)
  v
Project.Open(path) (linea 83)
  |- Si File.Exists(path) y termina en .prowl -> rootPath = GetDirectoryName(path)
  |- Si Directory.Exists(path) -> rootPath = path
  |- Si no -> DirectoryNotFoundException (linea 97)
  |- rootPath = GetFullPath(rootPath)
  |- Valida Assets/ existe -> InvalidOperationException si falta (linea 105)
  |- Encuentra .prowl (Directory.GetFiles)
  |- Si existe .prowl: ReadAllText -> JsonDocument.Parse -> extrae "name"
  |- Crea new Project(rootPath, name) (linea 122)
  |- EnsureDirectories() (linea 123)
  |- Si falta .prowl -> WriteProwlFile() (linea 127)
  |- Devuelve instancia Project
  v
Editor carga proyecto (SetActive por el usuario o por el launcher)
```

## 3. Orden de carga (exacto en `Project.Open`)
1. Resolver ruta (`.prowl` -> directorio; carpeta -> carpeta)
2. Normalizar (`GetFullPath`)
3. Validar `Assets/` existe (`Directory.Exists`, linea 104)
4. Buscar `.prowl` (`Directory.GetFiles`, linea 109)
5. Leer `.prowl` (`File.ReadAllText`, linea 114) -> `JsonDocument.Parse` (linea 115)
6. Extraer `name` del JSON (`TryGetProperty`, linea 116-117)
7. Instanciar `Project` (`new Project`, linea 122)
8. `EnsureDirectories()` (linea 123)
9. Si falta `.prowl`: `WriteProwlFile()` (`linea 127`)
10. Devolver `Project`

Nota: `Project.Open` NO llama `SetActive`. Eso lo hace el usuario (`ProjectLauncher`) o el sistema posteriormente.

## 4. Validaciones
- `Directory.Exists(Assets/)` obligatorio (`linea 104`).
- `File.Exists(.prowl)` opcional (`linea 87`, `109`).
- Si falta `.prowl`: se crea con datos basicos (`WriteProwlFile`).
- No hay verificacion de `engine` (`Zenith`) ni `version` (`0.0.1`) en `Project.Open`.
- `IsValidProject(path)` (`linea 156-159`) solo comprueba `Directory.Exists(path)` + `Assets/`.

## 5. Manejo de errores
- `DirectoryNotFoundException`: el path no existe (`linea 97`).
- `InvalidOperationException`: falta `Assets/` (`linea 105`).
- Si `.prowl` esta corrupto (`catch` en linea 119): se ignora y se usa el nombre del directorio (`name = Path.GetFileName(rootPath)`).
- Si falta `.prowl`: se regenera (`WriteProwlFile`).
- No hay `LogError` o `Toasts` dentro de `Project.Open` (`Project.cs` no tiene referencias a `LogError` ni `Toasts`).

Nota: NO DOCUMENTADO si hay errores adicionales en el launcher (`ProjectLauncher.TryOpenProject`) al intentar abrir proyectos invalidos.

## 6. Restauracion de estado
- Ultimo proyecto: `Project.SetActive()` (`linea 139`) actualiza `Current` (`linea 141`) y agrega a `RecentProjects` (`linea 143`) con `RootPath` y `Name` (`linea 144`).
- Ultima escena: `EditorSceneManager.SaveLastScenePath()` (`linea 279`) guarda `relativePath` en `general.LastScenePath` (`linea 285`).
- Al abrir proyecto, `EditorSceneManager` restaura la escena (`linea 208-210`): `if (!string.IsNullOrEmpty(general.LastScenePath)) { if (OpenScene(general.LastScenePath)) ... }`.
- No hay restauracion de layout de ventanas ni de dock panels documentada con grep.
- No hay migracion de version (`compatible` no encontrado en `Project.cs`).

## 7. Contador de pasos
- Clicks para retomar: 1 (`ProjectLauncher` -> click en proyecto reciente, linea 309).
- Si se usa `--project`: 0 clicks (`Program.cs:46` salta launcher).
- Decisiones del usuario: 0 (el proyecto se carga directamente).
- Pasos internos: 10 (ver orden de carga en seccion 3).
- Tiempo: NO DOCUMENTADO (no hay logs de timing en `Project.cs`).

Nota: `ProjectLauncher.IsOpen` inicia en `true` (`ProjectLauncher.cs:29`). Si el usuario no hace click, el editor muestra el launcher con los proyectos recientes.

## 8. FRICCION
- Que se podria cachear: `.prowl` se lee de disco cada vez; no hay cache en memoria del contenido JSON. `RecentProjects.AddRecent` escribe en disco (`AppData\Prowl\RecentProjects.json`) inmediatamente, lo que puede ser lento si se abren muchos proyectos seguidos.
- Pasos de validacion que faltan: No hay verificacion de que `engine` sea `"Zenith"`. No hay verificacion de que `version` sea compatible (`0.0.1` esta fija). No hay verificacion de integridad de archivos internos (`Library/metadata.db`, `Assets/`).
- Migracion de version: NO DOCUMENTADO. No hay referencias a `migrate`, `upgrade`, `version_check`, `compatible` en `Project.cs` ni en `ProjectLauncher.cs`.
- Estado de restauracion: `LastScenePath` se restaura (`EditorSceneManager`), pero no hay restauracion del ultimo panel abierto, del zoom del editor, ni del modo de edicion (`Edit` vs `Play`).
- Posible mejora: Agregar `Load()` en `Project` que lea `.prowl` en memoria antes de crear la instancia; agregar verificacion de `version` y `engine`; agregar mensaje de error claro en `ProjectLauncher` si la version del proyecto no coincide (`NO DOCUMENTADO` hoy).

---

## Reporte de comandos ejecutados
1. `Public Open` -> `Project.cs:83`
2. `TryOpenProject` -> `ProjectLauncher.cs:309`, `367`, `543`, `624`
3. `ReadAllText/Deserialize` -> `Project.cs:114` (ReadAllText, JsonDocument.Parse)
4. `LoadSettings/LoadAssetDatabase` -> NO ENCONTRADO (no en `Project.cs`)
5. `SaveLastScenePath/LastScene` -> `EditorSceneManager.cs:61`, `163`, `192`, `208`, `279`, `285`
6. `File.Exists/corrupt` -> `Project.cs:65`, `74`, `87`, `104`, `109`
7. `engine/version/check` -> NO ENCONTRADO (no en `Project.cs`)
8. `AssetDatabase/EditorAssetBackend` -> NO ENCONTRADO (no en `Project.cs`)
9. `LogError/Toasts` -> NO ENCONTRADO (no en `Project.cs`)
10. `SetActive` -> `Project.cs:139`
