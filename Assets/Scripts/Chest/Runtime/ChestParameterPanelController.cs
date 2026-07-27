using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

// 宝箱参数面板控制器。
// 职责边界：
// 1. 从 UILayout2.0.uxml 中找到 Params 区域里的 Slider / TextField。
// 2. 把这些 UI 控件绑定到 ChestParameterState.CurrentParams，也就是运行时可编辑参数副本。
// 3. 用户改任意参数后，统一调用 ChestLatentParams.ClampValues() 做约束修正。
// 4. 再把修正后的参数同步回全部 UI 控件，并触发 3D 预览重新生成。
//
// 注意：这里不会修改 InitialParams，因此切换或重置基础宝箱类型时，默认参数不会被用户拖拽污染。
[RequireComponent(typeof(UIDocument))]
public class ChestParameterPanelController : MonoBehaviour
{
    public event Action ParametersChanged;

    [Header("References")]
    // 承载 UILayout2.0.uxml 的 UI Document。没有手动指定时会从当前 GameObject 自动获取。
    [SerializeField] private UIDocument uiDocument;

    // 参数状态对象。面板只编辑它的 CurrentParams，不直接写 InitialParams。
    [SerializeField] private ChestParameterState parameterState;

    // UI 到 3D RenderTexture 的桥接控制器。参数变化后通过它刷新画布里的宝箱。
    [SerializeField] private Chest3DPreviewUIController previewController;

    [Header("Params UI")]
    // UXML 中参数滚动容器的 Name。滚轮浏览只发生在这个 ScrollView 内部。
    [SerializeField] private string parameterScrollName = "ParameterScroll";

    // 浮点参数绑定表。width、height、depth 等 float 参数都注册到这里。
    private readonly List<FloatParamBinding> floatBindings = new List<FloatParamBinding>();

    // 整数参数绑定表。目前主要用于 lidSegments。
    private readonly List<IntParamBinding> intBindings = new List<IntParamBinding>();

    private ScrollView parameterScroll;

    // 同步 UI 时会批量 SetValueWithoutNotify。这个标记用于避免同步过程反过来触发 Apply。
    private bool isSyncingControls;

    private void OnEnable()
    {
        // OnEnable 时 UI Document 的 visual tree 已经可访问，适合做 UXML 查询和事件绑定。
        ResolveReferences();
        BuildParameterBindings();
        BindUI();
    }

    private void OnDisable()
    {
        // 退出 Play、禁用对象或热重载时解绑事件，避免重复注册回调。
        UnbindUI();
    }

    private void ResolveReferences()
    {
        // 允许 Inspector 手动拖引用；没拖时尽量从当前层级自动补齐，降低场景配置成本。
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (parameterState == null)
        {
            parameterState = GetComponentInParent<ChestParameterState>();
        }

        if (previewController == null)
        {
            previewController = GetComponent<Chest3DPreviewUIController>();
        }
    }

    private void BuildParameterBindings()
    {
        // 每次启用时重建绑定表，避免热重载或重复 OnEnable 后留下旧引用。
        floatBindings.Clear();
        intBindings.Clear();

        // 宽度：必须不小于 lockerWidth；上限使用 ChestLatentParams 中的统一常量。
        floatBindings.Add(new FloatParamBinding(
            "Param_width",
            "Param_width_Field",
            p => p.width,
            (p, value) => p.width = value,
            p => Mathf.Max(ChestLatentParams.MinWidth, p.lockerWidth),
            _ => ChestLatentParams.MaxWidth));

        // 高度：必须至少容纳一半锁扣高度，避免锁扣比例完全压扁箱体。
        floatBindings.Add(new FloatParamBinding(
            "Param_height",
            "Param_height_Field",
            p => p.height,
            (p, value) => p.height = value,
            p => Mathf.Max(ChestLatentParams.MinPositiveSize, p.lockerHeight * 0.5f),
            _ => ChestLatentParams.MaxSize));

        // 深度：正数即可，具体几何安全由 ClampValues 和 mesh factory 共同保证。
        floatBindings.Add(new FloatParamBinding(
            "Param_depth",
            "Param_depth_Field",
            p => p.depth,
            (p, value) => p.depth = value,
            _ => ChestLatentParams.MinPositiveSize,
            _ => ChestLatentParams.MaxSize));

        // taper：控制底部收缩。上限会随当前 width / depth 动态变化，防止底边反向或交叉。
        floatBindings.Add(new FloatParamBinding(
            "Param_taper",
            "Param_taper_Field",
            p => p.taper,
            (p, value) => p.taper = value,
            _ => 0f,
            p => GetMaxTaper(p)));

        // 箱盖高度：正数参数，决定拱形盖子的最高点。
        floatBindings.Add(new FloatParamBinding(
            "Param_lidHeight",
            "Param_lidHeight_Field",
            p => p.lidHeight,
            (p, value) => p.lidHeight = value,
            _ => ChestLatentParams.MinPositiveSize,
            _ => ChestLatentParams.MaxSize));

        // 箱盖分段：整数参数。下限设成能看出弧形的最低分段数。
        intBindings.Add(new IntParamBinding(
            "Param_lidSegments",
            "Param_lidSegments_Field",
            p => p.lidSegments,
            (p, value) => p.lidSegments = value,
            _ => ChestLatentParams.MinLidSegments,
            _ => ChestLatentParams.MaxLidSegments));

        // 锁扣宽度：它会反过来影响 width 的最小值，因此任何参数变化后都要刷新所有控件范围。
        floatBindings.Add(new FloatParamBinding(
            "Param_lockerWidth",
            "Param_lockerWidth_Field",
            p => p.lockerWidth,
            (p, value) => p.lockerWidth = value,
            _ => ChestLatentParams.MinLockerWidth,
            _ => ChestLatentParams.MaxWidth));

        // 锁扣高度：它会反过来影响 height 的最小值。
        floatBindings.Add(new FloatParamBinding(
            "Param_lockerHeight",
            "Param_lockerHeight_Field",
            p => p.lockerHeight,
            (p, value) => p.lockerHeight = value,
            _ => ChestLatentParams.MinPositiveSize,
            _ => ChestLatentParams.MaxSize));

        // 锁扣厚度：上限跟当前箱体 width / depth 相关，避免配件厚度大到穿模过多。
        floatBindings.Add(new FloatParamBinding(
            "Param_lockerDepth",
            "Param_lockerDepth_Field",
            p => p.lockerDepth,
            (p, value) => p.lockerDepth = value,
            _ => ChestLatentParams.MinPositiveSize,
            p => Mathf.Max(ChestLatentParams.MinPositiveSize, Mathf.Min(p.width, p.depth) * 0.2f)));

        // 箱体厚度：目前主要是预留参数，后续生成内壁、包边或开口结构时会继续使用。
        floatBindings.Add(new FloatParamBinding(
            "Param_bodyThickness",
            "Param_bodyThickness_Field",
            p => p.bodyThickness,
            (p, value) => p.bodyThickness = value,
            _ => ChestLatentParams.MinPositiveSize,
            p => Mathf.Max(ChestLatentParams.MinPositiveSize, Mathf.Min(p.width, p.height, p.depth) * 0.45f)));

        // 箱盖厚度：当前 lid mesh 已经用它计算内外弧。
        floatBindings.Add(new FloatParamBinding(
            "Param_lidThickness",
            "Param_lidThickness_Field",
            p => p.lidThickness,
            (p, value) => p.lidThickness = value,
            _ => ChestLatentParams.MinPositiveSize,
            p => Mathf.Max(ChestLatentParams.MinPositiveSize, Mathf.Min(p.width, p.lidHeight, p.depth) * 0.45f)));

        // 锁扣锚点深度偏移：默认 0，即贴在箱盖前下沿中心；范围随 bodyThickness 动态变化。
        floatBindings.Add(new FloatParamBinding(
            "Param_lockerAnchorDepth",
            "Param_lockerAnchorDepth_Field",
            p => p.lockerAnchorDepth,
            (p, value) => p.lockerAnchorDepth = value,
            p => -p.bodyThickness,
            p => p.bodyThickness));
    }

    private void BindUI()
    {
        // 从 UXML 实例化后的 visual tree 中查询控件并注册事件。
        if (uiDocument == null)
        {
            Debug.LogError("ChestParameterPanelController requires a UIDocument.");
            return;
        }

        if (parameterState == null)
        {
            Debug.LogError("ChestParameterPanelController requires a ChestParameterState.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;

        // ParameterScroll 只负责 Params 列表内部滚动，不影响 header 或其他面板。
        parameterScroll = root.Q<ScrollView>(parameterScrollName);
        ConfigureParameterScroll();

        // 按绑定表批量绑定。新增一个 float 参数时，只需要在 BuildParameterBindings 中加一条配置。
        foreach (FloatParamBinding binding in floatBindings)
        {
            BindFloatParam(root, binding);
        }

        foreach (IntParamBinding binding in intBindings)
        {
            BindIntParam(root, binding);
        }

        SyncAllControls();
    }

    private void UnbindUI()
    {
        // Unity UI Toolkit 的事件回调不会自动根据我们自己的绑定表清理。
        // 禁用时手动解绑，避免重复进入 Play Mode 后一个拖动触发多次刷新。
        foreach (FloatParamBinding binding in floatBindings)
        {
            if (binding.Slider != null && binding.SliderChanged != null)
            {
                binding.Slider.UnregisterValueChangedCallback(binding.SliderChanged);
            }

            if (binding.Field != null)
            {
                if (binding.FieldChanged != null)
                {
                    binding.Field.UnregisterValueChangedCallback(binding.FieldChanged);
                }

                if (binding.FieldGeometryChanged != null)
                {
                    binding.Field.UnregisterCallback(binding.FieldGeometryChanged);
                }
            }
        }

        foreach (IntParamBinding binding in intBindings)
        {
            if (binding.Slider != null && binding.SliderChanged != null)
            {
                binding.Slider.UnregisterValueChangedCallback(binding.SliderChanged);
            }

            if (binding.Field != null)
            {
                if (binding.FieldChanged != null)
                {
                    binding.Field.UnregisterValueChangedCallback(binding.FieldChanged);
                }

                if (binding.FieldGeometryChanged != null)
                {
                    binding.Field.UnregisterCallback(binding.FieldGeometryChanged);
                }
            }
        }
    }

    private void ConfigureParameterScroll()
    {
        // 约束滚动方向：隐藏横向滚动条，只允许纵向浏览参数列表。
        if (parameterScroll == null)
        {
            Debug.LogError($"Parameter scroll view not found: {parameterScrollName}");
            return;
        }

        parameterScroll.mode = ScrollViewMode.Vertical;
        parameterScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        parameterScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
    }

    private void BindFloatParam(VisualElement root, FloatParamBinding binding)
    {
        // Slider 和 TextField 的命名来自 UILayout2.0.uxml，例如 Param_width / Param_width_Field。
        binding.Slider = root.Q<Slider>(binding.SliderName);
        binding.Field = root.Q<TextField>(binding.FieldName);

        if (binding.Slider == null)
        {
            Debug.LogError($"Float slider not found: {binding.SliderName}");
            return;
        }

        if (binding.Field == null)
        {
            Debug.LogError($"Float field not found: {binding.FieldName}");
            return;
        }

        ConfigureTextField(binding.Field);

        // 把具体参数 binding 闭包进回调里，所有 float 参数共用一套处理函数。
        binding.SliderChanged = evt => OnFloatSliderChanged(binding, evt.newValue);
        binding.FieldChanged = evt => OnFloatFieldChanged(binding, evt.newValue);

        // TextField 内部会生成 unity-text-input。布局变化后重新覆盖内部样式，避免文字被挤压。
        binding.FieldGeometryChanged = _ => ConfigureTextFieldInput(binding.Field);

        binding.Slider.RegisterValueChangedCallback(binding.SliderChanged);
        binding.Field.RegisterValueChangedCallback(binding.FieldChanged);
        binding.Field.RegisterCallback(binding.FieldGeometryChanged);

        ConfigureTextFieldInput(binding.Field);

        // UI Toolkit 的内部输入元素有时会在首帧布局后才稳定，因此延迟再应用一次样式。
        binding.Field.schedule.Execute(() => ConfigureTextFieldInput(binding.Field));
    }

    private void BindIntParam(VisualElement root, IntParamBinding binding)
    {
        // 整数参数使用 SliderInt，但输入框仍使用 TextField，方便统一解决内部文字显示样式。
        binding.Slider = root.Q<SliderInt>(binding.SliderName);
        binding.Field = root.Q<TextField>(binding.FieldName);

        if (binding.Slider == null)
        {
            Debug.LogError($"Int slider not found: {binding.SliderName}");
            return;
        }

        if (binding.Field == null)
        {
            Debug.LogError($"Int field not found: {binding.FieldName}");
            return;
        }

        ConfigureTextField(binding.Field);

        // 与 float 参数相同，int 参数也通过 binding 配置表进入统一处理流程。
        binding.SliderChanged = evt => OnIntSliderChanged(binding, evt.newValue);
        binding.FieldChanged = evt => OnIntFieldChanged(binding, evt.newValue);
        binding.FieldGeometryChanged = _ => ConfigureTextFieldInput(binding.Field);

        binding.Slider.RegisterValueChangedCallback(binding.SliderChanged);
        binding.Field.RegisterValueChangedCallback(binding.FieldChanged);
        binding.Field.RegisterCallback(binding.FieldGeometryChanged);

        ConfigureTextFieldInput(binding.Field);
        binding.Field.schedule.Execute(() => ConfigureTextFieldInput(binding.Field));
    }

    private void OnFloatSliderChanged(FloatParamBinding binding, float value)
    {
        // SyncAllControls 内部会写 Slider 值。此时忽略回调，防止出现递归刷新。
        if (isSyncingControls)
        {
            return;
        }

        ApplyFloatValue(binding, value);
    }

    private void OnFloatFieldChanged(FloatParamBinding binding, string text)
    {
        // TextField 设置为 delayed，通常在回车或失焦时提交。
        if (isSyncingControls)
        {
            return;
        }

        if (TryParseFloat(text, out float value))
        {
            ApplyFloatValue(binding, value);
            return;
        }

        // 输入非法时不写入参数，直接恢复为当前合法值。
        SyncAllControls();
    }

    private void OnIntSliderChanged(IntParamBinding binding, int value)
    {
        if (isSyncingControls)
        {
            return;
        }

        ApplyIntValue(binding, value);
    }

    private void OnIntFieldChanged(IntParamBinding binding, string text)
    {
        // int 输入框也允许用户输入类似 12.4，最终会 round 成 int，避免输入体验太僵硬。
        if (isSyncingControls)
        {
            return;
        }

        if (TryParseInt(text, out int value))
        {
            ApplyIntValue(binding, value);
            return;
        }

        SyncAllControls();
    }



    private void ApplyFloatValue(FloatParamBinding binding, float requestedValue)
    {
        // 参数写入流程：用户值 -> 当前参数副本 -> ClampValues 统一修正 -> UI 全量同步 -> 预览刷新。
        ChestLatentParams parameters = parameterState.CurrentParams;//取得当前参数对象
        ChestLatentParams previousParameters = parameters.Clone();
        float fallback = binding.GetValue(parameters);//记录旧值 fallback,主要用于防止用户输入奇怪的非法浮点数
        binding.SetValue(parameters, SanitizeFloat(requestedValue, fallback));
        parameters.ClampValues();
        SyncAllControls();//SyncAllControls() 是“把参数状态重新投射到 UI 上”的总刷新函数；

        if (AreParametersEquivalent(previousParameters, parameters))
        {
            return;
        }

        ParametersChanged?.Invoke();
        RenderPreview();
    }

    private void ApplyIntValue(IntParamBinding binding, int requestedValue)
    {
        // int 参数同样走 ClampValues，这样 lidSegments 的上下限只在数据层维护一份。
        ChestLatentParams parameters = parameterState.CurrentParams;
        ChestLatentParams previousParameters = parameters.Clone();
        binding.SetValue(parameters, requestedValue);
        parameters.ClampValues();
        SyncAllControls();

        if (AreParametersEquivalent(previousParameters, parameters))
        {
            return;
        }

        ParametersChanged?.Invoke();
        RenderPreview();
    }
    //把参数状态重新投射到 UI 上”的总刷新函数
    private void SyncAllControls()
    {
        // 任意参数变化都可能影响其他参数的 slider 范围，例如 lockerWidth 会影响 width 的最小值。
        // 因此这里选择全量同步，而不是只更新当前控件。
        if (parameterState == null)
        {
            return;
        }

        ChestLatentParams parameters = parameterState.CurrentParams;
        parameters.ClampValues();

        // 开启保护标记，避免 SetValueWithoutNotify 之外的样式/布局变化间接引起 Apply。
        isSyncingControls = true;

        foreach (FloatParamBinding binding in floatBindings)
        {
            SyncFloatBinding(binding, parameters);
        }

        foreach (IntParamBinding binding in intBindings)
        {
            SyncIntBinding(binding, parameters);
        }

        isSyncingControls = false;
    }

    // SyncFloatBinding() 和 SyncIntBinding() 是“按每个参数绑定规则，更新一个 Slider + TextField”的具体执行函数。
    private void SyncFloatBinding(FloatParamBinding binding, ChestLatentParams parameters)
    {
        // 根据当前参数状态动态计算 slider 范围，再把 clamp 后的值写回输入框和滑条。
        if (binding.Slider == null || binding.Field == null)
        {
            return;
        }

        float min = binding.GetMinValue(parameters);
        float max = Mathf.Max(min, binding.GetMaxValue(parameters));
        float value = Mathf.Clamp(binding.GetValue(parameters), min, max);

        binding.Slider.lowValue = min;
        binding.Slider.highValue = max;
        binding.Slider.SetValueWithoutNotify(value);
        binding.Field.SetValueWithoutNotify(FormatFloat(value));
        ConfigureTextFieldInput(binding.Field);
    }

    private void SyncIntBinding(IntParamBinding binding, ChestLatentParams parameters)
    {
        // 整数参数同步逻辑与 float 基本一致，只是 SliderInt 使用 int 范围和值。
        if (binding.Slider == null || binding.Field == null)
        {
            return;
        }

        int min = binding.GetMinValue(parameters);
        int max = Mathf.Max(min, binding.GetMaxValue(parameters));
        int value = Mathf.Clamp(binding.GetValue(parameters), min, max);

        binding.Slider.lowValue = min;
        binding.Slider.highValue = max;
        binding.Slider.SetValueWithoutNotify(value);
        binding.Field.SetValueWithoutNotify(value.ToString(CultureInfo.InvariantCulture));
        ConfigureTextFieldInput(binding.Field);
    }

    private void RenderPreview()
    {
        // 这里不直接操作 Camera / RenderTexture，而是交给 Chest3DPreviewUIController。
        if (previewController != null)
        {
            previewController.GenerateAndRenderPreview();
        }
    }

    private static void ConfigureTextField(TextField field)
    {
        // UI 上已经有单独的参数名 Label，因此 TextField 自己的 label 必须移除。
        // 如果只设 label = ""，Unity 默认 label 元素仍可能占宽，导致输入文字被压缩。
        field.label = string.Empty;
        if (field.labelElement.parent != null)
        {
            field.labelElement.RemoveFromHierarchy();
        }

        field.isPasswordField = false;
        field.isDelayed = true;
        field.maxLength = 16;
        field.selectAllOnFocus = true;
        field.style.width = 72f;
        field.style.minWidth = 72f;
        field.style.height = 26f;
        field.style.minHeight = 26f;
        field.style.flexShrink = 0f;
        field.style.flexDirection = FlexDirection.Row;
        field.style.alignItems = Align.Center;
        field.style.justifyContent = Justify.FlexStart;
        field.style.overflow = Overflow.Hidden;
        field.style.color = new Color(0.13f, 0.13f, 0.13f, 1f);
        field.style.paddingLeft = 0f;
        field.style.paddingRight = 0f;
        field.style.paddingTop = 0f;
        field.style.paddingBottom = 0f;
    }

    private static void ConfigureTextFieldInput(TextField field)
    {
        // TextField 的可见文字不在外层 TextField 上，而在运行时生成的 unity-text-input 内部。
        // UI Builder 不能直接稳定编辑这个内部节点，所以这里在运行时统一覆盖样式。
        if (field == null)
        {
            return;
        }

        VisualElement textInput = field.Q(TextInputBaseField<string>.textInputUssName);

        if (textInput == null)
        {
            return;
        }

        ApplyTextInputStyle(textInput);
        textInput.Query<VisualElement>().ForEach(ApplyTextInputStyle);
    }

    private static void ApplyTextInputStyle(VisualElement element)
    {
        // 同时处理 unity-text-input 以及它下面的文字子节点，覆盖 Unity 默认 Runtime Theme 的尺寸和边距。
        element.style.flexGrow = 1f;
        element.style.flexShrink = 1f;
        element.style.width = new StyleLength(Length.Percent(100f));
        element.style.minWidth = 0f;
        element.style.height = 24f;
        element.style.minHeight = 24f;
        element.style.marginLeft = 0f;
        element.style.marginRight = 0f;
        element.style.marginTop = 0f;
        element.style.marginBottom = 0f;
        element.style.paddingLeft = 4f;
        element.style.paddingRight = 4f;
        element.style.paddingTop = 0f;
        element.style.paddingBottom = 0f;
        element.style.color = new Color(0.13f, 0.13f, 0.13f, 1f);
        element.style.fontSize = 11f;
        element.style.unityTextAlign = TextAnchor.MiddleLeft;
        element.style.overflow = Overflow.Hidden;
        element.style.backgroundColor = Color.clear;
        element.style.borderLeftWidth = 0f;
        element.style.borderRightWidth = 0f;
        element.style.borderTopWidth = 0f;
        element.style.borderBottomWidth = 0f;
    }

    private static float GetMaxTaper(ChestLatentParams parameters)
    {
        // 当前 body mesh 中 taper 同时收缩底部宽度和前后深度。
        // 所以上限既要满足 width - taper > 0，也要避免前后底边在 z 方向交叉。
        float maxTaperByWidth = Mathf.Max(0f, parameters.width - ChestLatentParams.MinPositiveSize);
        float maxTaperByDepth = Mathf.Max(0f, parameters.depth * 0.5f - ChestLatentParams.MinPositiveSize);
        return Mathf.Min(maxTaperByWidth, maxTaperByDepth);
    }

    private static float SanitizeFloat(float value, float fallback)
    {
        // 防止 NaN / Infinity 进入参数对象，否则 mesh bounds 和相机适配会连锁出错。
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private static bool TryParseFloat(string text, out float value)
    {
        // 优先支持 invariant culture，保证小数点在不同系统语言下仍稳定；再兼容当前系统 culture。
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseInt(string text, out int value)
    {
        // 先按整数解析；失败时允许 float 输入并四舍五入，减少用户输入中的摩擦。
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            || int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        if (TryParseFloat(text, out float floatValue))
        {
            value = Mathf.RoundToInt(floatValue);
            return true;
        }

        value = default;
        return false;
    }

    private static string FormatFloat(float value)
    {
        // 参数面板只显示必要精度，避免 300.000000 这类数值挤占输入框空间。
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool AreParametersEquivalent(
        ChestLatentParams left,
        ChestLatentParams right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return Mathf.Approximately(left.width, right.width) &&
               Mathf.Approximately(left.height, right.height) &&
               Mathf.Approximately(left.depth, right.depth) &&
               Mathf.Approximately(left.bodyThickness, right.bodyThickness) &&
               Mathf.Approximately(left.taper, right.taper) &&
               Mathf.Approximately(left.lidHeight, right.lidHeight) &&
               left.lidSegments == right.lidSegments &&
               Mathf.Approximately(left.lidThickness, right.lidThickness) &&
               Mathf.Approximately(left.lockerWidth, right.lockerWidth) &&
               Mathf.Approximately(left.lockerHeight, right.lockerHeight) &&
               Mathf.Approximately(left.lockerDepth, right.lockerDepth) &&
               Mathf.Approximately(
                   left.lockerAnchorDepth,
                   right.lockerAnchorDepth);
    }

    private sealed class FloatParamBinding
    {
        // 一个 FloatParamBinding 描述一个 float 参数如何连接 UI 和数据：
        // SliderName / FieldName：UXML 控件名。
        // GetValue / SetValue：如何读写 ChestLatentParams。
        // GetMinValue / GetMaxValue：当前参数状态下 slider 的动态范围。
        public readonly string SliderName;
        public readonly string FieldName;
        public readonly Func<ChestLatentParams, float> GetValue;
        public readonly Action<ChestLatentParams, float> SetValue;
        public readonly Func<ChestLatentParams, float> GetMinValue;
        public readonly Func<ChestLatentParams, float> GetMaxValue;

        public Slider Slider;
        public TextField Field;

        // 保存委托实例，OnDisable 时才能准确注销同一个回调。
        public EventCallback<ChangeEvent<float>> SliderChanged;
        public EventCallback<ChangeEvent<string>> FieldChanged;
        public EventCallback<GeometryChangedEvent> FieldGeometryChanged;

        //FloatParamBinding的构造函数，一个数据配置类，创建“float如何绑定到UI控件”的配置记录
        public FloatParamBinding(
            string sliderName,
            string fieldName,
            Func<ChestLatentParams, float> getValue,//如何读值
            Action<ChestLatentParams, float> setValue,
            Func<ChestLatentParams, float> getMinValue,
            Func<ChestLatentParams, float> getMaxValue)
        {
            SliderName = sliderName;
            FieldName = fieldName;
            GetValue = getValue;
            SetValue = setValue;
            GetMinValue = getMinValue;
            GetMaxValue = getMaxValue;
        }
    }

    private sealed class IntParamBinding
    {
        // 整数参数版本。目前用于 lidSegments；保留单独类型是为了使用 SliderInt 并避免 float/int 转换混在一起。
        public readonly string SliderName;
        public readonly string FieldName;
        public readonly Func<ChestLatentParams, int> GetValue;
        public readonly Action<ChestLatentParams, int> SetValue;
        public readonly Func<ChestLatentParams, int> GetMinValue;
        public readonly Func<ChestLatentParams, int> GetMaxValue;

        public SliderInt Slider;
        public TextField Field;

        // 与 FloatParamBinding 一样，保存事件委托以便生命周期结束时解绑。
        public EventCallback<ChangeEvent<int>> SliderChanged;
        public EventCallback<ChangeEvent<string>> FieldChanged;
        public EventCallback<GeometryChangedEvent> FieldGeometryChanged;

        public IntParamBinding(
            string sliderName,
            string fieldName,
            Func<ChestLatentParams, int> getValue,
            Action<ChestLatentParams, int> setValue,
            Func<ChestLatentParams, int> getMinValue,
            Func<ChestLatentParams, int> getMaxValue)
        {
            SliderName = sliderName;
            FieldName = fieldName;
            GetValue = getValue;
            SetValue = setValue;
            GetMinValue = getMinValue;
            GetMaxValue = getMaxValue;
        }
    }
}
