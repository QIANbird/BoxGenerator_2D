using UnityEngine;
using UnityEngine.Rendering;

// Chest3DGenerator 是 3D 原型阶段的运行时装配器。
// 它负责把参数数据转换成场景中的可见对象：
// 1. 从 ChestParameterState 取得一份参数副本。
// 2. 调用 ChestMeshFactory 生成 Body / Lid / Locker 的 Mesh。
// 3. 为每个 Mesh 创建 GameObject、MeshFilter、MeshRenderer 和材质。
// 4. 可选地调整预览摄像机，让生成结果自动进入画面。
//
// 这个脚本不直接计算顶点，也不保存长期模型资产。
// 顶点几何由 ChestMeshFactory 负责；它这里只做“装配”和“显示”。
public class Chest3DGenerator : MonoBehaviour
{
    [Header("References")]
    // 参数来源。通常挂在 BoxGenerator3DRoot 上。
    [SerializeField] private ChestParameterState parameterState;

    // 生成出的 GeneratedChest 会挂在这个节点下面。
    // 如果不手动指定，就默认挂在当前 GameObject 下。
    [SerializeField] private Transform targetRoot;

    // 用于预览生成结果的摄像机。
    // 如果不手动指定，就尝试使用 Camera.main。
    [SerializeField] private Camera previewCamera;

    [Header("Generation")]
    // 是否在 Start 时自动生成一次，方便打开 BoxGenerator3D 场景后直接看到模型。
    [SerializeField] private bool generateOnStart = true;

    // 参数使用的是设计单位，例如 width = 300。
    // unitScale 把设计单位缩放到 Unity 世界单位，默认 300 -> 3。
    [SerializeField] private float unitScale = 0.01f;

    // 运行时生成根节点的名字。重新生成时会按这个名字查找并清理旧对象。
    [SerializeField] private string generatedRootName = "GeneratedChest";

    [Header("Preview Camera")]
    // 生成后是否根据模型包围盒自动调整摄像机。
    [SerializeField] private bool fitCameraOnGenerate = true;

    // 摄像机相对模型中心的观察方向。当前是一个偏 3/4 视角的方向。
    [SerializeField] private Vector3 cameraDirection = new Vector3(4f, 3f, -6f);

    // 摄像机正交尺寸的留白倍率，值越大模型在画面里越小。
    [SerializeField] private float cameraPadding = 1.25f;

    [Header("Materials")]
    // 可以在 Inspector 中指定材质；若为空，则运行时创建简单预览材质。
    [SerializeField] private Material bodyMaterial;
    [SerializeField] private Material lidMaterial;
    [SerializeField] private Material lockerMaterial;

    // 运行时默认材质颜色，主要用于低模原型阶段区分部件。
    [SerializeField] private Color bodyColor = new Color(0.72f, 0.52f, 0.36f, 1f);
    [SerializeField] private Color lidColor = new Color(0.50f, 0.68f, 0.45f, 1f);
    [SerializeField] private Color lockerColor = new Color(0.95f, 0.74f, 0.32f, 1f);

    // 当前生成出的根节点缓存。重新生成和计算 bounds 时会用到。
    private Transform generatedRoot;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        if (generateOnStart)
        {
            Generate();
        }
    }

    // 手动或自动触发完整生成流程。
    // 也可以在组件右键菜单中执行 "Generate Chest"。
    [ContextMenu("Generate Chest")]
    public void Generate()
    {
        ResolveReferences();

        ChestLatentParams parameters = parameterState != null
            ? parameterState.CreateParamsCopy()
            : new ChestLatentParams();

        ClearGeneratedRoot();
        generatedRoot = CreateGeneratedRoot();

        CreateMeshPart("Body", ChestMeshFactory.CreateBodyMesh(parameters), GetBodyMaterial());
        CreateMeshPart("Lid", ChestMeshFactory.CreateLidMesh(parameters), GetLidMaterial());
        CreateMeshPart("Locker", ChestMeshFactory.CreateLockerMesh(parameters), GetLockerMaterial());

        if (fitCameraOnGenerate && previewCamera != null)
        {
            FitCameraToGeneratedChest();
        }
    }

    // 清理上一次生成的模型，避免重复生成时堆叠对象和临时 Mesh。
    // 也可以在组件右键菜单中执行 "Clear Generated Chest"。
    [ContextMenu("Clear Generated Chest")]
    public void ClearGeneratedRoot()
    {
        Transform root = GetTargetRoot();
        Transform existingRoot = root.Find(generatedRootName);

        if (existingRoot == null)
        {
            generatedRoot = null;
            return;
        }

        ReleaseMeshes(existingRoot);
        DestroyGeneratedObject(existingRoot.gameObject);
        generatedRoot = null;
    }

    // 自动补齐场景引用，让脚本直接挂到原型根节点上也能运行。
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

        if (previewCamera == null)
        {
            previewCamera = Camera.main;
        }
    }

    private Transform GetTargetRoot()
    {
        return targetRoot != null ? targetRoot : transform;
    }

    // 创建一个可替换的生成根节点，所有部件都挂在它下面。
    // 缩放放在根节点上，这样 MeshFactory 仍然可以使用原始设计单位。
    private Transform CreateGeneratedRoot()
    {
        Transform root = new GameObject(generatedRootName).transform;
        root.SetParent(GetTargetRoot(), false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one * Mathf.Max(0.0001f, unitScale);
        return root;
    }

    // 为单个宝箱部件创建 GameObject，并绑定 Mesh 与材质。
    private void CreateMeshPart(string partName, Mesh mesh, Material material)
    {
        GameObject part = new GameObject(partName);
        part.transform.SetParent(generatedRoot, false);

        MeshFilter meshFilter = part.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;
    }

    private Material GetBodyMaterial()
    {
        if (bodyMaterial == null)
        {
            bodyMaterial = CreateRuntimeMaterial("Chest Body Material", bodyColor);
        }

        return bodyMaterial;
    }

    private Material GetLidMaterial()
    {
        if (lidMaterial == null)
        {
            lidMaterial = CreateRuntimeMaterial("Chest Lid Material", lidColor);
        }

        return lidMaterial;
    }

    private Material GetLockerMaterial()
    {
        if (lockerMaterial == null)
        {
            lockerMaterial = CreateRuntimeMaterial("Chest Locker Material", lockerColor);
        }

        return lockerMaterial;
    }

    // 创建低模预览材质。
    // URP 项目优先使用 URP/Lit；如果项目环境变化，则逐级降级到可用 shader。
    private Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = FindPreviewShader();
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

        // 双面显示，降低早期原型阶段因为法线/绕序不一致导致的不可见风险。
        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.25f);
        }

        return material;
    }

    private Shader FindPreviewShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Sprites/Default");
    }

    // 根据生成模型的 bounds 自动调整正交摄像机。
    // 这样改参数导致模型尺寸变化时，也能尽量保持模型在画面中央。
    private void FitCameraToGeneratedChest()
    {
        if (!TryGetGeneratedBounds(out Bounds bounds))
        {
            return;
        }

        previewCamera.orthographic = true;

        Vector3 direction = cameraDirection.sqrMagnitude > 0.0001f
            ? cameraDirection.normalized
            : new Vector3(4f, 3f, -6f).normalized;

        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
        float distance = Mathf.Max(radius * 4f, 8f);

        previewCamera.transform.position = bounds.center + direction * distance;
        previewCamera.transform.rotation = Quaternion.LookRotation(bounds.center - previewCamera.transform.position, Vector3.up);
        previewCamera.orthographicSize = radius * Mathf.Max(1f, cameraPadding);
        previewCamera.nearClipPlane = 0.01f;
        previewCamera.farClipPlane = distance + radius * 4f;
    }

    // 合并所有生成部件的 Renderer bounds，用于相机适配。
    private bool TryGetGeneratedBounds(out Bounds bounds)
    {
        bounds = default;

        if (generatedRoot == null)
        {
            return false;
        }

        Renderer[] renderers = generatedRoot.GetComponentsInChildren<Renderer>();

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

    // 手动销毁临时 Mesh，避免在编辑器反复生成时留下无主资源。
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

    // 兼容运行时和编辑器右键菜单：
    // Play 模式下用 Destroy，编辑器非运行状态下用 DestroyImmediate。
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
