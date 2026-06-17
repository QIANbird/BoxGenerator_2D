using UnityEngine;

// Holds the parameter preset stored in the scene and a separate runtime edit copy.
// UI controls should modify CurrentParams, not InitialParams, so preset defaults stay intact.
public class ChestParameterState : MonoBehaviour
{
    [SerializeField] private ChestLatentParams initialParams = new ChestLatentParams();

    private ChestLatentParams currentParams;

    public ChestLatentParams InitialParams
    {
        get
        {
            EnsureInitialParams();
            return initialParams;
        }
    }

    public ChestLatentParams CurrentParams
    {
        get
        {
            EnsureCurrentParams();
            return currentParams;
        }
    }

    public ChestLatentParams CreateParamsCopy()
    {
        EnsureCurrentParams();
        currentParams.ClampValues();
        return currentParams.Clone();
    }

    public void ResetToInitial()
    {
        EnsureInitialParams();
        currentParams = initialParams.Clone();
        currentParams.ClampValues();
    }

    public void SetCurrentParams(ChestLatentParams parameters)
    {
        currentParams = parameters != null ? parameters.Clone() : new ChestLatentParams();
        currentParams.ClampValues();
    }

    private void Awake()
    {
        EnsureInitialParams();
        ResetToInitial();
    }

    private void EnsureInitialParams()
    {
        if (initialParams == null)
        {
            initialParams = new ChestLatentParams();
        }

        initialParams.ClampValues();
    }

    private void EnsureCurrentParams()
    {
        if (currentParams == null)
        {
            ResetToInitial();
        }

        currentParams.ClampValues();
    }

    private void OnValidate()
    {
        EnsureInitialParams();

        if (!Application.isPlaying)
        {
            currentParams = null;
        }
    }
}
