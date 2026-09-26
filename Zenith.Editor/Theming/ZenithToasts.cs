// Shim that intercepts all Origami toast calls and redirects them to the console.
// The class lives in a parent namespace of all callers (Prowl.Editor), so C# namespace
// resolution prefers this implementation over the external Prowl.Origami one.

using Prowl.Runtime;
using ToastType = Prowl.OrigamiUI.ToastType;

namespace Prowl.Editor;

/// <summary>
/// Console-only replacement for Origami's Toasts. All Toasts.Show() calls in the
/// editor resolve here and print to the console instead of drawing floating popups.
/// </summary>
public static class Toasts
{
    public static void Show(string title, string message, ToastType type = ToastType.Info, float duration = 3f)
    {
        string prefix = type switch
        {
            ToastType.Error   => "[Toast:Error]",
            ToastType.Warning => "[Toast:Warning]",
            ToastType.Success => "[Toast:Success]",
            _                 => "[Toast:Info]"
        };
        Debug.Log($"{prefix} {title} - {message}");
    }

    public static void Info(string title, string message, float duration = 3f) => Show(title, message, ToastType.Info, duration);
    public static void Warning(string title, string message, float duration = 3f) => Show(title, message, ToastType.Warning, duration);
    public static void Success(string title, string message, float duration = 3f) => Show(title, message, ToastType.Success, duration);
    public static void Error(string title, string message, float duration = 3f) => Show(title, message, ToastType.Error, duration);
}
