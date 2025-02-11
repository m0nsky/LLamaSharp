using LLama.Common;
using LLama.Native;
using LLama.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using LLama.Exceptions;
using LLama.Sampling;
using Microsoft.Extensions.Logging;

namespace LLama
{
    /// <summary>
    /// The LLama executor for interactive mode.
    /// In interactive mode the executor handles text generation (and optionally image embeddings)
    /// in a streaming, asynchronous fashion. It leverages the base class for context management,
    /// session handling (including context recycling via HandleRunOutOfContext and TryReuseMatchingPrefix),
    /// and asynchronous token streaming via InferAsync.
    /// </summary>
    public class InteractiveExecutor : StatefulExecutorBase
    {
        // Indicates whether the executor is processing the initial prompt.
        private bool _is_prompt_run = true;
        
        // Fields for multimodal (LLava) support:
        // _EmbedImagePosition marks where in the token sequence image embeddings should be inserted.
        private int _EmbedImagePosition = -1;
        // _imageEmbedHandles holds safe handles for image embeddings created from in-memory images.
        private List<SafeLlavaImageEmbedHandle> _imageEmbedHandles = new List<SafeLlavaImageEmbedHandle>();
        // Flag to denote whether the current prompt contains an image tag ("<image>").
        private bool _imageInPrompt = false;

        // Optional reference to a sampling pipeline (not used directly in this file but may be configured externally).
        private ISamplingPipeline? _pipeline;

        /// <summary>
        /// Constructs an InteractiveExecutor using the provided LLamaContext and optional logger.
        /// Relies on the base class for initialization of common state and streaming inference.
        /// </summary>
        /// <param name="context">The LLama context used for tokenization, inference, and accessing model properties.</param>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public InteractiveExecutor(LLamaContext context, ILogger? logger = null)
            : base(context, logger)
        {
        }
        
        /// <summary>
        /// Overloaded constructor that additionally accepts a LLavaWeights instance for multimodal processing.
        /// </summary>
        /// <param name="context">The LLama context.</param>
        /// <param name="clipModel">The LLavaWeights (CLIP model) for image embeddings.</param>
        /// <param name="logger">Optional logger.</param>
        public InteractiveExecutor(LLamaContext context, LLavaWeights clipModel, ILogger? logger = null)
            : base(context, clipModel, logger)
        {
        }        

        /// <inheritdoc />
        public override ExecutorBaseState GetStateData()
        {
            // Package the internal state into an InteractiveExecutorState.
            // This includes tokens processed so far, session and prompt state, and last token history.
            InteractiveExecutorState state = new()
            {
                ConsumedSessionCount = _n_session_consumed,
                EmbedInps = _embed_inps.ToArray(),
                IsPromptRun = _is_prompt_run,
                ConsumedTokensCount = _consumedTokensCount,
                Embeds = _embeds.ToArray(),
                LastTokens = _last_n_tokens.ToArray(),
                MatchingSessionTokensCount = _n_matching_session_tokens,
                PastTokensCount = _pastTokensCount,
                SessionFilePath = _pathSession,
                SessionTokens = _session_tokens.ToArray(),
                LastTokensCapacity = _last_n_tokens.Capacity,
            };
            return state;
        }
        
        /// <inheritdoc />
        public override Task LoadState(ExecutorBaseState data)
        {
            if (data is InteractiveExecutorState state)
            {
                // Restore the internal state from the saved state.
                _n_session_consumed = state.ConsumedSessionCount;
                _embed_inps = state.EmbedInps.ToList();
                _is_prompt_run = state.IsPromptRun;
                _consumedTokensCount = state.ConsumedTokensCount;
                _embeds = state.Embeds.ToList();
                _last_n_tokens = new FixedSizeQueue<LLamaToken>(state.LastTokensCapacity, state.LastTokens);
                _n_matching_session_tokens = state.MatchingSessionTokensCount;
                _pastTokensCount = state.PastTokensCount;
                _pathSession = state.SessionFilePath;
                _session_tokens = state.SessionTokens.ToList();
            }
            else
                throw new ArgumentException("Invalid state data type.");

            return Task.CompletedTask;
        }
        
        /// <inheritdoc />
        public override async Task SaveState(string filename)
        {
            var state = (InteractiveExecutorState)GetStateData();
            using(var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                await JsonSerializer.SerializeAsync(fs, state);
            }
        }
        
        /// <inheritdoc />
        public override async Task LoadState(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                var state = await JsonSerializer.DeserializeAsync<InteractiveExecutorState>(fs);
                await LoadState(state);
            }
        }

        /// <summary>
        /// Defines the condition under which the generation loop continues.
        /// The loop runs while there are remaining tokens to generate (and we're not waiting for user input)
        /// or if the initial prompt run is still active.
        /// </summary>
        /// <returns>A Task resolving to a boolean indicating whether to continue generation.</returns>
        protected override Task<bool> GetLoopCondition(InferStateArgs args)
        {
            return Task.FromResult(args.RemainedTokens != 0 && !args.WaitForInput || _is_prompt_run);
        }

        /// <inheritdoc />
        protected override Task PreprocessInputs(string? text, InferStateArgs args)
        {
            // For the first prompt in interactive mode, we preprocess it specially.
            if (_is_prompt_run)
            {
                // The prompt must be non-null on the initial run.
                if (text == null) throw new ArgumentException("Prompt cannot be null to trigger continuation if a prompt has not been provided previously.");
                // In non-multimodal mode, simply tokenize the entire prompt (adding a beginning-of-sequence token).
                if (!this.IsMultiModal)
                {
                    _embed_inps = Context.Tokenize(text, true, true).ToList();
                }
                else
                {
                    // In multimodal mode, process image embedding tags within the prompt.
                    PreprocessLlava(text, args, true);
                }
            }
            else
            {
                // For continuation prompts (i.e. subsequent inputs), do not reprocess the template.
                if (text != null)
                {
                    // Ensure the prompt ends with a newline for proper token delimiting.
                    if (!text.EndsWith("\n"))
                    {
                        text += "\n";
                    }

                    if (!this.IsMultiModal)
                    {
                        // Tokenize without adding a BOS token, then append to the pending input tokens.
                        var line_inp = Context.Tokenize(text, false, true);
                        _embed_inps.AddRange(line_inp);
                        args.RemainedTokens -= line_inp.Length;
                    }
                    else
                    {
                        PreprocessLlava(text, args, false);
                    }
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles multimodal (LLava) preprocessing by extracting image tags.
        /// If the prompt contains the "<image>" tag, this method tokenizes the segments
        /// before and after the tag separately, records the insertion point for image embeddings,
        /// and creates safe handles for each image in the Images collection.
        /// </summary>
        /// <param name="text">The input prompt text.</param>
        /// <param name="args">Inference state arguments (e.g., remaining token count).</param>
        /// <param name="addBos">Specifies whether to add the beginning-of-sequence token.</param>
        /// <returns>A completed Task.</returns>
        private Task PreprocessLlava(string text, InferStateArgs args, bool addBos = true )
        {
            int usedTokens = 0;
            
            // Check if the prompt contains the "<image>" tag.
            _imageInPrompt = text.Contains("<image>");
            if (_imageInPrompt && IsMultiModal)
            {
                // For each image provided in the Images list, create an embedding handle.
                foreach (var image in Images)
                {
                    _imageEmbedHandles.Add(SafeLlavaImageEmbedHandle.CreateFromMemory(ClipModel.NativeHandle, Context, image));
                }

                int imageIndex = text.IndexOf("<image>");
                // Tokenize the portion before the "<image>" tag. 'addBos' controls whether to add a BOS token.
                string preImagePrompt = text.Substring(0, imageIndex);
                var segment1 = Context.Tokenize(preImagePrompt, addBos, true);
                // Record the position where image embeddings will be injected.
                _EmbedImagePosition = segment1.Length;
                // Tokenize the portion after the "<image>" tag.
                string postImagePrompt = text.Substring(imageIndex + 7);
                var segment2 = Context.Tokenize(postImagePrompt, false, true);
                // Append both segments to the input token list.
                _embed_inps.AddRange(segment1);
                _embed_inps.AddRange(segment2);
                usedTokens += (segment1.Length + segment2.Length);
            }
            else
            {
                // For regular (non-image) prompts, tokenize the full text.
                if (addBos)
                {
                    _embed_inps = Context.Tokenize(text, true, true).ToList();
                }
                else
                {
                    var line_inp = Context.Tokenize(text, false, true);
                    _embed_inps.AddRange(line_inp);
                    args.RemainedTokens -= line_inp.Length;                    
                }
            }
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Performs post-processing after token generation to decide if generation should stop.
        /// Checks for conditions such as:
        /// - All input tokens have been consumed.
        /// - The last generated tokens match any antiprompt strings.
        /// - An EOS (end-of-sequence) token has been generated.
        /// - The token generation budget is exhausted.
        /// </summary>
        /// <param name="inferenceParams">Inference parameters controlling sampling.</param>
        /// <param name="args">Inference state arguments.</param>
        /// <returns>
        /// A tuple where the first item is a boolean indicating if generation should break,
        /// and the second item is a list of any extra output strings (e.g., a termination message).
        /// </returns>
        protected override async Task<(bool, IReadOnlyList<string>)> PostProcess(IInferenceParams inferenceParams, InferStateArgs args)
        {
            if (_embed_inps.Count <= _consumedTokensCount)
            {
                // Use the antiprompt mechanism (defined in the base class) to decide if the model should wait.
                if (_last_n_tokens.TokensEndsWithAnyString(args.Antiprompts, Context.NativeHandle.ModelHandle, Context.Encoding))
                    args.WaitForInput = true;

                if (_pastTokensCount > 0 && args.WaitForInput)
                    return (true, Array.Empty<string>());
            }

            // If an EOS token was generated, signal termination with an end-of-text message.
            if (_embeds.Count > 0 && _embeds.Last() == Context.Vocab.EOS)
            {
                return (true, new[] { " [end of text]\n" });
            }

            // If token budget is exhausted and a max tokens limit is set, reset the token count and wait for input.
            if (args.RemainedTokens <= 0 && inferenceParams.MaxTokens != -1)
            {
                args.RemainedTokens = inferenceParams.MaxTokens;
                args.WaitForInput = true;
            }

            return (false, Array.Empty<string>());
        }

        /// <summary>
        /// The core inference logic for interactive mode.
        /// This method is repeatedly called within the base class's asynchronous generation loop (InferAsync).
        /// It handles:
        /// - Context recycling (via HandleRunOutOfContext) if the model's context window is exceeded.
        /// - Reusing matching session tokens from a previously saved session (via TryReuseMatchingPrefix).
        /// - Decoding the current tokens (with support for multimodal image embedding when applicable).
        /// - Sampling the next token using the provided sampling pipeline.
        /// - Integrating new tokens into the session and token history.
        /// </summary>
        /// <param name="inferenceParams">Parameters for sampling and decoding.</param>
        /// <param name="args">Inference state arguments (e.g., token budgets, flags).</param>
        protected override async Task InferInternal(IInferenceParams inferenceParams, InferStateArgs args)
        {
            var batch = new LLamaBatch();

            if (_embeds.Count > 0)
            {
                // Mark that the initial prompt run is over.
                _is_prompt_run = false;
                // If adding the current tokens would exceed the model's context window...
                if (_pastTokensCount + _embeds.Count > Context.ContextSize)
                {
                    // Determine how many tokens to keep. In interactive mode, this may be adjusted by inferenceParams.TokensKeep.
                    var tokensToKeep = inferenceParams.TokensKeep;
                    if (tokensToKeep < 0 || tokensToKeep > _embed_inps.Count)
                    {
                        tokensToKeep = _embed_inps.Count;
                    }
                    else
                    {
                        // Always include the beginning-of-sequence token if required.
                        tokensToKeep += Convert.ToInt32(Context.Vocab.ShouldAddBOS);
                    }

                    // Recycle the context by discarding some tokens and recomputing logits.
                    HandleRunOutOfContext(tokensToKeep);
                }

                // Attempt to reuse matching tokens from a saved session to save computation.
                TryReuseMatchingPrefix();

                // Support for multimodal decoding:
                // If in multimodal mode and an image embedding position is set, process tokens before and after the image tag separately.
                (DecodeResult, int, int) header, end, result;
                if (IsMultiModal &&  _EmbedImagePosition > 0)
                {
                    // Decode tokens preceding the image embedding.
                    header = await Context.DecodeAsync(_embeds.GetRange(0, _EmbedImagePosition), LLamaSeqId.Zero, batch, _pastTokensCount);
                    _pastTokensCount = header.Item3;

                    if (header.Item1 != DecodeResult.Ok) throw new LLamaDecodeError(header.Item1);
                   
                    // Evaluate image embeddings for each image.
                    foreach( var image in _imageEmbedHandles )
                        ClipModel.EvalImageEmbed(Context, image, ref _pastTokensCount);
                        
                    // Decode tokens following the image embedding.
                    end = await Context.DecodeAsync(_embeds.GetRange(_EmbedImagePosition, _embeds.Count - _EmbedImagePosition), LLamaSeqId.Zero, batch, _pastTokensCount);
                    _pastTokensCount = end.Item3;

                    // Reset image-related state.
                    _EmbedImagePosition = -1;
                    _imageEmbedHandles.Clear();
                    Images.Clear();
                }
                else
                {
                    // Regular (non-multimodal) decoding.
                    result = await Context.DecodeAsync(_embeds, LLamaSeqId.Zero, batch, _pastTokensCount);
                    _pastTokensCount = result.Item3;

                    if (result.Item1 != DecodeResult.Ok) throw new LLamaDecodeError(result.Item1);
                }
                
                // If a session file is being used, append the new tokens to the session history.
                if (_embeds.Count > 0 && !string.IsNullOrEmpty(_pathSession))
                {
                    _session_tokens.AddRange(_embeds);
                    _n_session_consumed = _session_tokens.Count;
                }
            }

            // Clear the embeds list now that the tokens have been processed.
            _embeds.Clear();

            if (_embed_inps.Count <= _consumedTokensCount && !args.WaitForInput)
            {
                // Optionally save the session (KV cache) on the first sample to accelerate future prompt loading.
                if (!string.IsNullOrEmpty(_pathSession) && args.NeedToSaveSession)
                {
                    args.NeedToSaveSession = false;
                    SaveSessionFile(_pathSession);
                }

                // Sample the next token using the sampling pipeline.
                var id = inferenceParams.SamplingPipeline.Sample(Context.NativeHandle, batch.TokenCount - 1);

                // Record the sampled token in the last tokens history.
                _last_n_tokens.Enqueue(id);

                // If an EOS token is sampled, replace it with a newline token and append an antiprompt.
                if (id == Context.NativeHandle.ModelHandle.Vocab.EOS)
                {
                    id = Context.NativeHandle.ModelHandle.Vocab.Newline!.Value;
                    if (args.Antiprompts is not null && args.Antiprompts.Count > 0)
                    {
                        var first_antiprompt = Context.Tokenize(args.Antiprompts[0], false);
                        _embed_inps.AddRange(first_antiprompt);
                    }
                }

                _embeds.Add(id);

                args.RemainedTokens--;
                args.ReturnValue = true;
            }
            else
            {
                // If there are pending input tokens that haven't been processed, transfer them into the current batch.
                while (_embed_inps.Count > _consumedTokensCount)
                {
                    _embeds.Add(_embed_inps[_consumedTokensCount]);
                    _last_n_tokens.Enqueue(_embed_inps[_consumedTokensCount]);
                    _consumedTokensCount++;
                    if (_embeds.Count >= Context.BatchSize)
                    {
                        break;
                    }
                }
            }

            return;
        }

        /// <summary>
        /// The descriptor of the state of the interactive executor.
        /// This state object is used for saving/restoring the executor's session and prompt state.
        /// </summary>
        public class InteractiveExecutorState : ExecutorBaseState
        {
            /// <summary>
            /// Whether the executor is running for the first time (i.e., processing the initial prompt).
            /// </summary>
            [JsonPropertyName("is_prompt_run")]
            public bool IsPromptRun { get; set; }
        }
    }
}
