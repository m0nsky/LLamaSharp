// Import necessary namespaces from LLama libraries and .NET standard libraries.
using LLama.Common;                        // Provides common utility classes and helper functions for the LLama project.
using LLama.Native;                        // Contains definitions and interop wrappers for native LLama functions.
using LLama.Abstractions;                  // Provides interface definitions and abstract classes for LLama.
using System;                              // Contains basic system definitions.
using System.Collections.Generic;          // Provides generic collection classes (e.g., List<T>).
using System.IO;                           // Provides types for file I/O.
using System.Linq;                         // Provides LINQ query capabilities.
using System.Text.Json;                    // Provides JSON serialization and deserialization.
using System.Text.Json.Serialization;     // Provides attributes and converters for JSON serialization.
using System.Threading.Tasks;              // Provides types for asynchronous programming.
using LLama.Exceptions;                    // Contains custom exception types used within the LLama project.
using LLama.Sampling;                      // Contains classes and interfaces related to token sampling methods.
using Microsoft.Extensions.Logging;        // Provides logging functionalities.

namespace LLama
{
    /// <summary>
    /// The LLama executor for interactive mode.
    /// This class handles interactive generation of text (and optionally image embeddings)
    /// by managing state, tokenization, context management, and sampling.
    /// </summary>
    public class InteractiveExecutor : StatefulExecutorBase // Inherits from StatefulExecutorBase, which manages state and shared functionality.
    {
        // Field to track whether the executor is processing the initial prompt.
        private bool _is_prompt_run = true;
        
        // Fields related to LLava (likely used for multimodal/image embedding support):
        // Stores the position in the token list where image embeddings should be inserted.
        private int _EmbedImagePosition = -1;
        // Holds safe handles for image embeddings; these encapsulate image data for processing.
        private List<SafeLlavaImageEmbedHandle> _imageEmbedHandles = new List<SafeLlavaImageEmbedHandle>();
        // Indicates if the current prompt contains an embedded image (i.e. the "<image>" tag is present).
        private bool _imageInPrompt = false;

        // An optional sampling pipeline reference (note: in this snippet, it is declared but not used directly).
        private ISamplingPipeline? _pipeline;

        /// <summary>
        /// Constructor for InteractiveExecutor using a LLamaContext and an optional logger.
        /// </summary>
        /// <param name="context">The LLama context used for tokenization and inference.</param>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        public InteractiveExecutor(LLamaContext context, ILogger? logger = null)
            : base(context, logger) // Passes the context and logger to the base class constructor.
        {
        }
        
        /// <summary>
        /// Overloaded constructor that also accepts a LLavaWeights (e.g., for multimodal models).
        /// </summary>
        /// <param name="context">The LLama context for tokenization and inference.</param>
        /// <param name="clipModel">The LLavaWeights (CLIP model) used for image embeddings.</param>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        public InteractiveExecutor(LLamaContext context, LLavaWeights clipModel, ILogger? logger = null)
            : base(context, clipModel, logger) // Passes the context, clipModel, and logger to the base class.
        {
        }        

        /// <inheritdoc />
        public override ExecutorBaseState GetStateData()
        {
            // Capture the current state of the executor in an InteractiveExecutorState object.
            InteractiveExecutorState state = new()
            {
                // The number of session tokens that have been consumed.
                ConsumedSessionCount = _n_session_consumed,
                // The pending input tokens (converted to an array).
                EmbedInps = _embed_inps.ToArray(),
                // Whether the executor is still running the initial prompt.
                IsPromptRun = _is_prompt_run,
                // Number of tokens that have already been processed.
                ConsumedTokensCount = _consumedTokensCount,
                // The tokens that have been embedded/decoded so far.
                Embeds = _embeds.ToArray(),
                // A fixed-size queue of the last generated tokens (for context/repetition handling).
                LastTokens = _last_n_tokens.ToArray(),
                // The count of tokens matching the session (used for reusing context).
                MatchingSessionTokensCount = _n_matching_session_tokens,
                // Total count of past tokens processed.
                PastTokensCount = _pastTokensCount,
                // File path used for saving or loading session data.
                SessionFilePath = _pathSession,
                // The session tokens stored so far.
                SessionTokens = _session_tokens.ToArray(),
                // The capacity of the fixed-size last tokens queue.
                LastTokensCapacity = _last_n_tokens.Capacity,
            };
            // Return the serialized state.
            return state;
        }
        
        /// <inheritdoc />
        public override Task LoadState(ExecutorBaseState data)
        {
            // Attempt to cast the provided state data to InteractiveExecutorState.
            if (data is InteractiveExecutorState state)
            {
                // Restore all internal state fields from the saved state.
                _n_session_consumed = state.ConsumedSessionCount;
                _embed_inps = state.EmbedInps.ToList();
                _is_prompt_run = state.IsPromptRun;
                _consumedTokensCount = state.ConsumedTokensCount;
                _embeds = state.Embeds.ToList();
                // Reconstruct the fixed-size queue of last tokens with its original capacity and contents.
                _last_n_tokens = new FixedSizeQueue<LLamaToken>(state.LastTokensCapacity, state.LastTokens);
                _n_matching_session_tokens = state.MatchingSessionTokensCount;
                _pastTokensCount = state.PastTokensCount;
                _pathSession = state.SessionFilePath;
                _session_tokens = state.SessionTokens.ToList();
            }
            else
                // If the provided state is not of the expected type, throw an exception.
                throw new ArgumentException("Invalid state data type.");

            // Return a completed task (this method completes synchronously).
            return Task.CompletedTask;
        }
        
        /// <inheritdoc />
        public override async Task SaveState(string filename)
        {
            // Retrieve the current state of the executor.
            var state = (InteractiveExecutorState)GetStateData();
            // Open a file stream to create or overwrite the specified file.
            using(var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                // Serialize the state object to JSON asynchronously and write it to the file.
                await JsonSerializer.SerializeAsync(fs, state);
            }
        }
        
        /// <inheritdoc />
        public override async Task LoadState(string filename)
        {
            // Open the specified file for reading.
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                // Asynchronously deserialize the JSON content into an InteractiveExecutorState.
                var state = await JsonSerializer.DeserializeAsync<InteractiveExecutorState>(fs);
                // Load the deserialized state into the executor.
                await LoadState(state);
            }
        }

        /// <summary>
        /// Define whether to continue the loop to generate responses.
        /// </summary>
        /// <returns>
        /// A Task that resolves to a boolean indicating whether generation should continue.
        /// Generation continues if there are remaining tokens and we are not waiting for new input,
        /// or if we are still processing the initial prompt.
        /// </returns>
        protected override Task<bool> GetLoopCondition(InferStateArgs args)
        {
            // Return true if either:
            // 1. There are remaining tokens and we are not in the "wait for input" state, or
            // 2. We are still in the initial prompt run.
            return Task.FromResult(args.RemainedTokens != 0 && !args.WaitForInput || _is_prompt_run);
        }

        /// <inheritdoc />
        protected override Task PreprocessInputs(string? text, InferStateArgs args)
        {
            // If this is the initial prompt run...
            if (_is_prompt_run)
            {
                // The prompt must not be null; throw an exception if it is.
                if (text == null) 
                    throw new ArgumentException("Prompt cannot be null to trigger continuation if a prompt has not been provided previously.");
                
                // For non-multimodal operation, tokenize the prompt text and replace the current input tokens.
                if (!this.IsMultiModal)
                {
                    _embed_inps = Context.Tokenize(text, true, true).ToList();
                }
                else
                {
                    // For multimodal operation, perform specialized preprocessing (e.g., for image embedding).
                    PreprocessLlava(text, args, true);
                }
            }
            else
            {
                // For subsequent inputs (not the initial prompt):
                if (text != null)
                {
                    // Ensure the input ends with a newline to delimit the prompt.
                    if (!text.EndsWith("\n"))
                    {
                        text += "\n";
                    }

                    // For non-multimodal, tokenize without adding a beginning-of-sequence token.
                    if (!this.IsMultiModal)
                    {
                        var line_inp = Context.Tokenize(text, false, true);
                        _embed_inps.AddRange(line_inp);   // Append the new tokens to the pending input tokens.
                        args.RemainedTokens -= line_inp.Length; // Decrease the token budget accordingly.
                    }
                    else
                    {
                        // For multimodal mode, call the specialized LLava preprocessing without BOS.
                        PreprocessLlava(text, args, false);
                    }
                }
            }

            // Return a completed task as no asynchronous work is needed here.
            return Task.CompletedTask;
        }

        /// <summary>
        /// Preprocesses multimodal (LLava) input.
        /// Handles the special case where an image is embedded in the prompt using the "<image>" tag.
        /// </summary>
        /// <param name="text">The input prompt text.</param>
        /// <param name="args">Inference state arguments including token budgets.</param>
        /// <param name="addBos">Indicates whether to add a beginning-of-sequence token.</param>
        /// <returns>A completed Task.</returns>
        private Task PreprocessLlava(string text, InferStateArgs args, bool addBos = true )
        {
            int usedTokens = 0; // Local counter for tokens processed (this value is updated but not used further in this snippet).
            
            // Check if the input text contains the "<image>" tag.
            _imageInPrompt = text.Contains("<image>");
            if (_imageInPrompt && IsMultiModal )
            {
                // For each image provided (Images is assumed to be defined elsewhere in multimodal context),
                // create a safe image embedding handle using the CLIP model.
                foreach (var image in Images)
                {
                    _imageEmbedHandles.Add(SafeLlavaImageEmbedHandle.CreateFromMemory(ClipModel.NativeHandle, Context, image));
                }

                // Find the index of the "<image>" tag in the prompt.
                int imageIndex = text.IndexOf("<image>");
                // Extract the segment of text before the image tag.
                string preImagePrompt = text.Substring(0, imageIndex);
                // Tokenize the segment before the image tag. 'addBos' determines whether to include the BOS token.
                var segment1 = Context.Tokenize(preImagePrompt, addBos, true);
                // Remember the token position where the image embedding should be inserted.
                _EmbedImagePosition = segment1.Length;
                // Extract the segment of text after the image tag.
                string postImagePrompt = text.Substring(imageIndex + 7); // Skip the "<image>" tag (7 characters long).
                // Tokenize the post-image segment without adding a BOS token.
                var segment2 = Context.Tokenize(postImagePrompt, false, true);
                // Append both segments to the list of input tokens for embedding.
                _embed_inps.AddRange(segment1);
                _embed_inps.AddRange(segment2);
                usedTokens += (segment1.Length + segment2.Length); // Update token count (for tracking/debugging).
            }
            else
            {
                // For non-image prompts or when not in multimodal mode:
                if (addBos)
                {
                    // Tokenize the entire prompt with a beginning-of-sequence token.
                    _embed_inps = Context.Tokenize(text, true, true).ToList();
                }
                else
                {
                    // Tokenize without adding a BOS token and append the tokens.
                    var line_inp = Context.Tokenize(text, false, true);
                    _embed_inps.AddRange(line_inp);
                    // Update the remaining token budget accordingly.
                    args.RemainedTokens -= line_inp.Length;                    
                }
            }
            return Task.CompletedTask; // Return a completed task.
        }
        
        /// <summary>
        /// Post-processes after token generation to decide whether to stop or continue generation.
        /// </summary>
        /// <param name="inferenceParams">The inference parameters used during generation.</param>
        /// <param name="args">The current inference state arguments.</param>
        /// <returns>
        /// A tuple where:
        /// - The first element is a boolean indicating if generation should be terminated.
        /// - The second element is a list of any final output strings (e.g., an end-of-text message).
        /// </returns>
        protected override async Task<(bool, IReadOnlyList<string>)> PostProcess(IInferenceParams inferenceParams, InferStateArgs args)
        {
            // If all the input tokens have been consumed...
            if (_embed_inps.Count <= _consumedTokensCount)
            {
                // Check if the last generated tokens end with any of the defined antiprompt strings.
                // This is used to decide if the model should wait for further user input.
                if (_last_n_tokens.TokensEndsWithAnyString(args.Antiprompts, Context.NativeHandle.ModelHandle, Context.Encoding))
                    args.WaitForInput = true;

                // If some tokens have already been generated and the model is waiting for input, break generation.
                if (_pastTokensCount > 0 && args.WaitForInput)
                    return (true, Array.Empty<string>());
            }

            // If the most recently generated token is the End-Of-Sequence (EOS) token...
            if (_embeds.Count > 0 && _embeds.Last() == Context.Vocab.EOS)
            {
                // Signal termination and return an end-of-text message.
                return (true, new[] { " [end of text]\n" });
            }

            // If the token budget has been exhausted (and a max tokens limit is set)...
            if (args.RemainedTokens <= 0 && inferenceParams.MaxTokens != -1)
            {
                // Reset the remaining tokens count to the maximum allowed.
                args.RemainedTokens = inferenceParams.MaxTokens;
                // Indicate that the executor should wait for further input.
                args.WaitForInput = true;
            }

            // Otherwise, continue generation.
            return (false, Array.Empty<string>());
        }

        /// <summary>
        /// The main internal inference method responsible for token generation.
        /// This method handles context management, multi-modal decoding, sampling, and batching.
        /// </summary>
        /// <param name="inferenceParams">Parameters controlling inference such as sampling configuration.</param>
        /// <param name="args">State arguments including token budgets and flags.</param>
        protected override async Task InferInternal(IInferenceParams inferenceParams, InferStateArgs args)
        {
            // Create a new batch to hold tokens for the decoding/inference process.
            var batch = new LLamaBatch();

            // If there are tokens waiting in the '_embeds' list to be processed...
            if (_embeds.Count > 0)
            {
                // Mark that the initial prompt run has completed.
                _is_prompt_run = false;
                // Check if adding the new tokens would exceed the model's context window.
                if (_pastTokensCount + _embeds.Count > Context.ContextSize)
                {
                    // Determine how many tokens to keep when resetting the context.
                    // This is ported from the llama.cpp example.
                    var tokensToKeep = inferenceParams.TokensKeep;
                    // If the tokens to keep are invalid or exceed the number of prompt tokens, keep all prompt tokens.
                    if (tokensToKeep < 0 || tokensToKeep > _embed_inps.Count)
                    {
                        tokensToKeep = _embed_inps.Count;
                    }
                    else
                    {
                        // Always include the beginning-of-sequence token if required.
                        tokensToKeep += Convert.ToInt32(Context.Vocab.ShouldAddBOS);
                    }

                    // Handle the scenario where the context window has been exceeded.
                    HandleRunOutOfContext(tokensToKeep);
                }

                // Attempt to reuse a matching prefix from the session history to save computation.
                TryReuseMatchingPrefix();

                // Variables to hold decoding results.
                (DecodeResult, int, int) header, end, result;
                // Check if we are in multimodal mode and if the image embedding position is set.
                if (IsMultiModal && _EmbedImagePosition > 0)
                {
                    // Decode tokens preceding the image embedding.
                    header = await Context.DecodeAsync(
                        _embeds.GetRange(0, _EmbedImagePosition), // Tokens before the image.
                        LLamaSeqId.Zero,                          // Sequence identifier (default/zero).
                        batch,                                    // Current batch.
                        _pastTokensCount);                        // Offset for past tokens.
                    // Update the count of past tokens.
                    _pastTokensCount = header.Item3;

                    // If the header decoding did not complete successfully, throw an error.
                    if (header.Item1 != DecodeResult.Ok) throw new LLamaDecodeError(header.Item1);
                   
                    // Process each image handle by evaluating its embedding.
                    foreach (var image in _imageEmbedHandles)
                        ClipModel.EvalImageEmbed(Context, image, ref _pastTokensCount);
                        
                    // Decode tokens following the image embedding.
                    end = await Context.DecodeAsync(
                        _embeds.GetRange(_EmbedImagePosition, _embeds.Count - _EmbedImagePosition), // Tokens after the image.
                        LLamaSeqId.Zero,
                        batch,
                        _pastTokensCount);
                    // Update the count of past tokens.
                    _pastTokensCount = end.Item3;

                    // Reset image-related state.
                    _EmbedImagePosition = -1;
                    _imageEmbedHandles.Clear();
                    Images.Clear(); // 'Images' is assumed to be a collection defined elsewhere.
                }
                else
                {
                    // For non-multimodal or when no image tag is present, decode the entire token list.
                    result = await Context.DecodeAsync(
                        _embeds,
                        LLamaSeqId.Zero,
                        batch,
                        _pastTokensCount);
                    // Update the past tokens count.
                    _pastTokensCount = result.Item3;

                    // Throw an error if decoding failed.
                    if (result.Item1 != DecodeResult.Ok) throw new LLamaDecodeError(result.Item1);
                }
                
                // If a session file is used and there were tokens processed,
                // add the processed tokens to the session for future reuse.
                if (_embeds.Count > 0 && !string.IsNullOrEmpty(_pathSession))
                {
                    _session_tokens.AddRange(_embeds);
                    _n_session_consumed = _session_tokens.Count;
                }
            }

            // Clear the embeds list as its tokens have now been processed.
            _embeds.Clear();

            // If there are no more pending input tokens and we are not waiting for user input...
            if (_embed_inps.Count <= _consumedTokensCount && !args.WaitForInput)
            {
                // Optionally save the session file on the first sample for faster future prompt loading.
                if (!string.IsNullOrEmpty(_pathSession) && args.NeedToSaveSession)
                {
                    args.NeedToSaveSession = false;
                    SaveSessionFile(_pathSession);
                }

                // Sample the next token using the provided sampling pipeline.
                // Note: 'batch.TokenCount - 1' is passed to indicate the current position for sampling.
                var id = inferenceParams.SamplingPipeline.Sample(Context.NativeHandle, batch.TokenCount - 1);

                // Record the sampled token in the history of recently generated tokens.
                _last_n_tokens.Enqueue(id);

                // If the sampled token is the EOS (end-of-sequence) token...
                if (id == Context.NativeHandle.ModelHandle.Vocab.EOS)
                {
                    // Replace it with a newline token.
                    id = Context.NativeHandle.ModelHandle.Vocab.Newline!.Value;
                    // If antiprompts are defined, tokenize the first one and add its tokens to the pending input list.
                    if (args.Antiprompts is not null && args.Antiprompts.Count > 0)
                    {
                        var first_antiprompt = Context.Tokenize(args.Antiprompts[0], false);
                        _embed_inps.AddRange(first_antiprompt);
                    }
                }

                // Add the sampled (or replaced) token to the embeds list for processing.
                _embeds.Add(id);

                // Decrement the remaining tokens counter.
                args.RemainedTokens--;
                // Indicate that a token was generated, so that any listening code may act upon it.
                args.ReturnValue = true;
            }
            else
            {
                // If there are still pending input tokens that haven’t been processed...
                while (_embed_inps.Count > _consumedTokensCount)
                {
                    // Add the next pending token to the embeds list.
                    _embeds.Add(_embed_inps[_consumedTokensCount]);
                    // Also record this token in the last tokens history.
                    _last_n_tokens.Enqueue(_embed_inps[_consumedTokensCount]);
                    // Increment the consumed tokens counter.
                    _consumedTokensCount++;
                    // If the current batch has reached the maximum batch size, stop adding more tokens.
                    if (_embeds.Count >= Context.BatchSize)
                    {
                        break;
                    }
                }
            }

            // End of the internal inference method.
            return;
        }

        /// <summary>
        /// The descriptor of the state of the interactive executor.
        /// This class extends ExecutorBaseState by adding properties specific to interactive execution.
        /// </summary>
        public class InteractiveExecutorState : ExecutorBaseState
        {
            /// <summary>
            /// Whether the executor is running for the first time (i.e., processing the prompt).
            /// This is used to differentiate prompt input from subsequent user input.
            /// </summary>
            [JsonPropertyName("is_prompt_run")]
            public bool IsPromptRun { get; set; }
        }
    }
}
