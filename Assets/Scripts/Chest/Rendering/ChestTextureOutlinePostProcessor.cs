using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 宝箱纹理预览的深度/法线描边后处理器。
///
/// 整体流程分为两个渲染阶段：
/// 1. 临时把纹理预览宝箱的所有材质替换为深度/法线编码材质，渲染出一张 DepthNormal 纹理；
/// 2. 全屏采样原始彩色纹理和 DepthNormal 纹理，根据相邻像素的深度差、法线差检测边缘，
///    再把线条颜色叠加到彩色纹理上并输出到目标 RenderTexture。
///
/// 本组件不负责生成宝箱，也不持续参与相机渲染循环；
/// Chest3DPreviewUIController 在纹理预览模式需要刷新画面时主动调用 Render()。
/// </summary>
[DisallowMultipleComponent]
public class ChestTextureOutlinePostProcessor : MonoBehaviour
{
    // Shader 使用 Hidden 路径，不会出现在普通材质的 Shader 选择菜单中。
    // Inspector 未显式指定 Shader 时，EnsureMaterials() 会通过这两个名称查找它们。
    private const string DepthNormalShaderName = "Hidden/Chest/DepthNormalEncode";
    private const string OutlineShaderName = "Hidden/Chest/DepthNormalOutline";

    // 缓存 Shader 属性 ID，避免每次刷新预览时反复用字符串查找属性。
    // 这些名称必须与 ChestDepthNormalEncode.shader、ChestDepthNormalOutline.shader 中的变量一致。
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
    // 第一阶段 Shader：RGB 编码世界空间法线，A 编码相机近远裁剪面之间的线性深度。
    [SerializeField] private Shader depthNormalShader;

    // 第二阶段 Shader：比较中心像素与周围 8 个像素的深度/法线差，并合成描边。
    [SerializeField] private Shader outlineShader;

    [Header("Outline")]
    // 最终叠加到原始彩色预览上的线条颜色；Alpha 同时控制描边混合强度。
    [SerializeField] private Color lineColor = Color.black;

    // 深度差放大倍率。值越大，物体轮廓、遮挡边界以及深度突变越容易形成线条。
    [SerializeField] private float depthSensitivity = 18f;

    // 法线差放大倍率。值越大，朝向变化明显的折角和曲面变化越容易形成线条。
    [SerializeField] private float normalSensitivity = 4f;

    // 边缘判定起点。综合边缘强度低于该值时不会输出描边。
    [SerializeField] private float edgeThreshold = 0.08f;

    // smoothstep 的过渡宽度，控制无描边到完整描边之间的柔和程度。
    [SerializeField] private float edgeSoftness = 0.02f;

    // 邻域采样的像素间距倍率，因此它控制的是描边采样范围，而不是几何线宽。
    [SerializeField] private float thickness = 1.25f;

    [Header("Depth Normal Pass")]
    // DepthNormal 纹理的背景值：RGB(0.5, 0.5, 1) 解码为朝向 +Z 的法线，A=1 表示最远深度。
    // 宝箱像素与背景像素之间会产生明显差异，从而帮助检测模型外轮廓。
    [SerializeField] private Color depthNormalClearColor = new Color(0.5f, 0.5f, 1f, 1f);

    [Header("Composite")]
    // RenderTexture 在不同图形 API/渲染路径下可能出现 Y 方向约定差异；开启时在合成采样阶段翻转 UV.y。
    [SerializeField] private bool flipRenderTextureY = true;

    // 以下对象全部在运行时按需创建，不写入场景或项目资产。
    private Material depthNormalMaterial;
    private Material outlineMaterial;

    // 第一阶段的中间纹理，RGB 保存法线，A 保存深度；尺寸始终与最终输出保持一致。
    private RenderTexture depthNormalTexture;

    // 第二阶段绘制使用的裁剪空间四边形，用一笔 DrawMesh 覆盖整张目标纹理。
    private Mesh fullscreenQuad;

    // 复用同一个 CommandBuffer 提交全屏合成命令，避免每次 Render() 都创建新的命令缓冲。
    private CommandBuffer outlineCommandBuffer;

    // 缺少或不支持 Shader 时只打印一次警告，避免参数刷新期间连续刷屏。
    private bool loggedMissingShaderWarning;

    /// <summary>
    /// 对一张已经由预览相机渲染完成的彩色纹理执行深度/法线描边。
    /// </summary>
    /// <param name="camera">纹理预览相机；第二次渲染 DepthNormal 时复用它的视角、投影和剔除设置。</param>
    /// <param name="renderRoot">纹理预览宝箱根节点；只替换该节点下 MeshRenderer 的材质。</param>
    /// <param name="sourceColor">相机第一次渲染得到的原始彩色纹理。</param>
    /// <param name="destination">写入描边合成结果的最终纹理。</param>
    public void Render(Camera camera, Transform renderRoot, RenderTexture sourceColor, RenderTexture destination)
    {
        // 基础输入不完整时无法安全渲染；调用方仍保留自己预先写入 destination 的兜底画面。
        if (camera == null || sourceColor == null || destination == null)
        {
            return;
        }

        // 没有模型根节点或 Shader 不可用时退回原始彩色预览，不让纹理模式显示空白。
        if (renderRoot == null || !EnsureMaterials())
        {
            Graphics.Blit(sourceColor, destination);
            return;
        }

        // 中间纹理必须与目标纹理同尺寸，否则邻域采样对应不到相同屏幕像素。
        EnsureDepthNormalTexture(destination.width, destination.height);

        // 先获取几何边缘信息，再把它和已经存在的彩色结果合成。
        RenderDepthNormal(camera, renderRoot);
        RenderOutline(sourceColor, destination);
    }

    private bool EnsureMaterials()
    {
        // Inspector 没有手动指定时，使用 Shader 文件中声明的完整名称查找。
        if (depthNormalShader == null)
        {
            depthNormalShader = Shader.Find(DepthNormalShaderName);
        }

        if (outlineShader == null)
        {
            outlineShader = Shader.Find(OutlineShaderName);
        }

        // Shader.Find 失败或当前平台不支持任一 Shader 时，整个后处理不可用。
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

        // 材质采用懒加载：只有第一次真正进入纹理描边流程时才创建。
        if (depthNormalMaterial == null)
        {
            depthNormalMaterial = CreateRuntimeMaterial(depthNormalShader, "Chest Depth Normal Encode Material");
        }

        if (outlineMaterial == null)
        {
            outlineMaterial = CreateRuntimeMaterial(outlineShader, "Chest Depth Normal Outline Material");
        }

        // 除了材质引用有效，还要确认两个 Shader 至少都编译出了一个可绘制 Pass。
        return depthNormalMaterial != null &&
            outlineMaterial != null &&
            depthNormalMaterial.passCount > 0 &&
            outlineMaterial.passCount > 0;
    }

    private static Material CreateRuntimeMaterial(Shader shader, string materialName)
    {
        // HideAndDontSave 防止临时材质进入场景序列化、层级窗口或项目资产。
        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave
        };

        return material;
    }

    private void EnsureDepthNormalTexture(int width, int height)
    {
        // RenderTexture 不接受 0 尺寸；即使 UI 正处于布局变化阶段也保证最小可创建尺寸。
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);

        // 尺寸未变化时复用已有 GPU 纹理，避免每次参数刷新都重新分配显存。
        if (depthNormalTexture != null &&
            depthNormalTexture.width == width &&
            depthNormalTexture.height == height)
        {
            return;
        }

        ReleaseDepthNormalTexture();

        // 优先使用半精度浮点纹理，减少深度和法线量化造成的误检；
        // 不支持 ARGBHalf 的平台回退到常规 8 位 ARGB32。
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
            ? RenderTextureFormat.ARGBHalf
            : RenderTextureFormat.ARGB32;

        // 24 位深度缓冲供第一阶段的 ZTest/ZWrite 使用，确保只保留离相机最近的可见表面。
        // 不启用 MSAA，避免法线/深度边界被预混合后影响后续差值检测。
        depthNormalTexture = new RenderTexture(width, height, 24, format)
        {
            name = "ChestTextureDepthNormalTexture",
            antiAliasing = 1,
            useMipMap = false,
            hideFlags = HideFlags.HideAndDontSave
        };

        // 显式创建底层 GPU 资源，保证紧接着的 camera.Render() 可以使用它。
        depthNormalTexture.Create();
    }

    private void RenderDepthNormal(Camera camera, Transform renderRoot)
    {
        // 只收集当前处于激活状态的子 MeshRenderer；false 表示忽略 inactive 子节点。
        MeshRenderer[] renderers = renderRoot.GetComponentsInChildren<MeshRenderer>(false);

        // 这次渲染会临时修改相机目标、清屏方式以及模型材质，必须先完整保存现场。
        RenderTexture previousTarget = camera.targetTexture;
        CameraClearFlags previousClearFlags = camera.clearFlags;
        Color previousBackgroundColor = camera.backgroundColor;
        Material[][] previousMaterials = new Material[renderers.Length][];

        // 编码 Shader 使用相机空间线性深度，并把 near~far 映射到 0~1。
        depthNormalMaterial.SetFloat(NearClipId, camera.nearClipPlane);
        depthNormalMaterial.SetFloat(FarClipId, camera.farClipPlane);

        // 材质替换和手动 Camera.Render 都可能因运行时异常中断；
        // finally 保证无论发生什么都恢复原材质和相机状态，避免污染后续编辑模式渲染。
        try
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                // sharedMaterials 返回该 Renderer 当前全部材质槽位。
                // 替换数组保持相同槽位数量，确保多 SubMesh 模型仍能完整绘制。
                previousMaterials[i] = renderers[i].sharedMaterials;
                renderers[i].sharedMaterials = CreateReplacementMaterials(previousMaterials[i].Length);
            }

            // 使用纯色背景清空中间纹理，然后以相同相机参数再次渲染宝箱。
            // 此时所有宝箱材质都输出“世界法线 RGB + 线性深度 A”，而不是原始颜色。
            camera.targetTexture = depthNormalTexture;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = depthNormalClearColor;
            camera.Render();
        }
        finally
        {
            // 恢复每个 Renderer 原来的材质数组。对象可能在渲染期间被销毁，因此先判空。
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterials = previousMaterials[i];
                }
            }

            // 还原调用前的相机状态，让调用方继续持有原来的 raw RenderTexture 目标。
            camera.targetTexture = previousTarget;
            camera.clearFlags = previousClearFlags;
            camera.backgroundColor = previousBackgroundColor;
        }
    }

    private Material[] CreateReplacementMaterials(int materialCount)
    {
        // 即使 Renderer 暂时没有材质槽，也至少提供一个替换材质供默认 SubMesh 绘制。
        materialCount = Mathf.Max(1, materialCount);
        Material[] replacementMaterials = new Material[materialCount];

        // 所有槽位共享同一个运行时深度/法线材质，不需要为每个 Renderer 创建材质实例。
        for (int i = 0; i < replacementMaterials.Length; i++)
        {
            replacementMaterials[i] = depthNormalMaterial;
        }

        return replacementMaterials;
    }

    private void RenderOutline(RenderTexture sourceColor, RenderTexture destination)
    {
        // 原始彩色纹理是合成底图。
        outlineMaterial.SetTexture(MainTexId, sourceColor);

        // xy 保存单个像素对应的 UV 尺寸，zw 保存纹理像素尺寸。
        // Outline Shader 用 xy * thickness 计算中心像素周围 3x3 邻域的采样偏移。
        outlineMaterial.SetVector(
            MainTexTexelSizeId,
            new Vector4(
                1f / Mathf.Max(1, sourceColor.width),
                1f / Mathf.Max(1, sourceColor.height),
                sourceColor.width,
                sourceColor.height));

        // 绑定第一阶段生成的几何信息，并把 Inspector 中的描边参数传入 Shader。
        outlineMaterial.SetTexture(DepthNormalTexId, depthNormalTexture);
        outlineMaterial.SetColor(LineColorId, lineColor);

        // 对参数设置合理下限，防止负灵敏度、零过渡宽度等输入破坏边缘计算。
        outlineMaterial.SetFloat(DepthSensitivityId, Mathf.Max(0f, depthSensitivity));
        outlineMaterial.SetFloat(NormalSensitivityId, Mathf.Max(0f, normalSensitivity));
        outlineMaterial.SetFloat(EdgeThresholdId, Mathf.Max(0f, edgeThreshold));
        outlineMaterial.SetFloat(EdgeSoftnessId, Mathf.Max(0.0001f, edgeSoftness));
        outlineMaterial.SetFloat(ThicknessId, Mathf.Max(0.5f, thickness));
        outlineMaterial.SetFloat(FlipYId, flipRenderTextureY ? 1f : 0f);

        // 把描边 Shader 绘制到整张 destination 上，完成最终彩色图与线条的混合。
        DrawFullscreen(destination, outlineMaterial);
    }

    private void DrawFullscreen(RenderTexture destination, Material material)
    {
        // 首次使用时创建命令缓冲，后续只清空并复用其中的命令列表。
        if (outlineCommandBuffer == null)
        {
            outlineCommandBuffer = new CommandBuffer
            {
                name = "Chest Texture Outline Composite"
            };
        }

        // 每次重建本次合成所需的三条命令：指定目标、清空颜色、绘制全屏四边形。
        outlineCommandBuffer.Clear();
        outlineCommandBuffer.SetRenderTarget(destination);
        outlineCommandBuffer.ClearRenderTarget(false, true, Color.clear);

        // 使用 outlineMaterial 的第 0 个 Pass；四边形顶点已经覆盖完整裁剪空间。
        outlineCommandBuffer.DrawMesh(GetFullscreenQuad(), Matrix4x4.identity, material, 0, 0);

        // 立即提交给当前图形设备执行，执行完后 destination 即可供 UI Toolkit 显示。
        Graphics.ExecuteCommandBuffer(outlineCommandBuffer);
    }

    private Mesh GetFullscreenQuad()
    {
        // 全屏网格只创建一次，并在后续预览刷新中持续复用。
        if (fullscreenQuad != null)
        {
            return fullscreenQuad;
        }

        // 顶点范围是 [-1, 1]，Outline Shader 的顶点阶段会直接把 xy 当作裁剪空间坐标，
        // 因而该四边形正好覆盖整个 RenderTexture。UV 则覆盖完整的 [0, 1] 范围。
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

        // 为 Mesh 计算包围盒，保持网格数据完整；实际绘制由 CommandBuffer 直接提交。
        fullscreenQuad.RecalculateBounds();
        return fullscreenQuad;
    }

    private void OnDestroy()
    {
        // 这些资源均由本组件在运行时创建，组件销毁时必须成对释放，避免显存和原生对象泄漏。
        ReleaseDepthNormalTexture();
        DestroyRuntimeObject(depthNormalMaterial);
        DestroyRuntimeObject(outlineMaterial);
        DestroyRuntimeObject(fullscreenQuad);

        // CommandBuffer 不属于 UnityEngine.Object，使用自身的 Release() 释放原生资源。
        if (outlineCommandBuffer != null)
        {
            outlineCommandBuffer.Release();
            outlineCommandBuffer = null;
        }
    }

    private void ReleaseDepthNormalTexture()
    {
        // 该方法既用于输出尺寸变化时重建中间纹理，也用于组件最终销毁。
        if (depthNormalTexture == null)
        {
            return;
        }

        // 先释放 RenderTexture 的底层 GPU 资源，再销毁 Unity 对象包装。
        depthNormalTexture.Release();
        DestroyRuntimeObject(depthNormalTexture);
        depthNormalTexture = null;
    }

    private static void DestroyRuntimeObject(Object target)
    {
        // 统一处理材质、Mesh、RenderTexture 等临时 UnityEngine.Object。
        if (target == null)
        {
            return;
        }

        // Play Mode 中使用延迟销毁；编辑器非运行状态下必须立即销毁，避免临时对象残留。
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
