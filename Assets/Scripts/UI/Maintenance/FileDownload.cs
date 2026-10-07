using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Gives a file made at runtime to the user:
///   WebGL  -> normal browser download (FileDownload.jslib)
///   Editor -> Save dialog, then shows the file
///   other  -> written to Application.persistentDataPath
/// </summary>
public static class FileDownload
{
    public const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void FileDownload_Save(string name, byte[] data, int length, string mime);
#endif

    /// <summary>Returns where it went (file name in WebGL, full path elsewhere), or null when cancelled / failed.</summary>
    public static string Save(string fileName, byte[] data, string mime)
    {
        if (data == null || data.Length == 0) return null;

#if UNITY_WEBGL && !UNITY_EDITOR
        FileDownload_Save(fileName, data, data.Length, mime);
        return fileName;
#elif UNITY_EDITOR
        string extension = Path.GetExtension(fileName).TrimStart('.');
        string path = UnityEditor.EditorUtility.SaveFilePanel("Save report", "", fileName, extension);
        if (string.IsNullOrEmpty(path)) return null;
        File.WriteAllBytes(path, data);
        UnityEditor.EditorUtility.RevealInFinder(path);
        return path;
#else
        try
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllBytes(path, data);
            Debug.Log("[FileDownload] Saved " + path);
            return path;
        }
        catch (IOException e)
        {
            Debug.LogWarning("[FileDownload] " + e.Message);
            return null;
        }
#endif
    }

    /// <summary>Removes characters not allowed in file names.</summary>
    public static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "file";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }
}
