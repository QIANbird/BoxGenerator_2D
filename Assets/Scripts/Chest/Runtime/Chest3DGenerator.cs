using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public enum ChestPreviewMode
{
    Edit,
    TextureLine
}

// Runtime assembler for the 3D chest preview.
// It builds two identical chest instances from the same ChestLatentParams:
// - Edit instance: colored low-poly material, rendered by the edit camera.
// - TextureLine instance: black/white icon material, rendered by the texture camera.
//
// The two cameras are kept in the same position, rotation, projection, and orthographic size
// so switching Editing / Preview the Texture only changes rendering style, not composition.
public class Chest3DGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ChestParameterState parameterState;
    [SerializeField] private Transform targetRoot;

    [FormerlySerializedAs("previewCamera")]
    [SerializeField] private Camera editPreviewCamera;

    [SerializeField] private Camera texturePreviewCamera;

    [Header("Generation")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private float unitScale = 0.01f;

    [FormerlySerializedAs("generatedRootName")]
    [SerializeField] private string editGeneratedRootName = "GeneratedChest_Edit";

    [SerializeField] private string textureGeneratedRootName = "GeneratedChest_Texture";

    [Header("Preview Layers")]
    [SerializeField] private string editLayerName = "ChestEditPreview";
    [SerializeField] private string textureLayerName = "ChestTexturePreview";

    [Header("Preview Camera")]
    [SerializeField] private bool fitCameraOnGenerate = true;
    [SerializeField] private Vector3 cameraDirection = new Vector3(4f, 3f, -6f);
    [SerializeField] private float cameraPadding = 1.25f;

    [Header("Edit Materials")]
    [SerializeField] private Material bodyMaterial;
    [SerializeField] private Material lidMaterial;
    [SerializeField] private Material lockerMaterial;
    [SerializeField] private Color bodyColor = new Color(0.72f, 0.52f, 0.36f, 1f);
    [SerializeField] private Color lidColor = new Color(0.50f, 0.68f, 0.45f, 1f);
    [SerializeField] private Color lockerColor = new Color(0.95f, 0.74f, 0.32f, 1f);

    [Header("Texture Line Materials")]
    [SerializeField] private Material textureBodyMaterial;
    [SerializeField] private Material textureLidMaterial;
    [SerializeField] private Material textureLockerMaterial;
    [SerializeField] private Color textureBodyColor = new Color(0.92f, 0.92f, 0.90f, 1f);
    [SerializeField] private Color textureLidColor = new Color(0.74f, 0.74f, 0.72f, 1f);
    [SerializeField] private Color textureLockerColor = new Color(0.16f, 0.16f, 0.16f, 1f);

    private Transform editGeneratedRoot;
    private Transform textureGeneratedRoot;

    public Camera EditPreviewCamera
    {
        get
        {
            ResolveReferences();
            return editPreviewCamera;
        }
    }

    public Camera TexturePreviewCamera
    {
        get
        {
            ResolveReferences();
            return texturePreviewCamera;
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateBoth();
        }
    }

    [ContextMenu("Generate Chest")]
    public void Generate()
    {
        GenerateBoth();
    }

    public void Generate(ChestPreviewMode mode)
    {
        GenerateBoth();
    }

    public void GenerateBoth()
    {
        ResolveReferences();
        ConfigurePreviewCameras();

        ChestLatentParams parameters = parameterState != null
            ? parameterState.CreateParamsCopy()
            : new ChestLatentParams();

        ClearGeneratedRoot();

        int editLayer = ResolveLayer(editLayerName, 0);
        int textureLayer = ResolveLayer(textureLayerName, 0);

        editGeneratedRoot = CreateGeneratedRoot(editGeneratedRootName, editLayer);
        textureGeneratedRoot = CreateGeneratedRoot(textureGeneratedRootName, textureLayer);

        CreateMeshPart(editGeneratedRoot, "Body", ChestMeshFactory.CreateBodyMesh(parameters), GetBodyMaterial(), editLayer);
        CreateMeshPart(editGeneratedRoot, "Lid", ChestMeshFactory.CreateLidMesh(parameters), GetLidMaterial(), editLayer);
        CreateMeshPart(editGeneratedRoot, "Locker", ChestMeshFactory.CreateLockerMesh(parameters), GetLockerMaterial(), editLayer);

        CreateMeshPart(textureGeneratedRoot, "Body", ChestMeshFactory.CreateBodyMesh(parameters), GetTextureBodyMaterial(), textureLayer);
        CreateMeshPart(textureGeneratedRoot, "Lid", ChestMeshFactory.CreateLidMesh(parameters), GetTextureLidMaterial(), textureLayer);
        CreateMeshPart(textureGeneratedRoot, "Locker", ChestMeshFactory.CreateLockerMesh(parameters), GetTextureLockerMaterial(), textureLayer);

        if (fitCameraOnGenerate && editPreviewCamera != null)
        {
            FitCameraToGeneratedChest(editPreviewCamera, editGeneratedRoot);
        }

        SyncTextureCameraToEditCamera();
    }

    [ContextMenu("Clear Generated Chest")]
    public void ClearGeneratedRoot()
    {
        ClearGeneratedRootByName(editGeneratedRootName, ref editGeneratedRoot);
        ClearGeneratedRootByName(textureGeneratedRootName, ref textureGeneratedRoot);

        if (editGeneratedRootName != "GeneratedChest" && textureGeneratedRootName != "GeneratedChest")
        {
            ClearGeneratedRootByName("GeneratedChest", ref editGeneratedRoot);
        }
    }

    private void ResolveReferences()
    {
        if (targetRoot == null)
        {
            targetRoot = transform;
        }

        if (parameterState == null)
        {
            parameterState = GetComponentInParent<ChestParameterState>();
        }

        if (editPreviewCamera == null)
        {
            editPreviewCamera = Camera.main;
        }

        if (texturePreviewCamera == null && editPreviewCamera != null)
        {
            texturePreviewCamera = CreateRuntimeTextureCamera(editPreviewCamera);
        }
    }

    private Transform GetTargetRoot()
    {
        return targetRoot != null ? targetRoot : transform;
    }

    private Camera CreateRuntimeTextureCamera(Camera sourceCamera)
    {
        GameObject cameraObject = new GameObject("ChestTexturePreviewCamera_Runtime");
        cameraObject.transform.SetParent(transform, false);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        CopyCameraPoseAndProjection(sourceCamera, camera);
        return camera;
    }

    private void ConfigurePreviewCameras()
    {
        int editLayer = LayerMask.NameToLayer(editLayerName);
        int textureLayer = LayerMask.NameToLayer(textureLayerName);

        ConfigurePreviewCamera(editPreviewCamera, editLayer, new Color(0.78f, 0.80f, 0.82f, 1f));
        ConfigurePreviewCamera(texturePreviewCamera, textureLayer, Color.white);
    }

    private static void ConfigurePreviewCamera(Camera camera, int layer, Color backgroundColor)
    {
        if (camera == null)
        {
            return;
        }

        camera.enabled = false;
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = backgroundColor;

        if (layer >= 0)
        {
            camera.cullingMask = 1 << layer;
        }
    }

    private Transform CreateGeneratedRoot(string rootName, int layer)
    {
        Transform root = new GameObject(rootName).transform;
        root.SetParent(GetTargetRoot(), false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one * Mathf.Max(0.0001f, unitScale);
        root.gameObject.layer = layer;
        return root;
    }

    private static void CreateMeshPart(Transform parent, string partName, Mesh mesh, Material material, int layer)
    {
        GameObject part = new GameObject(partName)
        {
            layer = layer
        };

        part.transform.SetParent(parent, false);

        MeshFilter meshFilter = part.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;
    }

    private Material GetBodyMaterial()
    {
        if (bodyMaterial == null)
        {
            bodyMaterial = CreateRuntimeMaterial("Chest Body Material", bodyColor, false);
        }

        return bodyMaterial;
    }

    private Material GetLidMaterial()
    {
        if (lidMaterial == null)
        {
            lidMaterial = CreateRuntimeMaterial("Chest Lid Material", lidColor, false);
        }

        return lidMaterial;
    }

    private Material GetLockerMaterial()
    {
        if (lockerMaterial == null)
        {
            lockerMaterial = CreateRuntimeMaterial("Chest Locker Material", lockerColor, false);
        }

        return lockerMaterial;
    }

    private Material GetTextureBodyMaterial()
    {
        if (textureBodyMaterial == null)
        {
            textureBodyMaterial = CreateRuntimeMaterial("Chest Texture Body Material", textureBodyColor, true);
        }

        return textureBodyMaterial;
    }

    private Material GetTextureLidMaterial()
    {
        if (textureLidMaterial == null)
        {
            textureLidMaterial = CreateRuntimeMaterial("Chest Texture Lid Material", textureLidColor, true);
        }

        return textureLidMaterial;
    }

    private Material GetTextureLockerMaterial()
    {
        if (textureLockerMaterial == null)
        {
            textureLockerMaterial = CreateRuntimeMaterial("Chest Texture Locker Material", textureLockerColor, true);
        }

        return textureLockerMaterial;
    }

    private static Material CreateRuntimeMaterial(string materialName, Color color, bool unlit)
    {
        Shader shader = unlit ? FindUnlitPreviewShader() : FindLitPreviewShader();
        Material material = new Material(shader)
        {
            name = materialName
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.18f);
        }

        return material;
    }

    private static Shader FindLitPreviewShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Sprites/Default");
    }

    private static Shader FindUnlitPreviewShader()
    {
        return Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default")
            ?? FindLitPreviewShader();
    }

    private void FitCameraToGeneratedChest(Camera camera, Transform generatedRoot)
    {
        if (camera == null || !TryGetGeneratedBounds(generatedRoot, out Bounds bounds))
        {
            return;
        }

        camera.orthographic = true;

        Vector3 direction = cameraDirection.sqrMagnitude > 0.0001f
            ? cameraDirection.normalized
            : new Vector3(4f, 3f, -6f).normalized;

        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
        float distance = Mathf.Max(radius * 4f, 8f);

        camera.transform.position = bounds.center + direction * distance;
        camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);
        camera.orthographicSize = radius * Mathf.Max(1f, cameraPadding);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = distance + radius * 4f;
    }

    private void SyncTextureCameraToEditCamera()
    {
        if (editPreviewCamera == null || texturePreviewCamera == null)
        {
            return;
        }

        int textureLayer = LayerMask.NameToLayer(textureLayerName);
        Color textureBackground = texturePreviewCamera.backgroundColor;
        CopyCameraPoseAndProjection(editPreviewCamera, texturePreviewCamera);
        ConfigurePreviewCamera(texturePreviewCamera, textureLayer, textureBackground);
    }

    private static void CopyCameraPoseAndProjection(Camera source, Camera target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.transform.position = source.transform.position;
        target.transform.rotation = source.transform.rotation;
        target.transform.localScale = source.transform.localScale;
        target.orthographic = source.orthographic;
        target.orthographicSize = source.orthographicSize;
        target.fieldOfView = source.fieldOfView;
        target.nearClipPlane = source.nearClipPlane;
        target.farClipPlane = source.farClipPlane;
    }

    private static bool TryGetGeneratedBounds(Transform root, out Bounds bounds)
    {
        bounds = default;

        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            return false;
        }

        bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return true;
    }

    private void ClearGeneratedRootByName(string rootName, ref Transform cachedRoot)
    {
        Transform root = GetTargetRoot();
        Transform existingRoot = root.Find(rootName);

        if (existingRoot == null)
        {
            if (cachedRoot != null && cachedRoot.name == rootName)
            {
                cachedRoot = null;
            }

            return;
        }

        ReleaseMeshes(existingRoot);
        DestroyGeneratedObject(existingRoot.gameObject);

        if (cachedRoot != null && cachedRoot.name == rootName)
        {
            cachedRoot = null;
        }
    }

    private void ReleaseMeshes(Transform root)
    {
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>();

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter.sharedMesh != null)
            {
                DestroyGeneratedObject(meshFilter.sharedMesh);
            }
        }
    }

    private static int ResolveLayer(string layerName, int fallbackLayer)
    {
        int layer = LayerMask.NameToLayer(layerName);

        if (layer < 0)
        {
            Debug.LogWarning($"Layer '{layerName}' is missing. Falling back to layer {fallbackLayer}.");
            return fallbackLayer;
        }

        return layer;
    }

    private void DestroyGeneratedObject(Object target)
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
