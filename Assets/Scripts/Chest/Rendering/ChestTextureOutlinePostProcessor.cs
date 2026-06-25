using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class ChestTextureOutlinePostProcessor : MonoBehaviour
{
    private const string DepthNormalShaderName = "Hidden/Chest/DepthNormalEncode";
    private const string OutlineShaderName = "Hidden/Chest/DepthNormalOutline";

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int MainTexTexelSizeId = Shader.PropertyToID("_MainTex_TexelSize");
    private static readonly int NearClipId = Shader.PropertyToID("_NearClip");
    private static readonly int FarClipId = Shader.PropertyToID("_FarClip");
    private static readonly int DepthNormalTexId = Shader.PropertyToID("_DepthNormalTex");
    private static readonly int LineColorId = Shader.PropertyToID("_LineColor");
    private static readonly int DepthSensitivityId = Shader.PropertyToID("_DepthSensitivity");
    private static readonly int NormalSensitivityId = Shader.PropertyToID("_NormalSensitivity");
    private static readonly int EdgeThresholdId = Shader.PropertyToID("_EdgeThreshold");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int FlipYId = Shader.PropertyToID("_FlipY");

    [Header("Shaders")]
    [SerializeField] private Shader depthNormalShader;
    [SerializeField] private Shader outlineShader;

    [Header("Outline")]
    [SerializeField] private Color lineColor = Color.black;
    [SerializeField] private float depthSensitivity = 18f;
    [SerializeField] private float normalSensitivity = 4f;
    [SerializeField] private float edgeThreshold = 0.08f;
    [SerializeField] private float edgeSoftness = 0.02f;
    [SerializeField] private float thickness = 1.25f;

    [Header("Depth Normal Pass")]
    [SerializeField] private Color depthNormalClearColor = new Color(0.5f, 0.5f, 1f, 1f);

    [Header("Composite")]
    [SerializeField] private bool flipRenderTextureY = true;

    private Material depthNormalMaterial;
    private Material outlineMaterial;
    private RenderTexture depthNormalTexture;
    private Mesh fullscreenQuad;
    private CommandBuffer outlineCommandBuffer;
    private bool loggedMissingShaderWarning;

    public void Render(Camera camera, Transform renderRoot, RenderTexture sourceColor, RenderTexture destination)
    {
        if (camera == null || sourceColor == null || destination == null)
        {
            return;
        }

        if (renderRoot == null || !EnsureMaterials())
        {
            Graphics.Blit(sourceColor, destination);
            return;
        }

        EnsureDepthNormalTexture(destination.width, destination.height);
        RenderDepthNormal(camera, renderRoot);
        RenderOutline(sourceColor, destination);
    }

    private bool EnsureMaterials()
    {
        if (depthNormalShader == null)
        {
            depthNormalShader = Shader.Find(DepthNormalShaderName);
        }

        if (outlineShader == null)
        {
            outlineShader = Shader.Find(OutlineShaderName);
        }

        if (depthNormalShader == null ||
            outlineShader == null ||
            !depthNormalShader.isSupported ||
            !outlineShader.isSupported)
        {
            if (!loggedMissingShaderWarning)
            {
                Debug.LogWarning("Chest texture outline shaders are missing or unsupported. Falling back to raw texture preview.");
                loggedMissingShaderWarning = true;
            }

            return false;
        }

        if (depthNormalMaterial == null)
        {
            depthNormalMaterial = CreateRuntimeMaterial(depthNormalShader, "Chest Depth Normal Encode Material");
        }

        if (outlineMaterial == null)
        {
            outlineMaterial = CreateRuntimeMaterial(outlineShader, "Chest Depth Normal Outline Material");
        }

        return depthNormalMaterial != null &&
            outlineMaterial != null &&
            depthNormalMaterial.passCount > 0 &&
            outlineMaterial.passCount > 0;
    }

    private static Material CreateRuntimeMaterial(Shader shader, string materialName)
    {
        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave
        };

        return material;
    }

    private void EnsureDepthNormalTexture(int width, int height)
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);

        if (depthNormalTexture != null &&
            depthNormalTexture.width == width &&
            depthNormalTexture.height == height)
        {
            return;
        }

        ReleaseDepthNormalTexture();

        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            ? RenderTextureFormat.ARGBHalf
            : RenderTextureFormat.ARGB32;

        depthNormalTexture = new RenderTexture(width, height, 24, format)
        {
            name = "ChestTextureDepthNormalTexture",
            antiAliasing = 1,
            useMipMap = false,
            hideFlags = HideFlags.HideAndDontSave
        };

        depthNormalTexture.Create();
    }

    private void RenderDepthNormal(Camera camera, Transform renderRoot)
    {
        MeshRenderer[] renderers = renderRoot.GetComponentsInChildren<MeshRenderer>(false);

        RenderTexture previousTarget = camera.targetTexture;
        CameraClearFlags previousClearFlags = camera.clearFlags;
        Color previousBackgroundColor = camera.backgroundColor;
        Material[][] previousMaterials = new Material[renderers.Length][];

        depthNormalMaterial.SetFloat(NearClipId, camera.nearClipPlane);
        depthNormalMaterial.SetFloat(FarClipId, camera.farClipPlane);

        try
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                previousMaterials[i] = renderers[i].sharedMaterials;
                renderers[i].sharedMaterials = CreateReplacementMaterials(previousMaterials[i].Length);
            }

            camera.targetTexture = depthNormalTexture;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = depthNormalClearColor;
            camera.Render();
        }
        finally
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterials = previousMaterials[i];
                }
            }

            camera.targetTexture = previousTarget;
            camera.clearFlags = previousClearFlags;
            camera.backgroundColor = previousBackgroundColor;
        }
    }

    private Material[] CreateReplacementMaterials(int materialCount)
    {
        materialCount = Mathf.Max(1, materialCount);
        Material[] replacementMaterials = new Material[materialCount];

        for (int i = 0; i < replacementMaterials.Length; i++)
        {
            replacementMaterials[i] = depthNormalMaterial;
        }

        return replacementMaterials;
    }

    private void RenderOutline(RenderTexture sourceColor, RenderTexture destination)
    {
        outlineMaterial.SetTexture(MainTexId, sourceColor);
        outlineMaterial.SetVector(
            MainTexTexelSizeId,
            new Vector4(
                1f / Mathf.Max(1, sourceColor.width),
                1f / Mathf.Max(1, sourceColor.height),
                sourceColor.width,
                sourceColor.height));
        outlineMaterial.SetTexture(DepthNormalTexId, depthNormalTexture);
        outlineMaterial.SetColor(LineColorId, lineColor);
        outlineMaterial.SetFloat(DepthSensitivityId, Mathf.Max(0f, depthSensitivity));
        outlineMaterial.SetFloat(NormalSensitivityId, Mathf.Max(0f, normalSensitivity));
        outlineMaterial.SetFloat(EdgeThresholdId, Mathf.Max(0f, edgeThreshold));
        outlineMaterial.SetFloat(EdgeSoftnessId, Mathf.Max(0.0001f, edgeSoftness));
        outlineMaterial.SetFloat(ThicknessId, Mathf.Max(0.5f, thickness));
        outlineMaterial.SetFloat(FlipYId, flipRenderTextureY ? 1f : 0f);

        DrawFullscreen(destination, outlineMaterial);
    }

    private void DrawFullscreen(RenderTexture destination, Material material)
    {
        if (outlineCommandBuffer == null)
        {
            outlineCommandBuffer = new CommandBuffer
            {
                name = "Chest Texture Outline Composite"
            };
        }

        outlineCommandBuffer.Clear();
        outlineCommandBuffer.SetRenderTarget(destination);
        outlineCommandBuffer.ClearRenderTarget(false, true, Color.clear);
        outlineCommandBuffer.DrawMesh(GetFullscreenQuad(), Matrix4x4.identity, material, 0, 0);
        Graphics.ExecuteCommandBuffer(outlineCommandBuffer);
    }

    private Mesh GetFullscreenQuad()
    {
        if (fullscreenQuad != null)
        {
            return fullscreenQuad;
        }

        fullscreenQuad = new Mesh
        {
            name = "Chest Texture Outline Fullscreen Quad",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(-1f, 1f, 0f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };

        fullscreenQuad.RecalculateBounds();
        return fullscreenQuad;
    }

    private void OnDestroy()
    {
        ReleaseDepthNormalTexture();
        DestroyRuntimeObject(depthNormalMaterial);
        DestroyRuntimeObject(outlineMaterial);
        DestroyRuntimeObject(fullscreenQuad);

        if (outlineCommandBuffer != null)
        {
            outlineCommandBuffer.Release();
            outlineCommandBuffer = null;
        }
    }

    private void ReleaseDepthNormalTexture()
    {
        if (depthNormalTexture == null)
        {
            return;
        }

        depthNormalTexture.Release();
        DestroyRuntimeObject(depthNormalTexture);
        depthNormalTexture = null;
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
