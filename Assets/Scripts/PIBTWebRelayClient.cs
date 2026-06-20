using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Same-origin HTTPS transport used only by single-player WebGL. The production
/// registry relays each JSON request over a persistent raw TCP connection to PIBT.
/// </summary>
public sealed class PIBTWebRelayClient : MonoBehaviour
{
    private const string RelayPath = "/api/sessions/pibt/";

    public IEnumerator Exchange(
        string operation,
        string json,
        int timeoutSeconds,
        Action<string, string> completed)
    {
        string url = ResolveOrigin() + RelayPath + operation;
        byte[] body = Encoding.UTF8.GetBytes(json ?? string.Empty);
        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(1, timeoutSeconds);
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        string response = request.downloadHandler != null
            ? request.downloadHandler.text
            : string.Empty;
        if (request.result != UnityWebRequest.Result.Success)
        {
            string error = string.IsNullOrWhiteSpace(response) ? request.error : response;
            completed?.Invoke(null, error);
            yield break;
        }

        completed?.Invoke(response, null);
    }

    private static string ResolveOrigin()
    {
        if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri uri))
            return $"{uri.Scheme}://{uri.Authority}";
        return "http://127.0.0.1:8080";
    }
}
