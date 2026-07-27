using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

// 预览模式枚举。
// Edit：编辑模式，显示带颜色和光照的低模宝箱。
// TextureLine：纹理预览模式，显示黑白线稿/图标风格的宝箱。
public enum ChestPreviewMode
{
    Edit,
    TextureLine,
    AIResult
}

// 3D 宝箱预览的运行时装配器。
// 它不直接处理 UI 输入，也不直接把 RenderTexture 显示到界面上；
// 它只负责根据 ChestLatentParams 生成 3D 模型、配置预览相机、维护当前预览旋转状态。
//
// 当前为了支持两种显示风格，会从同一套参数生成两份几何完全一致的宝箱：
// - Edit 实例：彩色低模材质，由 editPreviewCamera 渲染。
// - TextureLine 实例：黑白/线稿材质，由 texturePreviewCamera 渲染。
//
// 两台相机会保持同样的位置、旋转、投影和正交尺寸。
// 这样用户在 Editing / Preview the Texture 之间切换时，只改变渲染风格，不改变构图和观察角度。
public class Chest3DGenerator : MonoBehaviour
{
    // 预览垂直旋转的限制，避免用户把宝箱翻到过于极端的俯仰角。
    private const float MinPreviewPitch = -80f;
    private const float MaxPreviewPitch = 80f;

    [Header("References")]
    // 运行时参数状态。UI 参数面板会修改它的 CurrentParams，本脚本生成时读取其副本。
    [SerializeField] private ChestParameterState parameterState;

    // 生成出的宝箱根节点会挂到这个 Transform 下。
    // 如果没有手动指定，则使用本脚本所在 GameObject 的 transform。
    [SerializeField] private Transform targetRoot;

    // 旧字段名 previewCamera 的迁移兼容。已有场景里旧序列化字段会自动映射到 editPreviewCamera。
    [FormerlySerializedAs("previewCamera")]
    [SerializeField] private Camera editPreviewCamera;

    // 纹理线稿预览相机。没有手动指定时，会在运行时从 editPreviewCamera 复制一台。
    [SerializeField] private Camera texturePreviewCamera;

    [Header("Generation")]
    // 是否在 Start 时自动生成一次。当前 BoxGenerator3D 场景里通常由 UI 按钮触发生成。
    [SerializeField] private bool generateOnStart = true;

    // 几何工厂使用的是参数单位，例如宽 300、高 180。
    // 场景显示时用 unitScale 缩放到 Unity 世界单位，避免模型尺寸过大。
    [SerializeField] private float unitScale = 0.01f;

    // 旧字段名 generatedRootName 的迁移兼容。
    [FormerlySerializedAs("generatedRootName")]
    [SerializeField] private string editGeneratedRootName = "GeneratedChest_Edit";

    // 纹理预览实例的根节点名称。
    [SerializeField] private string textureGeneratedRootName = "GeneratedChest_Texture";

    [Header("Preview Layers")]
    // 两套模型分别放在不同 Layer 上，方便两台相机只渲染自己负责的那一套。
    [SerializeField] private string editLayerName = "ChestEditPreview";
    [SerializeField] private string textureLayerName = "ChestTexturePreview";

    [Header("Preview Camera")]
    // 每次重新生成模型后是否自动调整相机，使宝箱居中且完整显示。
    [SerializeField] private bool fitCameraOnGenerate = true;

    // 相机相对宝箱包围盒中心的观察方向。
    // 这个向量只表示方向，真正距离会根据宝箱尺寸动态计算。
    [SerializeField] private Vector3 cameraDirection = new Vector3(4f, 3f, -6f);

    // 相机正交尺寸的额外留白倍率。
    [SerializeField] private float cameraPadding = 1.25f;

    [Header("Edit Materials")]
    // 编辑模式下三类部件的材质。为空时会自动创建运行时材质。
    [SerializeField] private Material bodyMaterial;
    [SerializeField] private Material lidMaterial;
    [SerializeField] private Material lockerMaterial;

    // 编辑模式运行时材质的默认颜色。
    [SerializeField] private Color bodyColor = new Color(0.72f, 0.52f, 0.36f, 1f);
    [SerializeField] private Color lidColor = new Color(0.50f, 0.68f, 0.45f, 1f);
    [SerializeField] private Color lockerColor = new Color(0.95f, 0.74f, 0.32f, 1f);

    [Header("Texture Line Materials")]
    // 纹理预览模式下三类部件的材质。为空时会自动创建偏图标/线稿风格的运行时材质。
    [SerializeField] private Material textureBodyMaterial;
    [SerializeField] private Material textureLidMaterial;
    [SerializeField] private Material textureLockerMaterial;

    // 纹理预览运行时材质的默认颜色。
    [SerializeField] private Color textureBodyColor = new Color(0.92f, 0.92f, 0.90f, 1f);
    [SerializeField] private Color textureLidColor = new Color(0.74f, 0.74f, 0.72f, 1f);
    [SerializeField] private Color textureLockerColor = new Color(0.16f, 0.16f, 0.16f, 1f);

    // 当前生成出的两套宝箱根节点缓存。每次 GenerateBoth 都会销毁旧对象并重建。
    private Transform editGeneratedRoot;
    private Transform textureGeneratedRoot;

    // 用户拖拽预览时累积的旋转状态。
    // yaw 控制绕 Y 轴的左右旋转，pitch 控制绕 X 轴的上下旋转。
    private float previewYaw;
    private float previewPitch;

    // 旋转支点在生成根节点本地空间中的位置。
    // 生成后的 mesh 原点不一定等于视觉中心，因此用包围盒中心作为预览旋转支点。
    private Vector3 previewPivotLocal = Vector3.zero;

    public Camera EditPreviewCamera
    {
        get
        {
            // 对外暴露编辑预览相机时，先尝试补齐引用，降低场景配置遗漏带来的空引用风险。
            ResolveReferences();
            return editPreviewCamera;
        }
    }

    public Camera TexturePreviewCamera
    {
        get
        {
            // 纹理预览相机可能是运行时自动创建的，因此读取前也要 ResolveReferences。
            ResolveReferences();
            return texturePreviewCamera;
        }
    }

    public Transform TextureGeneratedRoot
    {
        get
        {
            // 纹理线稿后处理器会用这个根节点去遍历 Renderer，生成轮廓/线稿效果。
            return textureGeneratedRoot;
        }
    }

    public bool HasGeneratedChest => editGeneratedRoot != null;

    public Vector3 PreviewEulerAngles =>
        new Vector3(previewPitch, previewYaw, 0f);

    public ChestLatentParams CreateParameterSnapshot()
    {
        ResolveReferences();
        return parameterState != null
            ? parameterState.CreateParamsCopy()
            : null;
    }

    private void Awake()
    {
        // Awake 阶段只做引用补齐，不立即生成，是否生成交给 generateOnStart 或 UI 流程决定。
        ResolveReferences();
    }

    private void Start()
    {
        // 可选的自动生成入口。当前有效场景通常关闭它，由 UI 按钮或参数变化触发生成。
        if (generateOnStart)
        {
            GenerateBoth();
        }
    }

    [ContextMenu("Generate Chest")]
    public void Generate()
    {
        // Inspector 右键菜单入口，方便在编辑器里手动测试生成。
        GenerateBoth();
    }

    public void Generate(ChestPreviewMode mode)
    {
        // 兼容旧调用形式：现在两种预览模式都依赖同一套参数，所以统一生成两套实例。
        GenerateBoth();
    }

    public void RotatePreview(float deltaYawDegrees, float deltaPitchDegrees)
    {
        // 由 UI 拖拽调用。这里只累积旋转状态并应用到已有生成物，不重新生成 mesh。
        // 这样拖拽时成本低，也不会重置材质、相机或参数状态。
        previewYaw = Mathf.Repeat(previewYaw + SanitizeAngleDelta(deltaYawDegrees), 360f);
        previewPitch = Mathf.Clamp(
            previewPitch + SanitizeAngleDelta(deltaPitchDegrees),
            MinPreviewPitch,
            MaxPreviewPitch);

        ApplyPreviewRotation();
    }

    public void GenerateBoth()
    {
        // 生成主入口：
        // 1. 补齐引用并配置相机。
        // 2. 从参数状态取一份安全副本。
        // 3. 清理旧生成物。
        // 4. 创建编辑模式和纹理模式两套模型。
        // 5. 适配相机、恢复当前预览旋转、同步两台相机。
        ResolveReferences();
        ConfigurePreviewCameras();

        // 注意：CreateParamsCopy 会 clone 当前参数并 Clamp，生成流程不会直接修改 UI 正在编辑的对象。
        ChestLatentParams parameters = parameterState != null
            ? parameterState.CreateParamsCopy()
            : new ChestLatentParams();

        // 每次生成都彻底清理旧 mesh，避免场景里残留重复对象，也避免动态 Mesh 泄漏。
        ClearGeneratedRoot();

        // Layer 不存在时会回退到 Default，并给出 Warning。
        int editLayer = ResolveLayer(editLayerName, 0);
        int textureLayer = ResolveLayer(textureLayerName, 0);

        // 两套根节点共用 targetRoot，但分别使用不同 layer、不同材质和不同相机。
        editGeneratedRoot = CreateGeneratedRoot(editGeneratedRootName, editLayer);
        textureGeneratedRoot = CreateGeneratedRoot(textureGeneratedRootName, textureLayer);

        // 编辑模式：彩色低模显示，用于参数编辑和拖拽旋转。
        CreateMeshPart(editGeneratedRoot, "Body", ChestMeshFactory.CreateBodyMesh(parameters), GetBodyMaterial(), editLayer);
        CreateMeshPart(editGeneratedRoot, "Lid", ChestMeshFactory.CreateLidMesh(parameters), GetLidMaterial(), editLayer);
        CreateMeshPart(editGeneratedRoot, "Locker", ChestMeshFactory.CreateLockerMesh(parameters), GetLockerMaterial(), editLayer);

        // 纹理预览模式：几何相同，但材质更接近黑白线稿/图标生成效果。
        CreateMeshPart(textureGeneratedRoot, "Body", ChestMeshFactory.CreateBodyMesh(parameters), GetTextureBodyMaterial(), textureLayer);
        CreateMeshPart(textureGeneratedRoot, "Lid", ChestMeshFactory.CreateLidMesh(parameters), GetTextureLidMaterial(), textureLayer);
        CreateMeshPart(textureGeneratedRoot, "Locker", ChestMeshFactory.CreateLockerMesh(parameters), GetTextureLockerMaterial(), textureLayer);

        // 计算视觉中心作为旋转支点。这样拖拽旋转时宝箱看起来绕自身中心转，而不是绕模型原点甩动。
        previewPivotLocal = TryGetGeneratedLocalBounds(editGeneratedRoot, out Bounds localBounds)
            ? localBounds.center
            : Vector3.zero;

        // 在未旋转状态下先根据 edit 模型适配相机，确保宝箱完整进入画面。
        if (fitCameraOnGenerate && editPreviewCamera != null)
        {
            FitCameraToGeneratedChest(editPreviewCamera, editGeneratedRoot);
        }

        // 重新生成后恢复用户之前拖拽得到的角度，避免调参数时预览角度被重置。
        ApplyPreviewRotation();

        // 纹理相机跟随编辑相机，使两种模式切换时构图一致。
        SyncTextureCameraToEditCamera();
    }

    [ContextMenu("Clear Generated Chest")]
    public void ClearGeneratedRoot()
    {
        // 清理两套当前命名的生成根节点。
        ClearGeneratedRootByName(editGeneratedRootName, ref editGeneratedRoot);
        ClearGeneratedRootByName(textureGeneratedRootName, ref textureGeneratedRoot);

        // 兼容早期版本可能留下的旧根节点名称。
        if (editGeneratedRootName != "GeneratedChest" && textureGeneratedRootName != "GeneratedChest")
        {
            ClearGeneratedRootByName("GeneratedChest", ref editGeneratedRoot);
        }
    }

    private void ResolveReferences()
    {
        // targetRoot 为空时，生成物默认挂在本组件所在节点下面。
        if (targetRoot == null)
        {
            targetRoot = transform;
        }

        // 参数状态通常挂在父级 BoxGenerator3DRoot 上。
        if (parameterState == null)
        {
            parameterState = GetComponentInParent<ChestParameterState>();
        }

        // 如果没有显式配置编辑预览相机，则退回使用 MainCamera。
        if (editPreviewCamera == null)
        {
            editPreviewCamera = Camera.main;
        }

        // 纹理相机可以不手动放进场景。
        // 缺失时复制一台编辑相机，后续再单独设置背景色和 cullingMask。
        if (texturePreviewCamera == null && editPreviewCamera != null)
        {
            texturePreviewCamera = CreateRuntimeTextureCamera(editPreviewCamera);
        }
    }

    private Transform GetTargetRoot()
    {
        // 小型兜底：即使外部把 targetRoot 置空，生成流程也仍然有父节点可用。
        return targetRoot != null ? targetRoot : transform;
    }

    private Camera CreateRuntimeTextureCamera(Camera sourceCamera)
    {
        // 运行时创建的纹理相机只服务当前生成器，因此挂到本生成器下面，方便层级管理。
        GameObject cameraObject = new GameObject("ChestTexturePreviewCamera_Runtime");
        cameraObject.transform.SetParent(transform, false);

        // 相机禁用自动渲染，真正渲染由 Chest3DPreviewUIController 手动调用 Camera.Render。
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        CopyCameraPoseAndProjection(sourceCamera, camera);
        return camera;
    }

    private void ConfigurePreviewCameras()
    {
        // 每次生成前重新配置，确保 Inspector 修改 layer 名称或相机引用后能及时生效。
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

        // 预览相机只用于离屏渲染到 RenderTexture，不参与主画面自动渲染。
        camera.enabled = false;

        // 正交相机更适合生成器类工具：尺寸稳定，不会因为距离产生透视变形。
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = backgroundColor;

        // 每台相机只看自己的 layer，避免 edit/texture 两套模型互相叠到同一张图里。
        if (layer >= 0)
        {
            camera.cullingMask = 1 << layer;
        }
    }

    private Transform CreateGeneratedRoot(string rootName, int layer)
    {
        // 根节点只负责整体位移、旋转、缩放。
        // Body/Lid/Locker 等具体部件作为子节点挂在下面，方便统一旋转和清理。
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
        // 每个部件都是一个普通 GameObject：MeshFilter 存网格，MeshRenderer 存材质。
        // 几何本身由 ChestMeshFactory 负责，本脚本只做装配。
        GameObject part = new GameObject(partName)//创建空object
        {
            layer = layer
        };

        part.transform.SetParent(parent, false);//挂到空节点下面

        MeshFilter meshFilter = part.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = material;
    }

    private void ApplyPreviewRotation()
    {
        // 将 yaw/pitch 状态转成 Quaternion，并同时应用到两套预览模型。
        // 注意不改相机位置，因此表现为“宝箱在相机前原地转动”。
        Quaternion rotation = Quaternion.Euler(previewPitch, previewYaw, 0f);
        ApplyPreviewRotation(editGeneratedRoot, rotation);
        ApplyPreviewRotation(textureGeneratedRoot, rotation);
    }

    private void ApplyPreviewRotation(Transform root, Quaternion rotation)
    {
        if (root == null)
        {
            return;
        }

        // Unity Transform 默认绕 localPosition 所在的原点旋转。
        // 宝箱 mesh 的视觉中心可能不在原点，所以这里通过 localPosition 补偿：
        // root.localPosition = pivot - rotation * pivot
        // 这会让 pivot 在父空间中保持不动，从而实现“绕视觉中心旋转”。
        Vector3 scaledPivot = Vector3.Scale(root.localScale, previewPivotLocal);
        root.localRotation = rotation;
        root.localPosition = scaledPivot - rotation * scaledPivot;
    }

    private Material GetBodyMaterial()
    {
        // Inspector 没有指定材质时，懒创建一份运行时材质。
        // 这样 demo 场景不需要额外准备材质资产也能正常显示。
        if (bodyMaterial == null)
        {
            bodyMaterial = CreateRuntimeMaterial("Chest Body Material", bodyColor, false);
        }

        return bodyMaterial;
    }

    private Material GetLidMaterial()
    {
        // 编辑预览的箱盖材质。
        if (lidMaterial == null)
        {
            lidMaterial = CreateRuntimeMaterial("Chest Lid Material", lidColor, false);
        }

        return lidMaterial;
    }

    private Material GetLockerMaterial()
    {
        // 编辑预览的锁扣材质。
        if (lockerMaterial == null)
        {
            lockerMaterial = CreateRuntimeMaterial("Chest Locker Material", lockerColor, false);
        }

        return lockerMaterial;
    }

    private Material GetTextureBodyMaterial()
    {
        // 纹理预览的箱体材质，通常使用 unlit shader，减少光照对线稿效果的干扰。
        if (textureBodyMaterial == null)
        {
            textureBodyMaterial = CreateRuntimeMaterial("Chest Texture Body Material", textureBodyColor, true);
        }

        return textureBodyMaterial;
    }

    private Material GetTextureLidMaterial()
    {
        // 纹理预览的箱盖材质。
        if (textureLidMaterial == null)
        {
            textureLidMaterial = CreateRuntimeMaterial("Chest Texture Lid Material", textureLidColor, true);
        }

        return textureLidMaterial;
    }

    private Material GetTextureLockerMaterial()
    {
        // 纹理预览的锁扣材质。
        if (textureLockerMaterial == null)
        {
            textureLockerMaterial = CreateRuntimeMaterial("Chest Texture Locker Material", textureLockerColor, true);
        }

        return textureLockerMaterial;
    }

    private static Material CreateRuntimeMaterial(string materialName, Color color, bool unlit)
    {
        // 根据用途选择 lit 或 unlit shader，并兼容 URP / Built-in / Sprite fallback。
        Shader shader = unlit ? FindUnlitPreviewShader() : FindLitPreviewShader();
        Material material = new Material(shader)
        {
            name = materialName
        };

        // URP 常用 _BaseColor，Built-in/部分 fallback shader 常用 _Color。
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        // 关闭背面剔除，避免低模/线稿预览从某些角度看到缺面。
        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        // 让编辑模式材质保持轻微粗糙的低模质感。
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.18f);
        }

        return material;
    }

    private static Shader FindLitPreviewShader()
    {
        // 优先使用 URP Lit；如果项目渲染管线或包发生变化，逐级降级，保证材质仍可创建。
        return Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Sprites/Default");
    }

    private static Shader FindUnlitPreviewShader()
    {
        // 纹理线稿预览更需要颜色稳定，因此优先使用 Unlit。
        return Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default")
            ?? FindLitPreviewShader();
    }

    private void FitCameraToGeneratedChest(Camera camera, Transform generatedRoot)
    {
        // 根据生成物的世界包围盒，把正交相机放到合适位置并设置 orthographicSize。
        // 这里用 editGeneratedRoot 作为取景依据，textureGeneratedRoot 与它几何一致。
        if (camera == null || !TryGetGeneratedBounds(generatedRoot, out Bounds bounds))
        {
            return;
        }

        camera.orthographic = true;

        // cameraDirection 只控制观察方向；距离会根据包围盒半径动态算出。
        Vector3 direction = cameraDirection.sqrMagnitude > 0.0001f
            ? cameraDirection.normalized
            : new Vector3(4f, 3f, -6f).normalized;

        // extents.magnitude 相当于包围盒外接球半径，适合用来估算安全相机距离。
        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);//包围盒的最小外接球半径，or 0.5
        float distance = Mathf.Max(radius * 4f, 8f);//让相机离模型有一个安全距离

        // 相机看向宝箱中心；正交尺寸用 radius * padding 保证有留白。
        camera.transform.position = bounds.center + direction * distance;//把相机放到宝箱中心+观察方向×距离的位置
        camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);
        camera.orthographicSize = radius * Mathf.Max(1f, cameraPadding); //camera padding是留白倍率   

        // 近远裁剪面跟随当前距离，避免模型被裁切。
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = distance + radius * 4f;
    }

    private void SyncTextureCameraToEditCamera()
    {
        // 纹理预览相机的姿态和投影完全复制编辑相机。
        // 复制后再恢复自己的 layer 和背景色，确保只渲染 TextureLine 模型。
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
        // 只复制会影响构图的相机属性。
        // 背景色、cullingMask 等模式差异由 ConfigurePreviewCamera 再设置。
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
        // 计算生成物的世界空间包围盒，用于相机取景。
        // Renderer.bounds 已经包含 Transform 的位置、旋转、缩放影响。
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

        // 把所有子 Renderer 的包围盒合并成一个整体包围盒。
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return true;
    }

    private static bool TryGetGeneratedLocalBounds(Transform root, out Bounds bounds)
    {
        // 计算生成物在 root 本地空间下的包围盒，用于找视觉旋转中心。
        // 不能直接用 Renderer.bounds，因为那是世界空间；旋转支点需要跟随生成根节点本地坐标。
        bounds = default;

        if (root == null)
        {
            return false;
        }

        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>();
        bool hasBounds = false;
        Matrix4x4 rootFromMesh;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter.sharedMesh == null)
            {
                continue;
            }

            // 把每个 mesh 的本地包围盒变换到 root 的本地空间，再合并。
            rootFromMesh = root.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
            Bounds meshBounds = TransformBounds(meshFilter.sharedMesh.bounds, rootFromMesh);

            if (!hasBounds)
            {
                bounds = meshBounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(meshBounds);
        }

        return hasBounds;
    }

    private static Bounds TransformBounds(Bounds sourceBounds, Matrix4x4 matrix)
    {
        // 将一个 AABB 通过矩阵变换后重新包成新的 AABB。
        // 这里用“中心点 + 三个轴向 extents”的方式，比枚举 8 个角点更紧凑。
        Vector3 center = matrix.MultiplyPoint3x4(sourceBounds.center);
        Vector3 sourceExtents = sourceBounds.extents;

        Vector3 axisX = matrix.MultiplyVector(new Vector3(sourceExtents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, sourceExtents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, sourceExtents.z));

        Vector3 extents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

        return new Bounds(center, extents * 2f);
    }

    private static float SanitizeAngleDelta(float value)
    {
        // 防止 NaN / Infinity 从输入层进入旋转状态，导致 Transform 变成非法值。
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    private void ClearGeneratedRootByName(string rootName, ref Transform cachedRoot)
    {
        // 只清理 targetRoot 直接子节点中指定名称的生成物，避免误删场景里的其他对象。
        Transform root = GetTargetRoot();
        Transform existingRoot = root.Find(rootName);

        if (existingRoot == null)
        {
            // 如果场景里已经没有这个对象，也同步清掉缓存引用。
            if (cachedRoot != null && cachedRoot.name == rootName)
            {
                cachedRoot = null;
            }

            return;
        }

        // 动态创建的 Mesh 不是资产，需要在销毁 GameObject 前手动释放。
        ReleaseMeshes(existingRoot);
        DestroyGeneratedObject(existingRoot.gameObject);

        if (cachedRoot != null && cachedRoot.name == rootName)
        {
            cachedRoot = null;
        }
    }

    private void ReleaseMeshes(Transform root)
    {
        // MeshFilter.sharedMesh 指向运行时创建的 Mesh。
        // 如果只 Destroy GameObject 而不 Destroy Mesh，长时间多次生成可能造成内存残留。
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
        // Layer 名字来自 Inspector/ProjectSettings。
        // 如果项目设置里删掉了对应 Layer，则回退并打印 Warning，避免 cullingMask 变成无效状态。
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
        // 兼容运行时和编辑器 ContextMenu 调用：
        // Play 模式使用 Destroy，非 Play 模式使用 DestroyImmediate。
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
