using System.IO;
using LLama.Common;
using LLama.Native;
using Xunit;
using Xunit.Abstractions;

namespace LLama.Unittest
{
    public sealed class LLamaParamsFitTests
        : IDisposable
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly ModelParams _params;

        public LLamaParamsFitTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
            _params = new ModelParams(Constants.GenerativeModelPath2)
            {
                GpuLayerCount = Constants.CIGpuLayerCount
            };
        }

        public void Dispose()
        {
        }

        [Fact]
        public void FitStatusEnumMatchesNativeValues()
        {
            Assert.Equal(0, (int)LLamaParamsFitStatus.Success);
            Assert.Equal(1, (int)LLamaParamsFitStatus.Failure);
            Assert.Equal(2, (int)LLamaParamsFitStatus.Error);
        }

        [Fact]
        public void MaxTensorBuftOverridesReturnsPositive()
        {
            var max = NativeApi.llama_max_tensor_buft_overrides();
            Assert.True(max > 0, $"Expected llama_max_tensor_buft_overrides() > 0, got {max}");
        }

        [Fact]
        public void FitThrowsFileNotFoundForMissingModel()
        {
            var badParams = new ModelParams("/nonexistent/path/to/model.gguf");
            Assert.Throws<FileNotFoundException>(() => LLamaParamsFit.Fit(badParams));
        }

        [Fact]
        public void FitDoesNotReturnError()
        {
            var status = LLamaParamsFit.Fit(_params);

            _testOutputHelper.WriteLine($"Fit status: {status}");
            _testOutputHelper.WriteLine($"ContextSize: {_params.ContextSize}");
            _testOutputHelper.WriteLine($"GpuLayerCount: {_params.GpuLayerCount}");

            Assert.NotEqual(LLamaParamsFitStatus.Error, status);
        }

        [Fact]
        public void FitSucceedsWithNullContextSize()
        {
            _params.ContextSize = null;

            var status = LLamaParamsFit.Fit(_params);

            _testOutputHelper.WriteLine($"Fit status: {status}");
            _testOutputHelper.WriteLine($"ContextSize after fit: {_params.ContextSize}");
            _testOutputHelper.WriteLine($"GpuLayerCount after fit: {_params.GpuLayerCount}");

            Assert.Equal(LLamaParamsFitStatus.Success, status);

            // When fit adjusts the context size it will be set to a positive value.
            // On CPU-only runs (no device memory pressure) it may stay null (meaning "use model default").
            if (_params.ContextSize.HasValue)
                Assert.True(_params.ContextSize > 0);
        }

        [Fact]
        public void FitPreservesExplicitContextSize()
        {
            _params.ContextSize = 128;

            var status = LLamaParamsFit.Fit(_params);

            _testOutputHelper.WriteLine($"Fit status: {status}");
            _testOutputHelper.WriteLine($"ContextSize after fit: {_params.ContextSize}");

            // Explicit context size should not be changed by fit
            Assert.Equal(128u, _params.ContextSize);
        }
    }
}
