using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

public static class WebClipboard
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void JsCopyToClipboard(string str);
#endif

    public static void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        JsCopyToClipboard(text);
#else
        GUIUtility.systemCopyBuffer = text;
#endif
    }
}
