using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 宝箱生成流程控制器。
/// 负责监听 Generate 按钮，并调用几何生成与渲染逻辑。
/// </summary>
public class ChestGeneratorController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private UIDocument uiDocument;

    // UXML 中生成按钮的 name
    [SerializeField] private string generateButtonName = "generateButton";

    [Header("Renderer")]
    [SerializeField] private ChestRenderer2D chestRenderer;

    [Header("Parameters")]
    [SerializeField] private ChestParameterState parameterState;

    private Button generateButton;
    private ChestGeometryModel geometryModel;
    private ChestLatentParams currentParams;

    private void Awake()
    {
        // 初始化纯算法对象
        geometryModel = new ChestGeometryModel();

        // 如果没有手动拖入 UIDocument，就从当前物体获取
        if (uiDocument == null) 
        {
            uiDocument = GetComponent<UIDocument>();
        }

        // 如果没有手动拖入 Renderer，就从当前物体获取
        if (chestRenderer == null)
        {
            chestRenderer = GetComponent<ChestRenderer2D>();
        }

        if (parameterState == null)
        {
            parameterState = GetComponent<ChestParameterState>();
        }
    }

    private void OnEnable()
    {
        // 查找并绑定 Generate 按钮
        generateButton = uiDocument.rootVisualElement.Q<Button>(generateButtonName);

        if (generateButton == null)
        {
            Debug.LogError($"Generate button not found: {generateButtonName}");
            return;
        }

        generateButton.clicked += OnGenerateClicked;
    }

    private void OnDisable()
    {
        // 解绑事件，避免重复注册
        if (generateButton != null)
        {
            generateButton.clicked -= OnGenerateClicked;
        }
    }

    // 点击按钮后生成默认宝箱
    private void OnGenerateClicked()
    {
        currentParams = CreateInitialParams();

        ChestGeometryData geometryData = geometryModel.Build(currentParams);

        chestRenderer.Render(geometryData);
    }

    // 后续参数面板会调用这个方法刷新宝箱
    public void RefreshChest()
    {
        if (currentParams == null)
        {
            currentParams = CreateInitialParams();
        }

        ChestGeometryData geometryData = geometryModel.Build(currentParams);

        chestRenderer.Render(geometryData);
    }

    // 给参数面板访问当前参数
    public ChestLatentParams GetCurrentParams()
    {
        if (currentParams == null)
        {
            currentParams = CreateInitialParams();
        }

        return currentParams;
    }

    private ChestLatentParams CreateInitialParams()
    {
        return parameterState != null
            ? parameterState.CreateParamsCopy()
            : new ChestLatentParams();
    }
}
