using System;
using System.IO;
using LLama.Abstractions;
using LLama.Common;
using LLama.Extensions;
using LLama.Native;

namespace LLama;

/// <summary>
/// Fits model and context parameters to available device memory before loading a model.
/// This probes device memory using dummy allocations and adjusts parameters (context size,
/// GPU layer count, tensor split, tensor buffer overrides) to maximize GPU utilization
/// without exceeding available memory.
/// <br/>
/// Based on llama.cpp's <c>llama_params_fit</c> function.
/// </summary>
public static class LLamaParamsFit
{
    /// <summary>
    /// Default per-device memory margin in bytes (1024 MiB)
    /// </summary>
    public const long DefaultMarginBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// Default minimum context size when reducing memory use
    /// </summary>
    public const uint DefaultMinContextSize = 4096;

    /// <summary>
    /// Adjust model and context parameters to fit within available device memory.
    /// Parameters that have been explicitly set by the user (i.e. differ from defaults) are not modified,
    /// except for context size which is only modified when set to 0 (meaning "use model default").
    /// <br/><br/>
    /// This method must be called <b>before</b> loading the model. It creates temporary dummy models
    /// internally to probe memory requirements.
    /// <br/><br/>
    /// <b>Warning:</b> This method is NOT thread safe because it modifies global llama logger state.
    /// </summary>
    /// <param name="params">The parameters to adjust. Both model and context properties may be modified.</param>
    /// <param name="marginBytes">Per-device memory margin to leave free, in bytes. Defaults to 1024 MiB.</param>
    /// <param name="minContextSize">Minimum context size to allow when reducing memory use. Defaults to 4096.</param>
    /// <param name="logLevel">Minimum log level to print during fitting. Defaults to <see cref="LLamaLogLevel.Info"/>.</param>
    /// <returns>The status of the fitting operation.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the model file does not exist.</exception>
    public static LLamaParamsFitStatus Fit(
        ModelParams @params,
        long marginBytes = DefaultMarginBytes,
        uint minContextSize = DefaultMinContextSize,
        LLamaLogLevel logLevel = LLamaLogLevel.Info)
    {
        if (!File.Exists(@params.ModelPath))
            throw new FileNotFoundException("Model file not found", @params.ModelPath);

        // Convert managed params to native structs
        using var modelParamsDisposer = ((IModelParams)@params).ToLlamaModelParams(out var nativeModelParams);
        ((IContextParams)@params).ToLlamaContextParams(out var nativeContextParams);

        // Call the native function
        var status = FitNative(
            @params.ModelPath,
            ref nativeModelParams,
            ref nativeContextParams,
            marginBytes,
            minContextSize,
            logLevel);

        if (status == LLamaParamsFitStatus.Success)
        {
            // Apply the modified native params back to the managed object
            ApplyFittedParams(@params, nativeModelParams, nativeContextParams);
        }

        return status;
    }

    /// <summary>
    /// Low-level method that calls <c>llama_params_fit</c> directly on native parameter structs.
    /// Prefer using <see cref="Fit(ModelParams, long, uint, LLamaLogLevel)"/> for a simpler API.
    /// <br/><br/>
    /// <b>Warning:</b> This method is NOT thread safe because it modifies global llama logger state.
    /// </summary>
    /// <param name="modelPath">Path to the GGUF model file</param>
    /// <param name="mparams">Native model parameters (modified in place on success)</param>
    /// <param name="cparams">Native context parameters (modified in place on success)</param>
    /// <param name="marginBytes">Per-device memory margin to leave free, in bytes</param>
    /// <param name="minContextSize">Minimum context size when reducing memory use</param>
    /// <param name="logLevel">Minimum log level during fitting</param>
    /// <returns>The status of the fitting operation.</returns>
    public static unsafe LLamaParamsFitStatus FitNative(
        string modelPath,
        ref LLamaModelParams mparams,
        ref LLamaContextParams cparams,
        long marginBytes = DefaultMarginBytes,
        uint minContextSize = DefaultMinContextSize,
        LLamaLogLevel logLevel = LLamaLogLevel.Info)
    {
        var maxDevices = (int)NativeApi.llama_max_devices();
        var maxOverrides = (int)NativeApi.llama_max_tensor_buft_overrides();

        // Allocate writable buffers for llama_params_fit to populate
        var tensorSplit = new float[maxDevices];
        var tensorBuftOverrides = new LLamaModelTensorBufferOverride[maxOverrides];
        var margins = new nuint[maxDevices];

        // Fill margins with the requested per-device margin
        var marginNuint = (nuint)(marginBytes > 0 ? marginBytes : 0);
        for (var i = 0; i < margins.Length; i++)
            margins[i] = marginNuint;

        fixed (float* tensorSplitPtr = tensorSplit)
        fixed (LLamaModelTensorBufferOverride* overridesPtr = tensorBuftOverrides)
        fixed (nuint* marginsPtr = margins)
        {
            // Point the model params at our writable buffers
            mparams.tensor_split = tensorSplitPtr;
            mparams.tensor_buft_overrides = overridesPtr;

            return NativeApi.llama_params_fit(
                modelPath,
                ref mparams,
                ref cparams,
                tensorSplitPtr,
                overridesPtr,
                marginsPtr,
                minContextSize,
                logLevel);
        }
    }

    private static void ApplyFittedParams(ModelParams @params, LLamaModelParams nativeModel, LLamaContextParams nativeContext)
    {
        // Apply context size (the fit function sets this when it was 0)
        if (nativeContext.n_ctx > 0)
            @params.ContextSize = nativeContext.n_ctx;

        // Apply GPU layer count
        @params.GpuLayerCount = nativeModel.n_gpu_layers;

        // Apply tensor splits from native buffer
        unsafe
        {
            if (nativeModel.tensor_split != null)
            {
                var maxDevices = (int)NativeApi.llama_max_devices();
                for (var i = 0; i < maxDevices && i < @params.TensorSplits.Length; i++)
                    @params.TensorSplits[i] = nativeModel.tensor_split[i];
            }
        }
    }
}
