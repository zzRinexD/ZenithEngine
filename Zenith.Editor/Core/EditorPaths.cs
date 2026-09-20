using System;
using System.IO;

namespace Prowl.Editor.Core;

/// <summary>
/// Rutas centralizadas de los datos del editor (settings, recientes, etc.).
/// La carpeta base se llama "Zenith" para reflejar el branding del motor.
/// </summary>
public static class EditorPaths
{
    /// <summary>Carpeta base de datos del editor en AppData: %APPDATA%\Zenith\</summary>
    public static readonly string EditorDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Zenith");

    /// <summary>Archivo de settings globales del editor.</summary>
    public static readonly string EditorSettingsFile = Path.Combine(EditorDataFolder, "EditorSettings.json");

    /// <summary>Archivo de proyectos recientes.</summary>
    public static readonly string RecentProjectsFile = Path.Combine(EditorDataFolder, "RecentProjects.json");

    /// <summary>Carpeta legacy (Prowl). Se migra a EditorDataFolder la primera vez.</summary>
    private static readonly string LegacyEditorDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Prowl");

    /// <summary>
    /// Migra datos del editor de la version anterior si es necesario.
    /// Llamar UNA VEZ al inicio del editor, ANTES de leer settings o recientes.
    /// </summary>
    public static void MigrateLegacyData()
    {
        try
        {
            bool legacyExists = Directory.Exists(LegacyEditorDataFolder);
            bool currentExists = Directory.Exists(EditorDataFolder);

            if (legacyExists && !currentExists)
            {
                Directory.CreateDirectory(EditorDataFolder);

                foreach (string file in Directory.GetFiles(LegacyEditorDataFolder))
                    File.Move(file, Path.Combine(EditorDataFolder, Path.GetFileName(file)));

                foreach (string dir in Directory.GetDirectories(LegacyEditorDataFolder))
                    Directory.Move(dir, Path.Combine(EditorDataFolder, Path.GetFileName(dir)));

                try { Directory.Delete(LegacyEditorDataFolder, false); } catch { }

                Runtime.Debug.Log($"Migrated editor data from '{LegacyEditorDataFolder}' to '{EditorDataFolder}'.");
            }
            else if (legacyExists && currentExists)
            {
                Runtime.Debug.LogWarning($"Both '{LegacyEditorDataFolder}' and '{EditorDataFolder}' exist. Using '{EditorDataFolder}'. Old folder was not modified.");
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to migrate editor data: {ex.Message}");
        }
    }
}
