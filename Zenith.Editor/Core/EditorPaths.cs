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

    /// <summary>Carpeta default donde se crean los proyectos nuevos.</summary>
    public static readonly string DefaultProjectsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Zenith Projects");

    /// <summary>Carpeta legacy con espacio (Prowl original).</summary>
    private static readonly string LegacyProjectsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Prowl Projects");

    /// <summary>Carpeta legacy sin espacio (variante en EditorSettings).</summary>
    private static readonly string LegacyProjectsFolderNoSpace = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ProwlProjects");

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

        // Avisar si hay proyectos en la carpeta legacy. NO mover nada.
        if (Directory.Exists(LegacyProjectsFolder) && !Directory.Exists(DefaultProjectsFolder))
        {
            Runtime.Debug.Log(
                $"Detected old projects folder at '{LegacyProjectsFolder}'. " +
                $"New projects will use '{DefaultProjectsFolder}'. " +
                $"Your old projects remain accessible from '{LegacyProjectsFolder}'.");
        }
    }
}
