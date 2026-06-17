using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

// Binds the parameter panel controls to ChestParameterState.CurrentParams.
// This controller edits the runtime copy only; preset/default values remain unchanged.
[RequireComponent(typeof(UIDocument))]
public class ChestParameterPanelController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ChestParameterState parameterState;
    [SerializeField] private Chest3DPreviewUIController previewController;

    [Header("Params UI")]
    [SerializeField] private string parameterScrollName = "ParameterScroll";

    private readonly List<FloatParamBinding> floatBindings = new List<FloatParamBinding>();
    private readonly List<IntParamBinding> intBindings = new List<IntParamBinding>();

    private ScrollView parameterScroll;
    private bool isSyncingControls;

    private void OnEnable()
    {
        ResolveReferences();
        BuildParameterBindings();
        BindUI();
    }

    private void OnDisable()
    {
        UnbindUI();
    }

    private void ResolveReferences()
    {
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
        floatBindings.Clear();
        intBindings.Clear();

        floatBindings.Add(new FloatParamBinding(
            "Param_width",
            "Param_width_Field",
            p => p.width,
            (p, value) => p.width = value,
            p => Mathf.Max(ChestLatentParams.MinWidth, p.lockerWidth),
            _ => ChestLatentParams.MaxWidth));

        floatBindings.Add(new FloatParamBinding(
            "Param_height",
            "Param_height_Field",
            p => p.height,
            (p, value) => p.height = value,
            p => Mathf.Max(ChestLatentParams.MinPositiveSize, p.lockerHeight * 0.5f),
            _ => ChestLatentParams.MaxSize));

        floatBindings.Add(new FloatParamBinding(
            "Param_depth",
            "Param_depth_Field",
            p => p.depth,
            (p, value) => p.depth = value,
            _ => ChestLatentParams.MinPositiveSize,
            _ => ChestLatentParams.MaxSize));

        floatBindings.Add(new FloatParamBinding(
            "Param_taper",
            "Param_taper_Field",
            p => p.taper,
            (p, value) => p.taper = value,
            _ => 0f,
            p => GetMaxTaper(p)));

        floatBindings.Add(new FloatParamBinding(
            "Param_lidHeight",
            "Param_lidHeight_Field",
            p => p.lidHeight,
            (p, value) => p.lidHeight = value,
            _ => ChestLatentParams.MinPositiveSize,
            _ => ChestLatentParams.MaxSize));

        intBindings.Add(new IntParamBinding(
            "Param_lidSegments",
            "Param_lidSegments_Field",
            p => p.lidSegments,
            (p, value) => p.lidSegments = value,
            _ => ChestLatentParams.MinLidSegments,
            _ => ChestLatentParams.MaxLidSegments));

        floatBindings.Add(new FloatParamBinding(
            "Param_lockerWidth",
            "Param_lockerWidth_Field",
            p => p.lockerWidth,
            (p, value) => p.lockerWidth = value,
            _ => ChestLatentParams.MinLockerWidth,
            _ => ChestLatentParams.MaxWidth));

        floatBindings.Add(new FloatParamBinding(
            "Param_lockerHeight",
            "Param_lockerHeight_Field",
            p => p.lockerHeight,
            (p, value) => p.lockerHeight = value,
            _ => ChestLatentParams.MinPositiveSize,
            _ => ChestLatentParams.MaxSize));

        floatBindings.Add(new FloatParamBinding(
            "Param_lockerDepth",
            "Param_lockerDepth_Field",
            p => p.lockerDepth,
            (p, value) => p.lockerDepth = value,
            _ => ChestLatentParams.MinPositiveSize,
            p => Mathf.Max(ChestLatentParams.MinPositiveSize, Mathf.Min(p.width, p.depth) * 0.2f)));

        floatBindings.Add(new FloatParamBinding(
            "Param_bodyThickness",
            "Param_bodyThickness_Field",
            p => p.bodyThickness,
            (p, value) => p.bodyThickness = value,
            _ => ChestLatentParams.MinPositiveSize,
            p => Mathf.Max(ChestLatentParams.MinPositiveSize, Mathf.Min(p.width, p.height, p.depth) * 0.45f)));

        floatBindings.Add(new FloatParamBinding(
            "Param_lidThickness",
            "Param_lidThickness_Field",
            p => p.lidThickness,
            (p, value) => p.lidThickness = value,
            _ => ChestLatentParams.MinPositiveSize,
            p => Mathf.Max(ChestLatentParams.MinPositiveSize, Mathf.Min(p.width, p.lidHeight, p.depth) * 0.45f)));

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
        parameterScroll = root.Q<ScrollView>(parameterScrollName);
        ConfigureParameterScroll();

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

        binding.SliderChanged = evt => OnFloatSliderChanged(binding, evt.newValue);
        binding.FieldChanged = evt => OnFloatFieldChanged(binding, evt.newValue);
        binding.FieldGeometryChanged = _ => ConfigureTextFieldInput(binding.Field);

        binding.Slider.RegisterValueChangedCallback(binding.SliderChanged);
        binding.Field.RegisterValueChangedCallback(binding.FieldChanged);
        binding.Field.RegisterCallback(binding.FieldGeometryChanged);

        ConfigureTextFieldInput(binding.Field);
        binding.Field.schedule.Execute(() => ConfigureTextFieldInput(binding.Field));
    }

    private void BindIntParam(VisualElement root, IntParamBinding binding)
    {
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
        if (isSyncingControls)
        {
            return;
        }

        ApplyFloatValue(binding, value);
    }

    private void OnFloatFieldChanged(FloatParamBinding binding, string text)
    {
        if (isSyncingControls)
        {
            return;
        }

        if (TryParseFloat(text, out float value))
        {
            ApplyFloatValue(binding, value);
            return;
        }

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
        ChestLatentParams parameters = parameterState.CurrentParams;
        float fallback = binding.GetValue(parameters);
        binding.SetValue(parameters, SanitizeFloat(requestedValue, fallback));
        parameters.ClampValues();
        SyncAllControls();
        RenderPreview();
    }

    private void ApplyIntValue(IntParamBinding binding, int requestedValue)
    {
        ChestLatentParams parameters = parameterState.CurrentParams;
        binding.SetValue(parameters, requestedValue);
        parameters.ClampValues();
        SyncAllControls();
        RenderPreview();
    }

    private void SyncAllControls()
    {
        if (parameterState == null)
        {
            return;
        }

        ChestLatentParams parameters = parameterState.CurrentParams;
        parameters.ClampValues();

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

    private void SyncFloatBinding(FloatParamBinding binding, ChestLatentParams parameters)
    {
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
        if (previewController != null)
        {
            previewController.GenerateAndRenderPreview();
        }
    }

    private static void ConfigureTextField(TextField field)
    {
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
        float maxTaperByWidth = Mathf.Max(0f, parameters.width - ChestLatentParams.MinPositiveSize);
        float maxTaperByDepth = Mathf.Max(0f, parameters.depth * 0.5f - ChestLatentParams.MinPositiveSize);
        return Mathf.Min(maxTaperByWidth, maxTaperByDepth);
    }

    private static float SanitizeFloat(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseInt(string text, out int value)
    {
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
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private sealed class FloatParamBinding
    {
        public readonly string SliderName;
        public readonly string FieldName;
        public readonly Func<ChestLatentParams, float> GetValue;
        public readonly Action<ChestLatentParams, float> SetValue;
        public readonly Func<ChestLatentParams, float> GetMinValue;
        public readonly Func<ChestLatentParams, float> GetMaxValue;

        public Slider Slider;
        public TextField Field;
        public EventCallback<ChangeEvent<float>> SliderChanged;
        public EventCallback<ChangeEvent<string>> FieldChanged;
        public EventCallback<GeometryChangedEvent> FieldGeometryChanged;

        public FloatParamBinding(
            string sliderName,
            string fieldName,
            Func<ChestLatentParams, float> getValue,
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
        public readonly string SliderName;
        public readonly string FieldName;
        public readonly Func<ChestLatentParams, int> GetValue;
        public readonly Action<ChestLatentParams, int> SetValue;
        public readonly Func<ChestLatentParams, int> GetMinValue;
        public readonly Func<ChestLatentParams, int> GetMaxValue;

        public SliderInt Slider;
        public TextField Field;
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
