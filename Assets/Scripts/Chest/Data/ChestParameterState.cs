using UnityEngine;

// 场景中的参数状态组件：保存 Inspector 默认值，并提供安全的参数副本。
public class ChestParameterState : MonoBehaviour
{
    [SerializeField] private ChestLatentParams initialParams = new ChestLatentParams();

    public ChestLatentParams InitialParams => initialParams;

    public ChestLatentParams CreateParamsCopy()
    {
        if (initialParams == null)
        {
            initialParams = new ChestLatentParams();
        }

        initialParams.ClampValues();
        return initialParams.Clone();
    }

    private void OnValidate()
    {
        if (initialParams == null)
        {
            initialParams = new ChestLatentParams();
        }

        initialParams.ClampValues();
    }
}
