# WORKFLOW 2 — Creacion de proyecto

## 1. Trigger
Click en "Create Project" dentro del tab `New Project` del `ProjectLauncher` (`ProjectLauncher.cs:443`, `TryCreateProject` en linea 639).

## 2. Flujo interno (ASCII)
```
ProjectLauncher.Draw()
  |_ Tab "New Project" (_tab == 1)
      |_ Click en boton Create
          v
      TryCreateProject() (linea 639)
          |_ Valida nombre (_newProjectName)
          |_ EditorUtils.SafeFileName() (linea 649)
          |_ Si existe directorio no vacio -> Error Toast (linea 654)
          |_ Project.Create(parentFolder, projectName) (Project.cs:52)
              |_ Crea directorios (Assets, Library, Cache, etc.)
              |_ Escribe .gitignore, Directory.Build.props, .prowl (JSON)
              |_ Devuelve instancia Project
          |_ Cierra launcher / carga proyecto
```

## 3. Estructura de carpetas (ASCII)
```
<ProjectRoot>/
  .gitignore
  Directory.Build.props
  <Name>.prowl
  Assets/              (AssetsPath)
  Library/             (LibraryPath)
    metadata.db        (MetadataDbPath)
    EditorState.json   (EditorStatePath)
    ScriptAssemblies/  (ScriptAssemblyPath)
  ProjectSettings/     (ProjectSettingsPath)
  Cache/
  Thumbnails/
  Packages/
  Temp/
  Logs/
```
Nota: Creado con `Directory.CreateDirectory` (lineas 164-172 de `Project.cs`).

## 4. Archivos generados
| Archivo | Proposito | Formato |
|---------|-----------|---------|
| `.gitignore` | Ignorar `Library/`, `Temp/`, `Logs/`, `.vs/`, `bin/`, `obj/`, `.csproj`, `.sln` | Texto plano |
| `Directory.Build.props` | Propiedades MSBuild (`DirectoryBuildPropsTemplate`) | XML |
| `<Name>.prowl` (`ProwlFilePath`) | Metadatos del proyecto (`name`, `engine`, `version`, `created`) | JSON |

Nota: `.gitignore` y `Directory.Build.props` solo se escriben si no existen (`File.Exists`).

## 5. Settings por defecto
En `<Name>.prowl` (JSON serializado con `JsonSerializer`):
- `name`: nombre del proyecto
- `engine`: `"Zenith"`
- `version`: `"0.0.1"`
- `created`: `DateTime.UtcNow.ToString("o")`

Nota: No hay archivo `ProjectSettings.json` generado en disco; solo existe la carpeta `ProjectSettings/`. El archivo `.prowl` actua como archivo de metadatos principal.

## 6. Validaciones y errores
| Caso | Resultado |
|------|-----------|
| Nombre vacio (`_newProjectName` vacio) | `Toasts.Show` con `launcher.invalid_name` / `launcher.name_empty` (linea 643) |
| Nombre contiene caracteres invalidos | `SafeFileName` reemplaza/sanitiza (linea 649) |
| Directorio destino no vacio (`rootPath` existe y no esta vacio) | `InvalidOperationException` lanzado (Project.cs:57) -> `Toasts.Show` (linea 654) |
| Error general en creacion (`ex.Message`) | `Toasts.Show` con `launcher.create_failed` (linea 666) |
| Error al abrir proyecto despues de crear (`ex.Message`) | `LogError` (linea 634) |

Nota: `SafeFileName` esta en `EditorUtils` (NO DOCUMENTADO en detalle con grep).

## 7. Contador de pasos
- Clicks desde abrir editor hasta proyecto creado:
  1. Click en tab "New Project" (si no esta abierto por defecto; esta abierto en `_tab = 0` por defecto, pero `ProjectLauncher.IsOpen = true`).
  2. Escribir nombre en campo.
  3. Click en "Browse" (opcional, si se quiere cambiar ruta).
  4. Click en "Create Project".
- Decisiones: 2 obligatorias (nombre + confirmar ruta por defecto o elegir otra).
- Archivos que toca el usuario: 0 (todo automatico en `Project.Create`).
- Tiempo aproximado: instantaneo (no hay wizard paso a paso, es un solo dialogo).

Nota: `ProjectLauncher` inicia con `_tab = 0` (Recent), no New Project. El usuario debe hacer click en el tab New Project (1 click extra) antes de crear.

## 8. FRICCION
- Pasos que podrian automatizarse: Si hay un proyecto reciente, el usuario debe cambiar de tab; podria abrir directamente el ultimo proyecto (`--project` salta launcher, pero no hay `--recent`).
- Falta de feedback: No hay barra de progreso; los errores aparecen como Toasts (3-5 segundos), no como mensajes permanentes en la UI.
- Cosas que faltan:
  - No hay wizard paso a paso; es un solo dialogo con nombre + ruta + boton.
  - No hay opcion para elegir version del motor (`engine` siempre es `"Zenith"`).
  - No hay tags/metadata adicionales en `.prowl` (solo nombre, version fija, fecha).
  - Solo existe un template (`"blank project"`, linea 456); el resto son placeholders.
  - No se muestra la ruta completa seleccionada claramente (solo un boton "Browse" y un campo implicito).
- Posible mejora: Agregar confirmacion antes de sobrescribir directorio existente (hoy lanza excepcion sin dialogo de confirmacion previa).

---

## Reporte de comandos ejecutados
1. `git grep ... TryCreateProject` -> `ProjectLauncher.cs:443`, `639`
2. `git grep ... Create` -> `Project.cs:52`
3. `git grep ... Directory.CreateDirectory` -> 9 carpetas (164-172)
4. `git grep ... File.WriteAllText` -> `.gitignore`, `buildProps`, `.prowl`
5. `git grep ... new.*Settings` -> NO ENCONTRADO; datos en `.prowl`
6. `git grep ... AssetsPath` -> `AssetsPath`, `LibraryPath`, `ProjectSettingsPath`, etc.
7. `git grep ... ProjectSettings` -> `ProjectSettingsAttribute` (no archivo JSON)
8. `git grep ... SafeFileName` -> `SafeFileName` + Toast `invalid_name`
9. `git grep ... template` -> solo 1 template real; placeholders
10. `git grep ... Toasts.Show` -> 4 mensajes de error/log
