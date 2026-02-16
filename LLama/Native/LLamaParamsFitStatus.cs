namespace LLama.Native
{
    /// <summary>
    /// Status returned by <see cref="NativeApi.llama_params_fit"/>
    /// </summary>
    public enum LLamaParamsFitStatus
    {
        /// <summary>
        /// Found allocations that are projected to fit
        /// </summary>
        Success = 0,

        /// <summary>
        /// Could not find allocations that are projected to fit
        /// </summary>
        Failure = 1,

        /// <summary>
        /// A hard error occurred, e.g. because no model could be found at the specified path
        /// </summary>
        Error = 2,
    }
}
