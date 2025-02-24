// Import interface definitions and abstract classes for LLama.
using LLama.Abstractions;                  // Provides base interfaces and abstract classes for executors.
// Import common utility functions and helper classes.
using LLama.Common;                        // Contains common utilities used throughout the LLama project.
// Import definitions and interop wrappers for native LLama functions.
using LLama.Native;                        // Enables interop with native code for inference.
// Import basic system definitions.
using System;                              // Provides fundamental types and base functionality.
// Import generic collections such as List<T>.
using System.Collections.Generic;          // Enables use of collections like List<T>.
// Import types for file I/O operations.
using System.IO;                           // Supports file reading and writing.
// Import LINQ query capabilities.
using System.Linq;                         // Provides LINQ extension methods for collections.
// Import JSON serialization and deserialization types.
using System.Text.Json;                    // Used for serializing objects to/from JSON.
// Import JSON serialization attributes and converters.
using System.Text.Json.Serialization;     // Enables customization of JSON serialization via attributes.
// Import asynchronous programming types (e.g., Task, async/await).
using System.Threading.Tasks;              // Supports asynchronous method definitions.
// Import custom exception types for the LLama project.
using LLama.Exceptions;                    // Provides project-specific exceptions.
// Import token sampling classes and interfaces.
using LLama.Sampling;                      // Contains classes for sampling tokens during inference.
// Import logging functionalities.
using Microsoft.Extensions.Logging;        // Provides logging capabilities.

namespace LLama  // Define the namespace for the LLama project.
{
    /// <summary>
    /// The LLama executor for instruct mode.
    /// In instruct mode, the executor guides the model's responses by using a structured template.
    /// It prepends an instruction prefix and appends an instruction suffix to the user prompt,
    /// so that the model understands where the instruction begins and where the expected response starts.
    /// Like InteractiveExecutor, it leverages the base class for context management, session handling,
    /// and asynchronous token streaming via InferAsync.
    /// </summary>
    public class InstructExecutor : StatefulExecutorBase  // Inherit from StatefulExecutorBase to share common state handling.
    {
        // Field indicating whether we are processing the initial prompt.
        private bool _is_prompt_run = true;  // True if we are on the first prompt, false for subsequent inputs.
        
        // Read-only field holding the instruction prefix string (e.g., "\n\n### Instruction:\n\n").
        private readonly string _instructionPrefix;  // This string is prepended to the user input to signal the instruction.
        
        // Field holding the tokenized instruction prefix.
        private LLamaToken[] _inp_pfx;  // Stores the tokenized version of the instruction prefix.
        
        // Field holding the tokenized instruction suffix (e.g., "\n\n### Response:\n\n").
        private LLamaToken[] _inp_sfx;  // Stores the tokenized version of the instruction suffix.
        
        // Optional reference to a sampling pipeline; may be configured externally if needed.
        private ISamplingPipeline? _pipeline;  // Not used directly here but can be set for custom sampling behavior.

        /// <summary>
        /// Constructs an InstructExecutor with the given context, instruction template, and optional logger.
        /// The instruction prefix and suffix are tokenized immediately for later use during input preprocessing.
        /// </summary>
        /// <param name="context">The LLama context for tokenization, inference, and model properties.</param>
        /// <param name="instructionPrefix">
        /// The instruction prefix string prepended to user prompts.
        /// Defaults to "\n\n### Instruction:\n\n".
        /// </param>
        /// <param name="instructionSuffix">
        /// The instruction suffix string appended after the user prompt.
        /// Defaults to "\n\n### Response:\n\n".
        /// </param>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        public InstructExecutor(
            LLamaContext context,                                      // Context for inference and tokenization.
            string instructionPrefix = "\n\n### Instruction:\n\n",      // Default instruction prefix.
            string instructionSuffix = "\n\n### Response:\n\n",         // Default instruction suffix.
            ILogger? logger = null)                                    // Optional logger.
            : base(context, logger)                                    // Call base class constructor with context and logger.
        {
            // Tokenize the instruction prefix while adding the beginning-of-sequence token.
            _inp_pfx = Context.Tokenize(instructionPrefix, true, true);  // Tokenize with BOS=true, add special tokens.
            // Tokenize the instruction suffix without adding the BOS token.
            _inp_sfx = Context.Tokenize(instructionSuffix, false, true); // Tokenize without BOS.
            // Store the instruction prefix string for later reference.
            _instructionPrefix = instructionPrefix;
        }

        /// <summary>
        /// Captures the current state of the executor in an InstructExecutorState object.
        /// This includes both shared state (from the base class) and instruct-specific token arrays.
        /// </summary>
        /// <returns>An ExecutorBaseState representing the current state.</returns>
        public override ExecutorBaseState GetStateData()  // Override to capture all internal state.
        {
            // Create a new InstructExecutorState and populate it with the current field values.
            InstructExecutorState state = new()
            {
                // Shared state from the base class.
                ConsumedSessionCount = _n_session_consumed,             // Number of tokens consumed in the session.
                EmbedInps = _embed_inps.ToArray(),                        // Array of pending input tokens.
                IsPromptRun = _is_prompt_run,                             // Whether this is the first prompt run.
                ConsumedTokensCount = _consumedTokensCount,               // Count of tokens already processed.
                Embeds = _embeds.ToArray(),                               // Array of tokens that have been generated.
                LastTokens = _last_n_tokens.ToArray(),                    // Recent history of generated tokens.
                // Instruct-specific state: tokenized instruction prefix.
                InputPrefixTokens = _inp_pfx,                             // Tokenized prefix template.
                // Instruct-specific state: tokenized instruction suffix.
                InputSuffixTokens = _inp_sfx,                             // Tokenized suffix template.
                MatchingSessionTokensCount = _n_matching_session_tokens,  // Count of tokens matching the session history.
                PastTokensCount = _pastTokensCount,                       // Total count of tokens processed so far.
                SessionFilePath = _pathSession,                           // File path used for session saving/loading.
                SessionTokens = _session_tokens.ToArray(),                // Array of tokens saved in the session.
                LastTokensCapacity = _last_n_tokens.Capacity,             // Capacity of the fixed-size token history.
            };
            return state;  // Return the constructed state object.
        }

        /// <summary>
        /// Restores the internal state from a provided ExecutorBaseState.
        /// Expects the state to be of type InstructExecutorState.
        /// </summary>
        /// <param name="data">The state data to restore.</param>
        /// <returns>A completed Task.</returns>
        public override Task LoadState(ExecutorBaseState data)  // Override to load state from a saved object.
        {
            // Check if the provided state is an InstructExecutorState.
            if (data is InstructExecutorState state)
            {
                // Restore shared state from the base class.
                _n_session_consumed = state.ConsumedSessionCount;     // Restore session consumed count.
                _embed_inps = state.EmbedInps.ToList();                 // Restore pending input tokens.
                _is_prompt_run = state.IsPromptRun;                     // Restore prompt run flag.
                _consumedTokensCount = state.ConsumedTokensCount;       // Restore consumed tokens count.
                _embeds = state.Embeds.ToList();                        // Restore generated tokens.
                _last_n_tokens = new FixedSizeQueue<LLamaToken>(state.LastTokensCapacity, state.LastTokens);  // Restore token history queue.
                // Restore instruct-specific token arrays.
                _inp_pfx = state.InputPrefixTokens;                     // Restore tokenized instruction prefix.
                _inp_sfx = state.InputSuffixTokens;                     // Restore tokenized instruction suffix.
                _n_matching_session_tokens = state.MatchingSessionTokensCount;  // Restore matching session tokens count.
                _pastTokensCount = state.PastTokensCount;               // Restore past tokens count.
                _pathSession = state.SessionFilePath;                   // Restore session file path.
                _session_tokens = state.SessionTokens.ToList();         // Restore session tokens list.
            }
            else  // If the provided state is not of the expected type:
            {
                throw new ArgumentException("Invalid state data type.");  // Throw an error indicating a type mismatch.
            }
            
            return Task.CompletedTask;  // Return a completed task since no asynchronous operations are needed.
        }

        /// <summary>
        /// Asynchronously saves the current executor state to a file in JSON format.
        /// </summary>
        /// <param name="filename">The file path to save the state.</param>
        /// <returns>A Task representing the asynchronous save operation.</returns>
        public override async Task SaveState(string filename)  // Override method for saving state.
        {
            var state = (InstructExecutorState)GetStateData();  // Get the current state as an InstructExecutorState.
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))  // Open a file stream for writing.
            {
                await JsonSerializer.SerializeAsync(fs, state);  // Serialize the state to JSON and write it to the file asynchronously.
            }
        }

        /// <summary>
        /// Asynchronously loads executor state from a specified file.
        /// The file must contain JSON representing an InstructExecutorState.
        /// </summary>
        /// <param name="filename">The file path from which to load the state.</param>
        /// <returns>A Task representing the asynchronous load operation.</returns>
        public override async Task LoadState(string filename)  // Override method for loading state.
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))  // Open a file stream for reading.
            {
                var state = await JsonSerializer.DeserializeAsync<InstructExecutorState>(fs);  // Deserialize the JSON data into an InstructExecutorState.
                await LoadState(state);  // Restore the state from the deserialized object.
            }
        }

        /// <summary>
        /// Determines whether the token generation loop should continue.
        /// In instruct mode, the loop continues as long as there are remaining tokens to generate
        /// or if the initial prompt run has not yet finished.
        /// </summary>
        /// <param name="args">Current inference state arguments (including token budgets).</param>
        /// <returns>A Task resolving to a boolean indicating whether generation should continue.</returns>
        protected override Task<bool> GetLoopCondition(InferStateArgs args)  // Override to specify loop continuation condition.
        {
            // Continue if there are remaining tokens or if still processing the initial prompt.
            return Task.FromResult(args.RemainedTokens != 0 || _is_prompt_run);
        }

        /// <summary>
        /// Preprocesses input text before token generation.
        /// For the initial prompt, the provided text is tokenized with a BOS token.
        /// For subsequent prompts, the instruction prefix and suffix are appended to the user input.
        /// </summary>
        /// <param name="text">The user prompt text; must be non-null for the first prompt.</param>
        /// <param name="args">Inference state arguments (e.g., token budgets, antiprompt list).</param>
        /// <returns>A completed Task.</returns>
        protected override Task PreprocessInputs(string? text, InferStateArgs args)  // Override input preprocessing.
        {
            // Ensure the antiprompt list is initialized; if null, assign an empty list.
            args.Antiprompts ??= new List<string>();
            // If the instruction prefix is not already in the antiprompt list, add it.
            // This helps signal to the model where the instruction starts.
            if (!args.Antiprompts.Contains(_instructionPrefix))
                args.Antiprompts.Add(_instructionPrefix);
            
            if (_is_prompt_run)  // If this is the initial prompt run:
            {
                if (text == null)  // Check that text is provided.
                    throw new ArgumentException("Prompt cannot be null to trigger continuation if a prompt has not been provided previously.");
                // Tokenize the user prompt text with a beginning-of-sequence token.
                _embed_inps = Context.Tokenize(text, true, true).ToList();
            }
            else  // For subsequent (continuation) prompts:
            {
                // Mark that all previous input tokens have been processed.
                _consumedTokensCount = _embed_inps.Count;
                
                if (text != null)  // If new input text is provided:
                {
                    // Ensure the text ends with a newline for proper token delimiting.
                    if (!text.EndsWith("\n"))
                    {
                        text += "\n";
                    }
                    // Append the tokenized instruction prefix (template) to the pending input tokens.
                    _embed_inps.AddRange(_inp_pfx);
                    
                    // Tokenize the new input text without adding a BOS token.
                    var line_inp = Context.Tokenize(text, false, true);
                    // Append the tokenized user input.
                    _embed_inps.AddRange(line_inp);
                    
                    // Append the tokenized instruction suffix (template) to the pending input tokens.
                    _embed_inps.AddRange(_inp_sfx);
                    
                    // Decrease the remaining token count by the number of tokens in the user input.
                    args.RemainedTokens -= line_inp.Length;
                }
            }
            
            return Task.CompletedTask;  // Return a completed task since no asynchronous work is needed.
        }

        /// <summary>
        /// Post-processes after token generation to decide whether to terminate or continue generation.
        /// Checks conditions such as:
        /// - Whether all input tokens have been processed and the generated tokens end with any antiprompt.
        /// - Whether the last generated token is the End-Of-Sequence (EOS) token.
        /// - Whether the token generation budget has been exhausted.
        /// </summary>
        /// <param name="inferenceParams">Parameters controlling sampling behavior.</param>
        /// <param name="args">Current inference state arguments.</param>
        /// <returns>
        /// A tuple where the first element is a boolean indicating if generation should stop,
        /// and the second element is a list of any final output strings (e.g., a prompt indicator).
        /// </returns>
        protected override async Task<(bool, IReadOnlyList<string>)> PostProcess(IInferenceParams inferenceParams, InferStateArgs args)  // Override post-processing.
        {
            if (_embed_inps.Count <= _consumedTokensCount)  // If all pending input tokens have been processed:
            {
                // Check if the recent generated tokens end with any antiprompt strings.
                if (_last_n_tokens.TokensEndsWithAnyString(args.Antiprompts, Context.NativeHandle.ModelHandle, Context.Encoding))
                {
                    args.WaitForInput = true;  // Set the flag to wait for further input.
                    return (true, Array.Empty<string>());  // Signal termination.
                }
                
                // If some tokens have been processed and we're waiting for input, return a prompt indicator.
                if (_pastTokensCount > 0 && args.WaitForInput)
                {
                    return (true, new[] { "\n> " });  // Return a simple prompt indicator.
                }
            }
            
            // If the last generated token is the EOS token, set the wait flag.
            if (_embeds.Count > 0 && _embeds.Last() == Context.Vocab.EOS)
            {
                args.WaitForInput = true;
            }
            
            // If the token generation budget is exhausted and a maximum is set, reset the budget and wait.
            if (args.RemainedTokens <= 0 && inferenceParams.MaxTokens != -1)
            {
                args.RemainedTokens = inferenceParams.MaxTokens;
                args.WaitForInput = true;
            }
            
            return (false, Array.Empty<string>());  // Otherwise, continue generation.
        }

        /// <summary>
        /// The core inference method responsible for token generation.
        /// This method is repeatedly invoked within the asynchronous generation loop.
        /// It handles:
        /// - Decoding the current tokens in the batch.
        /// - Recycling context if the total tokens exceed the model's context window.
        /// - Reusing matching session tokens (to accelerate inference).
        /// - Sampling the next token via the configured sampling pipeline.
        /// - Updating the session history and token counters.
        /// </summary>
        /// <param name="inferenceParams">Parameters controlling sampling and decoding behavior.</param>
        /// <param name="args">Current inference state arguments (e.g., token budgets, flags).</param>
        protected override async Task InferInternal(IInferenceParams inferenceParams, InferStateArgs args)  // Override the main inference logic.
        {
            // Create a new batch object to hold tokens for decoding.
            var batch = new LLamaBatch();
            
            if (_embeds.Count > 0)  // If there are tokens waiting to be processed:
            {
                _is_prompt_run = false;  // Mark that the initial prompt run has completed.
                
                // Check if the total tokens (past tokens + current batch) exceed the model's context window.
                if (_pastTokensCount + _embeds.Count > Context.ContextSize)
                {
                    // For instruct mode, always keep the entire input (prompt with instruction template).
                    var tokensToKeep = _embed_inps.Count;  // Set tokensToKeep to the entire prompt length.
                    // Recycle the context by discarding older tokens beyond the tokensToKeep count.
                    HandleRunOutOfContext(tokensToKeep);
                }
                
                // Attempt to reuse a matching prefix from the session history (to reduce computation).
                TryReuseMatchingPrefix();
                
                // Decode the tokens currently stored in _embeds.
                // The returned tuple contains the decode result, an unused value, and the updated past tokens count.
                var (result, _, pastTokensCount) = await Context.DecodeAsync(_embeds, LLamaSeqId.Zero, batch, _pastTokensCount);
                _pastTokensCount = pastTokensCount;  // Update the past tokens count.
                
                // If the decoding result is not successful, throw an exception.
                if (result != DecodeResult.Ok)
                    throw new LLamaDecodeError(result);
                
                // If a session file is used, update the session tokens history.
                if (_embeds.Count > 0 && !string.IsNullOrEmpty(_pathSession))
                {
                    _session_tokens.AddRange(_embeds);  // Append the processed tokens to the session history.
                    _n_session_consumed = _session_tokens.Count;  // Update the count of session tokens consumed.
                }
            }
            
            _embeds.Clear();  // Clear the embeds list since those tokens have been processed.
            
            if (_embed_inps.Count <= _consumedTokensCount && !args.WaitForInput)  // If no pending input tokens remain and we're not waiting:
            {
                // Optionally save the session file on the first sample to speed up future prompt loading.
                if (!string.IsNullOrEmpty(_pathSession) && args.NeedToSaveSession)
                {
                    args.NeedToSaveSession = false;  // Reset the flag indicating that a session save is needed.
                    SaveSessionFile(_pathSession);    // Save the session file.
                }
                
                // Sample the next token using the configured sampling pipeline.
                var id = inferenceParams.SamplingPipeline.Sample(Context.NativeHandle, batch.TokenCount - 1);
                
                _last_n_tokens.Enqueue(id);  // Enqueue the sampled token into the token history.
                _embeds.Add(id);             // Add the sampled token to the embeds list for processing.
                
                args.RemainedTokens--;       // Decrement the remaining token budget.
                args.ReturnValue = true;     // Signal that a token was generated.
            }
            else  // If there are still pending input tokens:
            {
                // Transfer pending input tokens to the embeds list until the batch size limit is reached.
                while (_embed_inps.Count > _consumedTokensCount)
                {
                    _embeds.Add(_embed_inps[_consumedTokensCount]);  // Add the next pending token.
                    _last_n_tokens.Enqueue(_embed_inps[_consumedTokensCount]);  // Record it in the token history.
                    _consumedTokensCount++;  // Increment the count of consumed input tokens.
                    if (_embeds.Count >= Context.BatchSize)  // Stop if the batch size limit is reached.
                    {
                        break;
                    }
                }
            }
            
            return;  // End of inference processing.
        }

        /// <summary>
        /// Represents the state of the InstructExecutor.
        /// This state object extends the base ExecutorBaseState with instruct-specific token arrays.
        /// </summary>
        public class InstructExecutorState : ExecutorBaseState  // Nested class for storing the executor state.
        {
            /// <summary>
            /// Indicates whether the executor is processing the initial prompt.
            /// </summary>
            [JsonPropertyName("is_prompt_run")]  // JSON property name for serialization.
            public bool IsPromptRun { get; set; }  // True if still in the first prompt run.
            
            /// <summary>
            /// Tokenized instruction prefix tokens.
            /// </summary>
            [JsonPropertyName("inp_pfx")]  // JSON property name.
            public LLamaToken[] InputPrefixTokens { get; set; }  // Stores the tokenized instruction prefix.
            
            /// <summary>
            /// Tokenized instruction suffix tokens.
            /// </summary>
            [JsonPropertyName("inp_sfx")]  // JSON property name.
            public LLamaToken[] InputSuffixTokens { get; set; }  // Stores the tokenized instruction suffix.
        }
    }
}
