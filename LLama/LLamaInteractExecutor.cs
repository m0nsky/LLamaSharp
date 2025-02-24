// Import common utility classes and helper functions for the LLama project.
using LLama.Common;                        // Provides general-purpose utilities.
// Import definitions and interop wrappers for native LLama functions.
using LLama.Native;                        // Enables interop with native code.
// Import interface definitions and abstract classes for LLama.
using LLama.Abstractions;                  // Defines base types and interfaces.
// Import basic system definitions.
using System;                              // Provides fundamental system types.
// Import generic collection classes (e.g., List<T>).
using System.Collections.Generic;          // Enables use of collections such as List, Dictionary, etc.
// Import types for file input/output operations.
using System.IO;                           // Enables file reading and writing operations.
// Import LINQ query capabilities.
using System.Linq;                         // Provides LINQ extension methods for collections.
// Import JSON serialization and deserialization types.
using System.Text.Json;                    // Used for converting objects to/from JSON.
// Import JSON serialization attributes and converters.
using System.Text.Json.Serialization;     // Allows customization of JSON output/input.
// Import types for asynchronous programming (e.g., Task, async/await).
using System.Threading.Tasks;              // Enables asynchronous methods and operations.
// Import custom exception types for the LLama project.
using LLama.Exceptions;                    // Defines project-specific exceptions.
// Import token sampling classes and interfaces.
using LLama.Sampling;                      // Contains sampling methods for token generation.
// Import logging functionalities.
using Microsoft.Extensions.Logging;        // Provides logging support.

namespace LLama  // Define the namespace for the LLama project.
{
    /// <summary>
    /// The LLama executor for interactive mode.
    /// In interactive mode, this class handles text generation (and optionally image embeddings)
    /// in a streaming, asynchronous fashion. It manages state, tokenization, context management,
    /// session handling (including context recycling via HandleRunOutOfContext and TryReuseMatchingPrefix),
    /// and token sampling.
    /// </summary>
    public class InteractiveExecutor : StatefulExecutorBase  // Inherit from StatefulExecutorBase to reuse common state logic.
    {
        // Field indicating whether the executor is processing the initial prompt.
        private bool _is_prompt_run = true;  // True if the initial prompt is still being processed; false afterward.
        
        // Field for multimodal support: holds the index in the token sequence for image embedding insertion.
        private int _EmbedImagePosition = -1;  // Initialized to -1 meaning no image embedding position is currently set.
        // Field for multimodal support: stores safe handles for image embeddings created from in-memory images.
        private List<SafeLlavaImageEmbedHandle> _imageEmbedHandles = new List<SafeLlavaImageEmbedHandle>();  // Starts as an empty list.
        // Field for multimodal support: indicates if the current prompt contains an image tag ("<image>").
        private bool _imageInPrompt = false;  // False by default; set to true when an "<image>" tag is detected.
        
        // Optional reference to a sampling pipeline; it may be configured externally.
        private ISamplingPipeline? _pipeline;  // Nullable field; not used directly in this file.

        /// <summary>
        /// Constructs an InteractiveExecutor using the provided LLamaContext and an optional logger.
        /// Relies on the base class for initializing common state and streaming inference.
        /// </summary>
        /// <param name="context">The LLama context used for tokenization, inference, and accessing model properties.</param>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public InteractiveExecutor(LLamaContext context, ILogger? logger = null)  // Constructor with required context and optional logger.
            : base(context, logger)  // Pass context and logger to the base class constructor.
        {
            // No additional initialization required here.
        }
        
        /// <summary>
        /// Overloaded constructor that additionally accepts a LLavaWeights instance for multimodal processing.
        /// </summary>
        /// <param name="context">The LLama context.</param>
        /// <param name="clipModel">The LLavaWeights (CLIP model) for image embeddings.</param>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public InteractiveExecutor(LLamaContext context, LLavaWeights clipModel, ILogger? logger = null)  // Constructor including a multimodal model.
            : base(context, clipModel, logger)  // Call the base constructor with context, clip model, and logger.
        {
            // No extra initialization needed.
        }        

        /// <summary>
        /// Captures the current state of the executor into an InteractiveExecutorState object.
        /// This includes tokens processed so far, session and prompt state, and recent token history.
        /// </summary>
        /// <returns>An ExecutorBaseState representing the current state.</returns>
        public override ExecutorBaseState GetStateData()  // Override method to capture internal state.
        {
            // Create a new state object and populate its properties with the current field values.
            InteractiveExecutorState state = new()
            {
                ConsumedSessionCount = _n_session_consumed,            // Number of session tokens already processed.
                EmbedInps = _embed_inps.ToArray(),                       // Convert pending input tokens to an array.
                IsPromptRun = _is_prompt_run,                            // Whether we are still processing the initial prompt.
                ConsumedTokensCount = _consumedTokensCount,              // Number of tokens consumed from the input.
                Embeds = _embeds.ToArray(),                              // Array of tokens that have been generated.
                LastTokens = _last_n_tokens.ToArray(),                   // Array of the most recent tokens generated.
                MatchingSessionTokensCount = _n_matching_session_tokens, // Count of tokens matching session history.
                PastTokensCount = _pastTokensCount,                      // Total count of tokens processed in the past.
                SessionFilePath = _pathSession,                          // File path for saving/loading session data.
                SessionTokens = _session_tokens.ToArray(),               // Array of tokens stored in the session.
                LastTokensCapacity = _last_n_tokens.Capacity,            // The capacity of the fixed-size last tokens queue.
            };
            return state;  // Return the fully populated state object.
        }
        
        /// <summary>
        /// Restores the internal state from a provided ExecutorBaseState.
        /// Expects the state to be of type InteractiveExecutorState.
        /// </summary>
        /// <param name="data">The state data to restore.</param>
        /// <returns>A completed Task.</returns>
        public override Task LoadState(ExecutorBaseState data)  // Override method to load internal state.
        {
            // Check if the provided data is of the expected type.
            if (data is InteractiveExecutorState state)
            {
                _n_session_consumed = state.ConsumedSessionCount;  // Restore the count of consumed session tokens.
                _embed_inps = state.EmbedInps.ToList();              // Restore the pending input tokens.
                _is_prompt_run = state.IsPromptRun;                  // Restore whether we are still in the prompt run.
                _consumedTokensCount = state.ConsumedTokensCount;    // Restore how many input tokens have been consumed.
                _embeds = state.Embeds.ToList();                     // Restore the list of generated tokens.
                _last_n_tokens = new FixedSizeQueue<LLamaToken>(state.LastTokensCapacity, state.LastTokens);  // Recreate the fixed-size queue with its original capacity and content.
                _n_matching_session_tokens = state.MatchingSessionTokensCount;  // Restore count of matching session tokens.
                _pastTokensCount = state.PastTokensCount;            // Restore the total count of past tokens.
                _pathSession = state.SessionFilePath;                // Restore the session file path.
                _session_tokens = state.SessionTokens.ToList();      // Restore the session tokens list.
            }
            else  // If the provided state is not of the expected type:
                throw new ArgumentException("Invalid state data type.");  // Throw an exception indicating the error.
                
            return Task.CompletedTask;  // Return a task indicating that state loading is complete.
        }
        
        /// <summary>
        /// Asynchronously saves the current executor state to a file in JSON format.
        /// </summary>
        /// <param name="filename">The file path where the state should be saved.</param>
        /// <returns>A Task representing the asynchronous save operation.</returns>
        public override async Task SaveState(string filename)  // Override method to save state to a file.
        {
            var state = (InteractiveExecutorState)GetStateData();  // Get the current state as an InteractiveExecutorState object.
            using(var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))  // Open a file stream to create or overwrite the file.
            {
                await JsonSerializer.SerializeAsync(fs, state);  // Asynchronously serialize the state object to JSON and write it to the file.
            }
        }
        
        /// <summary>
        /// Asynchronously loads executor state from a specified file.
        /// The file is expected to contain JSON data representing an InteractiveExecutorState.
        /// </summary>
        /// <param name="filename">The file path from which to load the state.</param>
        /// <returns>A Task representing the asynchronous load operation.</returns>
        public override async Task LoadState(string filename)  // Override method to load state from a file.
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))  // Open a file stream to read from the specified file.
            {
                var state = await JsonSerializer.DeserializeAsync<InteractiveExecutorState>(fs);  // Deserialize the JSON data into an InteractiveExecutorState.
                await LoadState(state);  // Restore the state using the deserialized state object.
            }
        }
        
        /// <summary>
        /// Determines whether the token generation loop should continue.
        /// The loop continues if there are remaining tokens and the executor is not waiting for input,
        /// or if the initial prompt run is still active.
        /// </summary>
        /// <param name="args">The current inference state arguments including token budgets and flags.</param>
        /// <returns>A Task that resolves to a boolean indicating whether generation should continue.</returns>
        protected override Task<bool> GetLoopCondition(InferStateArgs args)  // Override method to define loop continuation conditions.
        {
            // Continue if there are tokens left and not waiting for input, or if still processing the initial prompt.
            return Task.FromResult(args.RemainedTokens != 0 && !args.WaitForInput || _is_prompt_run);
        }
        
        /// <summary>
        /// Preprocesses input text before token generation.
        /// For the initial prompt, the input is tokenized (or processed for image embeddings) and stored.
        /// For subsequent inputs, tokens are appended after ensuring that the text ends with a newline delimiter.
        /// </summary>
        /// <param name="text">The input prompt text; must be non-null for the initial prompt.</param>
        /// <param name="args">Inference state arguments (e.g., token budgets).</param>
        /// <returns>A completed Task.</returns>
        protected override Task PreprocessInputs(string? text, InferStateArgs args)  // Override method to preprocess input text.
        {
            if (_is_prompt_run)  // Check if this is the initial prompt run.
            {
                if (text == null)  // If the prompt text is null:
                    throw new ArgumentException("Prompt cannot be null to trigger continuation if a prompt has not been provided previously.");  // Throw an error.
                    
                if (!this.IsMultiModal)  // If the executor is not in multimodal mode:
                {
                    // Tokenize the entire prompt with a beginning-of-sequence token and convert to a list.
                    _embed_inps = Context.Tokenize(text, true, true).ToList();
                }
                else  // If in multimodal mode:
                {
                    // Process the prompt for image embeddings.
                    PreprocessLlava(text, args, true);
                }
            }
            else  // For subsequent (non-initial) inputs:
            {
                if (text != null)  // If input text is provided:
                {
                    if (!text.EndsWith("\n"))  // Check if the text does not end with a newline:
                    {
                        text += "\n";  // Append a newline to ensure proper token delimiting.
                    }
                    
                    if (!this.IsMultiModal)  // If not in multimodal mode:
                    {
                        // Tokenize the input text without adding a beginning-of-sequence token.
                        var line_inp = Context.Tokenize(text, false, true);
                        _embed_inps.AddRange(line_inp);  // Append the tokenized input to the pending inputs.
                        args.RemainedTokens -= line_inp.Length;  // Decrease the remaining token count by the number of tokens added.
                    }
                    else  // If in multimodal mode:
                    {
                        // Process the input text for image embeddings without a BOS token.
                        PreprocessLlava(text, args, false);
                    }
                }
            }
            
            return Task.CompletedTask;  // Return a completed task since no asynchronous work is performed here.
        }
        
        /// <summary>
        /// Preprocesses multimodal (LLava) input by detecting and handling the "<image>" tag.
        /// If the prompt contains "<image>" and multimodal mode is active, this method:
        /// - Creates safe image embedding handles for each image.
        /// - Tokenizes the prompt in segments before and after the image tag.
        /// - Records the token index where image embeddings should be inserted.
        /// For non-image prompts or non-multimodal mode, it tokenizes the prompt normally.
        /// </summary>
        /// <param name="text">The input prompt text.</param>
        /// <param name="args">Inference state arguments (e.g., remaining token count).</param>
        /// <param name="addBos">Specifies whether to add a beginning-of-sequence token to the first segment.</param>
        /// <returns>A completed Task.</returns>
        private Task PreprocessLlava(string text, InferStateArgs args, bool addBos = true)  // Private helper method for multimodal input processing.
        {
            int usedTokens = 0;  // Initialize a counter to track the number of tokens processed.
            
            _imageInPrompt = text.Contains("<image>");  // Check if the prompt text contains the "<image>" tag.
            if (_imageInPrompt && IsMultiModal)  // If an image tag is present and multimodal mode is enabled:
            {
                foreach (var image in Images)  // Iterate over each image in the Images collection.
                {
                    // Create a safe image embedding handle for the image using the CLIP model and add it to the list.
                    _imageEmbedHandles.Add(SafeLlavaImageEmbedHandle.CreateFromMemory(ClipModel.NativeHandle, Context, image));
                }
                
                int imageIndex = text.IndexOf("<image>");  // Find the starting index of the "<image>" tag.
                string preImagePrompt = text.Substring(0, imageIndex);  // Extract the text segment before the image tag.
                var segment1 = Context.Tokenize(preImagePrompt, addBos, true);  // Tokenize the pre-image segment, optionally adding a BOS token.
                _EmbedImagePosition = segment1.Length;  // Record the position where the image embedding tokens should be inserted.
                string postImagePrompt = text.Substring(imageIndex + 7);  // Extract the text segment after the "<image>" tag (skip 7 characters).
                var segment2 = Context.Tokenize(postImagePrompt, false, true);  // Tokenize the post-image segment without a BOS token.
                _embed_inps.AddRange(segment1);  // Add the pre-image tokens to the pending inputs.
                _embed_inps.AddRange(segment2);  // Add the post-image tokens to the pending inputs.
                usedTokens += (segment1.Length + segment2.Length);  // Update the counter with the total tokens processed.
            }
            else  // If no image tag is present or not in multimodal mode:
            {
                if (addBos)  // If a beginning-of-sequence token is required:
                {
                    // Tokenize the entire prompt with a BOS token and store it as the pending input tokens.
                    _embed_inps = Context.Tokenize(text, true, true).ToList();
                }
                else  // If no BOS token should be added:
                {
                    // Tokenize the prompt without a BOS token.
                    var line_inp = Context.Tokenize(text, false, true);
                    _embed_inps.AddRange(line_inp);  // Append these tokens to the pending inputs.
                    args.RemainedTokens -= line_inp.Length;  // Decrease the remaining token budget accordingly.
                }
            }
            
            return Task.CompletedTask;  // Return a completed task.
        }
        
        /// <summary>
        /// Post-processes after token generation to decide whether to terminate or continue generation.
        /// Checks conditions such as:
        /// - All input tokens have been processed and the last generated tokens match any antiprompt strings.
        /// - An End-Of-Sequence (EOS) token has been generated.
        /// - The token generation budget is exhausted.
        /// </summary>
        /// <param name="inferenceParams">Inference parameters controlling sampling behavior.</param>
        /// <param name="args">Current inference state arguments.</param>
        /// <returns>
        /// A tuple where the first element is a boolean indicating if generation should stop,
        /// and the second element is a list of any final output strings (e.g., an end-of-text message).
        /// </returns>
        protected override async Task<(bool, IReadOnlyList<string>)> PostProcess(IInferenceParams inferenceParams, InferStateArgs args)  // Override PostProcess for termination logic.
        {
            if (_embed_inps.Count <= _consumedTokensCount)  // Check if all pending input tokens have been processed.
            {
                // If the recent tokens end with any antiprompt strings, set the flag to wait for new input.
                if (_last_n_tokens.TokensEndsWithAnyString(args.Antiprompts, Context.NativeHandle.ModelHandle, Context.Encoding))
                    args.WaitForInput = true;
                    
                // If tokens have been generated and the executor is waiting for input, signal termination.
                if (_pastTokensCount > 0 && args.WaitForInput)
                    return (true, Array.Empty<string>());
            }
            
            if (_embeds.Count > 0 && _embeds.Last() == Context.Vocab.EOS)  // If the last generated token is the EOS token:
            {
                // Terminate generation and return an end-of-text message.
                return (true, new[] { " [end of text]\n" });
            }
            
            if (args.RemainedTokens <= 0 && inferenceParams.MaxTokens != -1)  // If the token budget is exhausted and a max token limit is set:
            {
                args.RemainedTokens = inferenceParams.MaxTokens;  // Reset the remaining token count to the max limit.
                args.WaitForInput = true;  // Set the flag to wait for further input.
            }
            
            return (false, Array.Empty<string>());  // Otherwise, continue generation with no additional output.
        }
        
        /// <summary>
        /// The core inference method responsible for token generation.
        /// This method is repeatedly invoked within the asynchronous generation loop.
        /// It handles:
        /// - Recycling context if the token count exceeds the model's context window.
        /// - Reusing matching tokens from a saved session to accelerate inference.
        /// - Decoding the current batch of tokens (with special handling for multimodal image embeddings if needed).
        /// - Sampling the next token using the configured sampling pipeline.
        /// - Incorporating the new token into the session and updating token history.
        /// </summary>
        /// <param name="inferenceParams">Parameters controlling sampling and decoding behavior.</param>
        /// <param name="args">Current inference state arguments (e.g., token budgets, flags).</param>
        protected override async Task InferInternal(IInferenceParams inferenceParams, InferStateArgs args)  // Override method for the main inference logic.
        {
            var batch = new LLamaBatch();  // Create a new batch object for token decoding.
            
            if (_embeds.Count > 0)  // Check if there are tokens waiting to be processed.
            {
                _is_prompt_run = false;  // Mark the initial prompt run as completed.
                
                // If processing these tokens would exceed the model's context window:
                if (_pastTokensCount + _embeds.Count > Context.ContextSize)
                {
                    var tokensToKeep = inferenceParams.TokensKeep;  // Start with the tokensToKeep value from inference parameters.
                    if (tokensToKeep < 0 || tokensToKeep > _embed_inps.Count)  // Validate tokensToKeep against available tokens.
                    {
                        tokensToKeep = _embed_inps.Count;  // If invalid, keep all available prompt tokens.
                    }
                    else
                    {
                        // Ensure inclusion of the beginning-of-sequence token if required.
                        tokensToKeep += Convert.ToInt32(Context.Vocab.ShouldAddBOS);
                    }
                    
                    // Recycle the context by discarding older tokens beyond the tokensToKeep count.
                    HandleRunOutOfContext(tokensToKeep);
                }
                
                // Attempt to reuse matching tokens from a previously saved session to speed up processing.
                TryReuseMatchingPrefix();
                
                // Declare variables to store the results of the decoding operations.
                (DecodeResult, int, int) header, end, result;
                if (IsMultiModal && _EmbedImagePosition > 0)  // Check if in multimodal mode and an image embedding position is defined.
                {
                    // Decode tokens before the image embedding position.
                    header = await Context.DecodeAsync(
                        _embeds.GetRange(0, _EmbedImagePosition),  // Get tokens preceding the "<image>" tag.
                        LLamaSeqId.Zero,                           // Use the default sequence identifier.
                        batch,                                     // Pass the current batch for decoding.
                        _pastTokensCount);                         // Provide the current count of past tokens.
                    _pastTokensCount = header.Item3;  // Update the past tokens count with the result from decoding.
                    
                    if (header.Item1 != DecodeResult.Ok)  // If header decoding was not successful:
                        throw new LLamaDecodeError(header.Item1);  // Throw a decode error.
                    
                    // For each image embedding handle, evaluate the image embedding using the CLIP model.
                    foreach (var image in _imageEmbedHandles)
                        ClipModel.EvalImageEmbed(Context, image, ref _pastTokensCount);
                        
                    // Decode tokens after the image embedding.
                    end = await Context.DecodeAsync(
                        _embeds.GetRange(_EmbedImagePosition, _embeds.Count - _EmbedImagePosition),  // Get tokens following the "<image>" tag.
                        LLamaSeqId.Zero,  // Use the default sequence identifier.
                        batch,            // Pass the current batch.
                        _pastTokensCount);  // Provide the updated past tokens count.
                    _pastTokensCount = end.Item3;  // Update the past tokens count with the result.
                    
                    // Reset image embedding related state after processing.
                    _EmbedImagePosition = -1;  // Reset the image embedding position.
                    _imageEmbedHandles.Clear();  // Clear all stored image embedding handles.
                    Images.Clear();             // Clear the Images collection (assumed to be defined elsewhere).
                }
                else  // If not in multimodal mode or no image embedding is needed:
                {
                    // Decode the entire token list.
                    result = await Context.DecodeAsync(
                        _embeds,           // Pass all tokens currently stored in _embeds.
                        LLamaSeqId.Zero,   // Use the default sequence identifier.
                        batch,             // Provide the current batch.
                        _pastTokensCount); // Provide the current past tokens count.
                    _pastTokensCount = result.Item3;  // Update the past tokens count with the result.
                    
                    if (result.Item1 != DecodeResult.Ok)  // If decoding the tokens was unsuccessful:
                        throw new LLamaDecodeError(result.Item1);  // Throw a decode error.
                }
                
                // If a session file is defined and tokens were processed:
                if (_embeds.Count > 0 && !string.IsNullOrEmpty(_pathSession))
                {
                    _session_tokens.AddRange(_embeds);  // Append the processed tokens to the session history.
                    _n_session_consumed = _session_tokens.Count;  // Update the count of consumed session tokens.
                }
            }
            
            _embeds.Clear();  // Clear the list of tokens to be processed as they have been handled.
            
            if (_embed_inps.Count <= _consumedTokensCount && !args.WaitForInput)  // If no pending input tokens remain and we are not waiting for input:
            {
                // If a session file is being used and a save is needed, perform the save.
                if (!string.IsNullOrEmpty(_pathSession) && args.NeedToSaveSession)
                {
                    args.NeedToSaveSession = false;  // Reset the flag indicating that a session save is needed.
                    SaveSessionFile(_pathSession);    // Save the session file to disk.
                }
                
                // Sample the next token using the configured sampling pipeline.
                var id = inferenceParams.SamplingPipeline.Sample(Context.NativeHandle, batch.TokenCount - 1);
                
                _last_n_tokens.Enqueue(id);  // Add the sampled token to the recent token history.
                
                if (id == Context.NativeHandle.ModelHandle.Vocab.EOS)  // If the sampled token is the End-Of-Sequence (EOS) token:
                {
                    id = Context.NativeHandle.ModelHandle.Vocab.Newline!.Value;  // Replace the EOS token with a newline token.
                    if (args.Antiprompts is not null && args.Antiprompts.Count > 0)  // If antiprompt strings are defined:
                    {
                        var first_antiprompt = Context.Tokenize(args.Antiprompts[0], false);  // Tokenize the first antiprompt.
                        _embed_inps.AddRange(first_antiprompt);  // Append these tokens to the pending inputs.
                    }
                }
                
                _embeds.Add(id);  // Add the sampled (or replaced) token to the embeds list.
                
                args.RemainedTokens--;  // Decrement the remaining token budget.
                args.ReturnValue = true;  // Indicate that a token has been generated.
            }
            else  // Else, if there are still pending input tokens:
            {
                // Continue transferring pending input tokens to the embeds list until the batch size is reached.
                while (_embed_inps.Count > _consumedTokensCount)
                {
                    _embeds.Add(_embed_inps[_consumedTokensCount]);  // Add the next pending token.
                    _last_n_tokens.Enqueue(_embed_inps[_consumedTokensCount]);  // Record this token in the recent history.
                    _consumedTokensCount++;  // Increment the counter for consumed tokens.
                    if (_embeds.Count >= Context.BatchSize)  // If the number of tokens in the current batch reaches the batch size:
                    {
                        break;  // Exit the loop.
                    }
                }
            }
            
            return;  // End of the inference method.
        }
        
        /// <summary>
        /// Represents the state of the InteractiveExecutor.
        /// This state object extends ExecutorBaseState to include properties specific to interactive execution,
        /// such as whether the initial prompt run is still active.
        /// </summary>
        public class InteractiveExecutorState : ExecutorBaseState  // Nested class for capturing the executor's state.
        {
            /// <summary>
            /// Indicates whether the executor is running for the first time (i.e., processing the initial prompt).
            /// </summary>
            [JsonPropertyName("is_prompt_run")]  // Specifies the JSON property name during serialization.
            public bool IsPromptRun { get; set; }  // Property to track if the initial prompt run is still in progress.
        }
    }
}
