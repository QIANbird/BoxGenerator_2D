using System;
using UnityEngine;

[Serializable]
public class GenerationRequestData
{
    [Header("User Input")]
    public string Prompt;

    [Header("Input File Paths")]
    public string PromptTextPath;
    public string ReferenceImagePath;
    public string MaskImagePath;

    [Header("Output File Paths")]
    public string ResultImagePath;

    [Header("Output Settings")]
    public int OutputWidth;
    public int OutputHeight;

    [Header("Optional AI Parameters")]
    public string ModelName;
    public int Seed = -1;
    public int Steps = 0;
    public float GuidanceScale = 0f;

    [Header("Metadata")]
    public string RequestId;
    public string CreatedAt;
}