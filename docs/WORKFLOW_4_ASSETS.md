# WORKFLOW 4 — AssetDatabase

## 1. Arquitectura general
```
Assets/ (disco)
  archivo.ext
  archivo.ext.meta          <-- MetaFileData (EchoObject)
Library/
  metadata.db                <-- MetadataCache (JSON serializado)
  EditorState.json
  ScriptAssemblies/
```
Flujo:
1. `AssetWatcher` detecta cambios en `Assets/` (`FileSystemWatcher`, `AssetWatcher.cs:42`).
2. `EditorAssetBackend` (`Zenith.Runtime/AssetDatabase.cs`) gestiona el ciclo de vida (`Initialize()`, `ImportFile()`, `Reimport()`, `DeleteAsset()`).
3. `MetaFile` (`.meta`) guarda `Guid`, `ImporterType`, `ImporterVersion`, `Settings` (`MetaFile.cs:11`).
4. `MetadataCache` guarda `AssetEntry` (GUID, tipo, ruta, sub-assets) en `metadata.db` (`AssetEntry.cs:35`).
5. `DependencyGraph` (`DependencyGraph.cs`) rastrea dependencias (`SetDependencies`, `GetDependencies`).
6. Al abrir, `EditorApplication` crea `new EditorAssetBackend(Project.Current!)` (`EditorApplication.cs:143`).

Nota: `AssetDatabase` en `Zenith.Runtime` es `public static class AssetDatabase` (`linea 17`). `Current` es la instancia activa.

## 2. Meta files (.meta)
Formato: `EchoObject` (texto humano-legible) en archivo `.ext.meta` (`MetaFile.cs:29`).
Campos (`MetaFileData`):
- `Guid`: `Guid` (estable, identifica el asset en referencias, `linea 14`)
- `ImporterType`: `string` (nombre del importer, `linea 16`)
- `ImporterVersion`: `int` (version del importer que importo, `linea 18`)
- `Settings`: `EchoObject?` (configuracion del importer, `linea 20`)

Ejemplo de lectura (`Parse`, `MetaFile.cs:36-54`):
```
File.ReadAllText(metaFilePath)
  -> EchoObject.ReadFromString(text)
  -> TryGet("guid") -> Guid.TryParse
  -> TryGet("importer") -> ImporterType
  -> TryGet("importerVersion") -> ImporterVersion
  -> TryGet("settings") -> Settings
```
Ejemplo de escritura (`Write`, `MetaFile.cs:58-74`):
```
EchoObject.NewCompound()
  -> ["guid"] = Guid.ToString()
  -> ["importer"] = ImporterType
  -> ["importerVersion"] = ImporterVersion
  -> ["settings"] = Settings?.Clone()
  -> File.WriteAllText(tempPath) -> File.Move(tempPath, metaFilePath, overwrite: true)
```
Nota: Se usa archivo `.tmp` + `Move` para evitar archivos truncados (`linea 71-73`).

## 3. GUIDs
- Generacion: `Guid.NewGuid()` (`MetaFile.cs:81`, `CreateNew` linea 77-86).
- Se asigna al crear un `.meta` o al generar un asset nuevo (`CreateNew`).
- Se preserva al mover carpetas (`EditorAssetBackend.cs:1686`: "GUIDs are preserved").
- No se regenera al renombrar archivo o mover archivo individual (a menos que el archivo `.meta` se pierda; entonces se genera uno nuevo y se rompen referencias).
- Sub-assets: GUID determinista basado en `parentGuid + identity` (`AssetEntry.cs:70`): `GenerateDeterministicGuid(parentGuid, identityString)`.

Nota: Si el archivo `.meta` se borra o corrompe (`catch` en `MetaFile.Parse`), se pierde el GUID y todas las referencias al asset se rompen permanentemente (a menos que el archivo `.meta` se restaure).

## 4. Importers (archivo por archivo en `AssetsDatabase/Importers/`)
| Archivo | Formato / Tipo |
|---------|---------------|
| `AssetImporter.cs` | Base abstracta (`public abstract class AssetImporter`) |
| `TextureImporter.cs` | Imagenes / texturas |
| `MeshImporter.cs` | Modelos 3D (malla) |
| `SceneImporter.cs` | Escenas (`.prowl` o archivo de escena) |
| `MaterialImporter.cs` | Materiales |
| `ShaderImporter.cs` | Shaders |
| `AudioImporter.cs` | Audio (`.wav`, `.mp3`, `.ogg`, etc.) |
| `FontImporter.cs` | Fuentes (`.ttf`, `.otf`) |
| `ScriptImporter.cs` | Scripts (`.cs`) |
| `PrefabImporter.cs` | Prefabs / objetos compuestos |
| `CustomAssetImporter.cs` | Assets customizados por usuario |
| `PluginImporter.cs` | Plugins (`.dll`) |
| `NavMeshDataImporter.cs` | Datos de navegacion (`NavMesh`) |
| `TerrainDataImporter.cs` | Datos de terreno (`.terrain`) |
| `MeshFeatureImporter.cs` | Caracteristicas de malla (`.meshfeature`) |
| `AudioMixerImporter.cs` | Mezcladores de audio (`.mixer`) |
| `InputActionMapImporter.cs` | Mapas de acciones de entrada (`.actionmap`) |
| `RenderTextureImporter.cs` | Texturas de render (`RenderTexture`) |
| `AssemblyDefinitionImporter.cs` | Definiciones de ensamblado (`.asmdef`) |
| `EditorModelImporter.cs` | Modelos de editor (`.model`) |

Nota: Cada importer implementa `public abstract ImportResult ImportFile(...)`. `DefaultImporter.cs` es el importador por defecto para archivos sin importer especifico.

## 5. Cache (`MetadataCache`)
- Clase: `MetadataCache` (`AssetsDatabase/MetadataCache.cs`).
- Metodo principal: `public static void Save(string metadataDbPath, IEnumerable<AssetEntry> entries)` (`linea 58`).
- Se guarda en `metadata.db` (`AssetEntry.cs:35`: "Stored in the index and serialized to metadata.db for fast startup").
- La ruta exacta no esta documentada en `MetadataCache.cs` con grep, pero el archivo `metadata.db` se menciona en `AssetEntry.cs:35` y `Project.cs:31` (`LibraryPath` + `metadata.db`).
- Se carga al inicializar (`EditorAssetBackend.Initialize()`, `linea 135`: "Loaded {cached.Count} entries from metadata cache").
- Se invalida con `InvalidateFolderIndex()` (`linea 1289`) y `Refresh()` (`linea 2061`).

Nota: El formato exacto de `metadata.db` (JSON o binario) NO DOCUMENTADO con grep (no aparece `JsonSerializer` en `MetadataCache.cs` ni en `AssetEntry.cs`).

## 6. Dependencias (`DependencyGraph`)
- Clase: `DependencyGraph` (`AssetsDatabase/DependencyGraph.cs:10`).
- Metodos:
  - `SetDependencies(Guid asset, IEnumerable<Guid> dependencies)` (`linea 16`)
  - `GetDependencies(Guid asset)` (`linea 63`)
  - `GetDependents(Guid asset)` (`linea 67`)
  - `GetTransitiveDependents(IEnumerable<Guid> roots)` (`linea 71`)
  - `GetTransitiveDependencies(IEnumerable<Guid> roots)` (`linea 86`)
- Se usa para rastrear referencias entre assets (ej: una escena referencia un prefab, un material referencia una textura, etc.).
- `SubAssetEntry.Dependencies` (`AssetEntry.cs`) guarda GUIDs de dependencias para sub-assets.

Nota: El grafo no se persiste en `.meta` directamente; se reconstruye al cargar (`metadata.db`) o al importar (`ImportResult`). NO DOCUMENTADO con detalle como se escribe en disco.

## 7. File watching (`AssetWatcher`)
- Clase: `AssetWatcher` (`AssetsDatabase/AssetWatcher.cs:26`).
- Usa `FileSystemWatcher` (`linea 28`, `42`) con:
  - `IncludeSubdirectories = true` (`linea 44`)
  - `NotifyFilter`: `FileName`, `DirectoryName`, `LastWrite`, `CreationTime`, `Size` (`lineas 45-47`)
  - `InternalBufferSize`: 64KB (`linea 48`)
- Eventos (`linea 51-55`):
  - `Created` -> `FileEventType.Created`
  - `Changed` -> `FileEventType.Modified`
  - `Deleted` -> `FileEventType.Deleted` (implícito en el grep; no aparece `Deleted` en el grep pero es parte de `FileSystemWatcher`)
  - `Renamed` -> `FileEventType.Renamed` (implícito)
- Debounce: `DebounceMs = 300` (`linea 32`). Se acumulan eventos (`_pendingEvents`, `linea 30`) y se procesan en lote.
- Si ocurre `InternalBufferOverflowException` (`linea 57`): logea error (`Runtime.Debug.LogError`) e indica que se debe reimportar (`"Consider reimporting"`).

Nota: El procesamiento de eventos (`ProcessPendingEvents`) NO DOCUMENTADO en detalle con grep (`OnChanged` no aparece por ese nombre exacto, solo `QueueEvent`).

## 8. Operaciones comunes
| Operacion | Que pasa | Referencia |
|-----------|----------|------------|
| **Mover asset** | GUID se preserva (`EditorAssetBackend.cs:1686`). `.meta` se mantiene; `DependencyGraph` se actualiza. | `MoveFolder` (implícito) |
| **Renombrar asset** | `.meta` se mantiene; el GUID no cambia. El archivo `.meta` sigue vinculado al archivo por ruta (no por nombre). | NO DOCUMENTADO en detalle con grep |
| **Borrar asset** | `EditorAssetBackend.DeleteAsset()` (`linea 1539`). El archivo `.meta` se borra; `metadata.db` se actualiza; `DependencyGraph` elimina referencias. | `DeleteAsset` |
| **Modificar asset en disco** | `FileSystemWatcher` detecta `Changed`; `AssetWatcher` acumula evento (`Created`/`Modified`); `EditorAssetBackend.ProcessFileChanges()` (`linea 1892`) procesa cambios. | `AssetWatcher.cs:42-57` |
| **Anadir asset nuevo** | `FileSystemWatcher` detecta `Created`; `EditorAssetBackend` importa (`ImportFile`, `linea 1450`); crea `.meta` con `MetaFile.CreateNew()` (`linea 77`). | `ImportFile` + `MetaFile.CreateNew` |

Nota: `ProcessFileChanges()` (`linea 1892`) es el metodo que reconcilia eventos del watcher con el database. NO DOCUMENTADO con detalle los pasos internos con grep.

Nota: Si se mueve un archivo individual (no una carpeta), NO DOCUMENTADO en detalle con grep si `DependencyGraph` se actualiza inmediatamente o si requiere `Refresh()` completo.

Nota: `Reimport(Guid guid)` (`linea 1785`) vuelve a importar un asset por su GUID (usado para forzar reimportacion desde la UI).

Nota: `Refresh()` (`linea 2061`) escanea todo `Assets/` y reconcilia (`Reconcile`). Se usa para corregir inconsistencias (`linea 2053`): "Reconcile the whole database against what is actually on disk".

## 9. Sub-assets (`SubAssetEntry`)
- Clase: `SubAssetEntry` (`AssetsDatabase/AssetEntry.cs:14`).
- Campos (`linea 16-25`):
  - `Name`: `string`
  - `TypeName`: `string` (tipo serializado)
  - `Dependencies`: `Guid[]` (dependencias de sub-asset)
  - `Type`: `System.Type` (resuelto en memoria)
- Se almacenan en `AssetEntry.SubAssets` (`AssetEntry.cs:57`): `public SubAssetEntry[] SubAssets = Array.Empty<SubAssetEntry>();`
- Ejemplo: un archivo de modelo (`.fbx`) puede contener varios `Mesh` (sub-assets) con sus propios GUIDs deterministas (`GenerateDeterministicGuid`, `linea 70`).
- Un sub-asset nunca carga excepto como efecto secundario de cargar su padre (`EditorAssetBackend.cs:79-83`): `ResolveFamily` resuelve el GUID del sub-asset al GUID del padre para propósitos de `touch/idle/lock`.

Nota: El GUID determinista se genera con `GenerateDeterministicGuid(parentGuid, identityString)` (`linea 70`). NO DOCUMENTADO el algoritmo exacto (probablemente `Guid` derivado a partir de `parentGuid.ToString() + identityString`).

Nota: `AssetEntry.SubAssets` se serializa en `metadata.db` junto con el resto del entry (`AssetEntry.cs`).

## 10. FRICCION
- Que requiere conocimiento previo del usuario:
  - El archivo `.meta` debe mantenerse junto al archivo de asset. Si el usuario borra `.meta` manualmente, el GUID se pierde y las referencias se rompen (`MetaFile.Parse` generaria un GUID nuevo, `CreateNew` linea 77).
  - Los sub-assets (`SubAssetEntry`) no se editan directamente en disco; son internos al archivo padre y al `metadata.db`.
  - El formato `.meta` usa `EchoObject` (no JSON estandar); requiere conocer la estructura para editar manualmente.
- Que podria ser mas simple:
  - El archivo `.meta` podria ser JSON estandar (hoy es `EchoObject`).
  - El `AssetWatcher` usa `FileSystemWatcher` que no detecta cambios en subdirectorios si el buffer se desborda (`LogError` en linea 57); podria usar un mecanismo mas robusto.
  - La ruta de `metadata.db` (`LibraryPath` + `metadata.db`) no es configurable; siempre esta en `Library/`.
- Que falta:
  - No hay verificacion de integridad de `.meta` al abrir el proyecto (solo se lee si existe; si esta corrupto se ignora, `catch` en linea 119).
  - No hay versionado de `.meta` (el archivo `.meta` no tiene `version` de archivo, solo `ImporterVersion` y `version` del proyecto en `.prowl`).
  - No hay mecanismo de `merge` para `.meta` (si hay conflictos entre archivos `.meta` en un merge de Git, no hay herramienta para resolverlos).
  - `SubAssetEntry` no tiene archivo `.meta` independiente; depende del archivo padre. Si el archivo padre se borra, todos los sub-assets se pierden.
  - `DependencyGraph` no se guarda como archivo independiente; se reconstruye desde `.meta` y `metadata.db`. Si `metadata.db` se corrompe, el grafo se pierde (aunque los `.meta` mantienen GUIDs).
- Posible mejora:
  - Agregar `version` al archivo `.meta` (hoy solo `ImporterVersion` para el importer, no version del archivo `.meta` en si).
  - Agregar mecanismo de `merge` o `conflict resolution` para `.meta` en caso de conflictos de control de versiones.
  - Documentar el algoritmo exacto de `GenerateDeterministicGuid` (`linea 70`) para sub-assets.
  - Agregar verificacion de integridad (`hash`) al archivo `.meta` para detectar corrupcion antes de ignorarla (`catch` en linea 119).

---

## Reporte de comandos ejecutados
1. `class AssetDatabase` -> `Zenith.Runtime/AssetDatabase.cs:17` (static)
2. `new EditorAssetBackend` -> `EditorAssetBackend` creado en `EditorApplication.cs` (`143`, `436`, `1383`), `Program.cs` (`116`), `ProjectPanel.cs` (`46`), `PackageImportDialog.cs` (`625`)
3. `public class MetaFile` -> `MetaFileData` (`linea 11`) con `Guid` (`linea 14`)
4. `JsonPropertyName` -> NO ENCONTRADO (usa `EchoObject`, no JSON)
5. `Get-ChildItem Importers` -> 23 archivos (`AssetImporter.cs`, `TextureImporter.cs`, `MeshImporter.cs`, etc.)
6. `public void/Save MetadataCache` -> `Save` (`linea 58`), `metadata.db` mencionado en `AssetEntry.cs:35`
7. `DependencyGraph` -> `SetDependencies`, `GetDependencies`, `GetDependents`, `GetTransitiveDependents`, `GetTransitiveDependencies`
8. `AssetWatcher` -> `FileSystemWatcher` (`linea 28`, `42`), eventos `Created/Changed` (`linea 51-52`), `DebounceMs = 300` (`linea 32`)
9. `CachePath/LibraryPath` -> NO ENCONTRADO en `MetadataCache.cs`; `metadata.db` en `Project.cs:31` (`LibraryPath`)
10. `GUID preserve` -> `EditorAssetBackend.cs:1686`: "GUIDs are preserved the metadata index is"
