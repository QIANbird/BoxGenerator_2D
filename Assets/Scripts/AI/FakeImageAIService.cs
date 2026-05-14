using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class FakeImageAIService : MonoBehaviour
{
    public IEnumerator GenerateFakeResult(
        GenerationRequestData requestData,
        float delaySeconds,
        Action<string> onSuccess,
        Action<string> onError
    )
    {
        if (requestData == null)
        {
            onError?.Invoke("GenerationRequestData is null.");
            yield break;
        }

        if (string.IsNullOrEmpty(requestData.ReferenceImagePath))
        {
            onError?.Invoke("Reference image path is empty.");
            yield break;
        }

        if (string.IsNullOrEmpty(requestData.ResultImagePath))
        {
            onError?.Invoke("Result image path is empty.");
            yield break;
        }

        if (!File.Exists(requestData.ReferenceImagePath))
        {
            onError?.Invoke($"Reference image does not exist: {requestData.ReferenceImagePath}");
            yield break;
        }

        Debug.Log("Fake AI generation started.");
        Debug.Log($"Prompt: {requestData.Prompt}");
        Debug.Log($"Reference image: {requestData.ReferenceImagePath}");

        float safeDelay = Mathf.Max(0f, delaySeconds);
        yield return new WaitForSeconds(safeDelay);

        try
        {
            string resultDirectory = Path.GetDirectoryName(requestData.ResultImagePath);

            if (!string.IsNullOrEmpty(resultDirectory))
            {
                Directory.CreateDirectory(resultDirectory);
            }

            if (File.Exists(requestData.ResultImagePath))
            {
                File.Delete(requestData.ResultImagePath);
            }

            File.Copy(requestData.ReferenceImagePath, requestData.ResultImagePath);

            Debug.Log($"Fake AI result saved: {requestData.ResultImagePath}");

            onSuccess?.Invoke(requestData.ResultImagePath);
        }
        catch (Exception e)
        {
            onError?.Invoke(e.ToString());
        }
    }
}