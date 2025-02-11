// Import necessary namespaces from LLama libraries and .NET standard libraries.
using LLama.Abstractions;                  // Provides interface definitions and abstract classes for LLama.
using LLama.Common;                        // Contains common utilities and helper classes.
using LLama.Native;                        // Contains definitions and interop wrappers for native LLama functions.
using System;                              // Contains basic system definitions.
using System.Collections.Generic;          // Provides generic collections like List<T>.
using System.IO;                           // Provides types for file I/O.
using System.Linq;                         // Provides LINQ query capabilities.
using System.Text.Json;                    // Provides JSON serialization and deserialization.
using System.Text.Json.Serialization;     // Provides attributes and converters for JSON serialization.
using System.Threading.Tasks;              // Provides types for asynchronous programming.
using LLama.Exceptions;                    // Contains custom exceptions for LLama.
using LLama.Sampling;                      // Provides classes and interfaces for token sampling methods.
using Microsoft.Extensions.Logging;        // Provides logging functionalities.

namespace LLama
{
    /// <summary>
    /// The LLama executor for instruct mode.
    /// This class handles generation in "instruct" mode, which uses a structured template
    /// (with instruction prefix and suffix) to guide the model's responses.
    /// </summary>
    public class InstructExecutor : StatefulExecutorBase // Inherits from StatefulExecutorBase for stateful execution.
    {
        // Field to track whether the executor is processing the initial prompt.
        private bool _is_prompt_run = true;

        // Holds the instruction prefix string (e.g., "\n\n### Instruction:\n\n") that helps guide the model.
        private readonly string _instructionPrefix;
        // Holds the tokenized version of the instruction prefix.
        private LLamaToken[] _inp_pfx;
        // Holds the tokenized version of the instruction suffix (e.g., "\n\n### Response:\n\n").
        private LLamaToken[] _inp_sfx;

        // Optional reference to a sampling pipeline (declared here but not directly used in the snippet).
        private ISamplingPipeline? _pipeline;

        /// <summary>
        /// Constructs a new InstructExecutor with a given context, instruction prefix and suffix, and an optional logger.
        /// </summary>
        /// <param name="context">The LLama context used for tokenization and inference.</param>
        /// <param name="instructionPrefix">
        /// The instruction prefix string that is prepended to user prompts.
        /// Defaults to "\n\n### Instruction:\n\n".
        /// </param>
        /// <param name="instructionSuffix">
        /// The instruction suffix string that is appended after the user prompt.
        /// Defaults to "\n\n### Response:\n\n".
        /// </param>
        /// <param name="logger">An optional logger for diagnostic output.</param>
        public InstructExecutor(
            LLamaContext context,
            string instructionPrefix = "\n\n### Instruction:\n\n",
            string instructionSuffix = "\n\n### Response:\n\n",
            ILogger? logger = null)
            : base(context, logger) // Passes the context and logger to the base class constructor.
        {
            // Tokenize the instruction prefix with the beginning-of-sequence token (BOS) added.
            _inp_pfx = Context.Tokenize(instructionPrefix, true, true);
            // Tokenize the instruction suffix without adding a BOS token.
            _inp_sfx = Context.Tokenize(instructionSuffix, false, true);
            // Store the instruction prefix string.
            _instructionPrefix = instructionPrefix;
        }

        /// <inheritdoc />
        public override ExecutorBaseState GetStateData()
        {
            // Capture the current state of the executor in an InstructExecutorState object.
            InstructExecutorState state = new()
            {
                // Inherited state fields from the base class:
                ConsumedSessionCount = _n_session_consumed,
                EmbedInps = _embed_inps.ToArray(),
                IsPromptRun = _is_prompt_run,
                ConsumedTokensCount = _consumedTokensCount,
                Embeds = _embeds.ToArray(),
                LastTokens = _last_n_tokens.ToArray(),
                // Specific to instruct mode: include the tokenized instruction prefix.
                InputPrefixTokens = _inp_pfx,
                // Specific to instruct mode: include the tokenized instruction suffix.
                InputSuffixTokens = _inp_sfx,
                MatchingSessionTokensCount = _n_matching_session_tokens,
                PastTokensCount = _pastTokensCount,
                SessionFilePath = _pathSession,
                SessionTokens = _session_tokens.ToArray(),
                LastTokensCapacity = _last_n_tokens.Capacity,
            };
            // Return the constructed state object.
            return state;
        }

        /// <inheritdoc />
        public override Task LoadState(ExecutorBaseState data)
        {
            // Attempt to cast the provided state data to InstructExecutorState.
            if (data is InstructExecutorState state)
            {
                // Restore the inherited state fields.
                _n_session_consumed = state.ConsumedSessionCount;
                _embed_inps = state.EmbedInps.ToList();
                _is_prompt_run = state.IsPromptRun;
                _consumedTokensCount = state.ConsumedTokensCount;
                _embeds = state.Embeds.ToList();
                _last_n_tokens = new FixedSizeQueue<LLamaToken>(state.LastTokensCapacity, state.LastTokens);
                // Restore instruct-specific token arrays.
                _inp_pfx = state.InputPrefixTokens;
                _inp_sfx = state.InputSuffixTokens;
                _n_matching_session_tokens = state.MatchingSessionTokensCount;
                _pastTokensCount = state.PastTokensCount;
                _pathSession = state.SessionFilePath;
                _session_tokens = state.SessionTokens.ToList();
            }
            else
            {
                // If the provided state is not of the expected type, throw an exception.
                throw new ArgumentException("Invalid state data type.");
            }

            // Return a completed task (no asynchronous work needed).
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override async Task SaveState(string filename)
        {
            // Retrieve the current state data.
            var state = (InstructExecutorState)GetStateData();
            // Open a file stream to create or overwrite the file.
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                // Serialize the state asynchronously to JSON and write it to the file.
                await JsonSerializer.SerializeAsync(fs, state);
            }
        }

        /// <inheritdoc />
        public override async Task LoadState(string filename)
        {
            // Open the file for reading.
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                // Deserialize the JSON content into an InstructExecutorState object.
                var state = await JsonSerializer.DeserializeAsync<InstructExecutorState>(fs);
                // Load the deserialized state.
                await LoadState(state);
            }
        }

        /// <inheritdoc />
        protected override Task<bool> GetLoopCondition(InferStateArgs args)
        {
            // The generation loop continues if there are remaining tokens to generate
            // or if we are still processing the initial prompt.
            return Task.FromResult(args.RemainedTokens != 0 || _is_prompt_run);
        }

        /// <inheritdoc />
        protected override Task PreprocessInputs(string? text, InferStateArgs args)
        {
            // Ensure that the antiprompt list is not null.
            // NOTE: The following line uses a null-coalescing assignment. In valid C#,
            // it should assign an empty list if args.Antiprompts is null.
            args.Antiprompts ??= new List<string>();

            // If the antiprompt list does not already contain the instruction prefix,
            // add it. This helps the model understand where an instruction begins.
            if (!args.Antiprompts.Contains(_instructionPrefix))
                args.Antiprompts.Add(_instructionPrefix);

            // If this is the initial prompt run...
            if (_is_prompt_run)
            {
                // The provided prompt text must not be null; otherwise, throw an exception.
                if (text == null)
                    throw new ArgumentException("Prompt cannot be null to trigger continuation if a prompt has not been provided previously.");
                // Tokenize the prompt text with BOS and set the list of input tokens.
                _embed_inps = Context.Tokenize(text, true, true).ToList();
            }
            else
            {
                // For continuation requests, mark that all previous input tokens have been processed.
                _consumedTokensCount = _embed_inps.Count;

                // When a new prompt text is provided (non-null), process it:
                if (text != null)
                {
                    // Ensure the prompt ends with a newline to delimit it.
                    if (!text.EndsWith("\n"))
                    {
                        text += "\n";
                    }
                    // Append the tokenized instruction prefix (template) to the input tokens.
                    _embed_inps.AddRange(_inp_pfx);

                    // Tokenize the new input text without adding a BOS token.
                    var line_inp = Context.Tokenize(text, false, true);
                    // Append the tokenized user input.
                    _embed_inps.AddRange(line_inp);

                    // Append the tokenized instruction suffix (template) to the input tokens.
                    _embed_inps.AddRange(_inp_sfx);

                    // Deduct the number of tokens in the user input from the remaining tokens budget.
                    args.RemainedTokens -= line_inp.Length;
                }
            }

            // Return a completed task as no asynchronous work is needed here.
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task<(bool, IReadOnlyList<string>)> PostProcess(IInferenceParams inferenceParams, InferStateArgs args)
        {
            // If all input tokens have been consumed...
            if (_embed_inps.Count <= _consumedTokensCount)
            {
                // Check if the last generated tokens end with any of the antiprompts.
                if (_last_n_tokens.TokensEndsWithAnyString(args.Antiprompts, Context.NativeHandle.ModelHandle, Context.Encoding))
                {
                    // If so, signal that we should wait for further input.
                    args.WaitForInput = true;
                    return (true, Array.Empty<string>());
                }

                // If some tokens have been processed and we're waiting for input,
                // return a prompt indicator (here, "\n> ") to signal the user.
                if (_pastTokensCount > 0 && args.WaitForInput)
                {
                    return (true, new[] { "\n> " });
                }
            }

            // If the last generated token is the End-Of-Sequence (EOS) token...
            if (_embeds.Count > 0 && _embeds.Last() == Context.Vocab.EOS)
            {
                // Set the flag to wait for user input.
                args.WaitForInput = true;
            }

            // If the token budget is exhausted (and a max tokens limit is specified)...
            if (args.RemainedTokens <= 0 && inferenceParams.MaxTokens != -1)
            {
                // Reset the remaining tokens to the maximum allowed and set the flag to wait.
                args.RemainedTokens = inferenceParams.MaxTokens;
                args.WaitForInput = true;
            }
            // Continue generation.
            return (false, Array.Empty<string>());
        }

        /// <inheritdoc />
        protected override async Task InferInternal(IInferenceParams inferenceParams, InferStateArgs args)
        {
            // Create a new batch for token decoding/inference.
            var batch = new LLamaBatch();

            // If there are tokens waiting to be processed in the embeds list...
            if (_embeds.Count > 0)
            {
                // Mark that the initial prompt run has completed.
                _is_prompt_run = false;
                // Check if adding the new tokens would exceed the model's context window.
                if (_pastTokensCount + _embeds.Count > Context.ContextSize)
                {
                    // For instruct mode, always keep the entire input token size.
                    var tokensToKeep = _embed_inps.Count;
                    // Handle the case where the context window is exceeded.
                    HandleRunOutOfContext(tokensToKeep);
                }

                // Attempt to reuse a matching prefix from session history to reduce computation.
                TryReuseMatchingPrefix();

                // Decode the tokens currently in _embeds.
                // The returned tuple contains the decode result, an unused value, and the updated past tokens count.
                var (result, _, pastTokensCount) = await Context.DecodeAsync(_embeds, LLamaSeqId.Zero, batch, _pastTokensCount);
                _pastTokensCount = pastTokensCount;

                // If decoding did not complete successfully, throw an exception.
                if (result != DecodeResult.Ok)
                    throw new LLamaDecodeError(result);

                // If a session file is being used, append the processed tokens to the session history.
                if (_embeds.Count > 0 && !string.IsNullOrEmpty(_pathSession))
                {
                    _session_tokens.AddRange(_embeds);
                    _n_session_consumed = _session_tokens.Count;
                }
            }

            // Clear the embeds list now that its tokens have been processed.
            _embeds.Clear();

            // If all input tokens have been consumed and we're not waiting for new input...
            if (_embed_inps.Count <= _consumedTokensCount && !args.WaitForInput)
            {
                // Optionally save the session on the first sample for faster future prompt loading.
                if (!string.IsNullOrEmpty(_pathSession) && args.NeedToSaveSession)
                {
                    args.NeedToSaveSession = false;
                    SaveSessionFile(_pathSession);
                }

                // Sample the next token using the sampling pipeline.
                // Note: 'batch.TokenCount - 1' represents the current position in the batch.
                var id = inferenceParams.SamplingPipeline.Sample(Context.NativeHandle, batch.TokenCount - 1);

                // Add the sampled token to the history of recently generated tokens.
                _last_n_tokens.Enqueue(id);
                // Add the sampled token to the embeds list to be processed.
                _embeds.Add(id);

                // Decrement the remaining token counter.
                args.RemainedTokens--;
                // Signal that a token was generated.
                args.ReturnValue = true;
            }
            else
            {
                // If there are pending input tokens that have not been processed,
                // transfer them to the embeds list until the batch is full.
                while (_embed_inps.Count > _consumedTokensCount)
                {
                    _embeds.Add(_embed_inps[_consumedTokensCount]);
                    _last_n_tokens.Enqueue(_embed_inps[_consumedTokensCount]);
                    _consumedTokensCount++;
                    // Stop adding tokens once the current batch size limit is reached.
                    if (_embeds.Count >= Context.BatchSize)
                    {
                        break;
                    }
                }
            }

            // End of inference processing.
            return;
        }

        /// <summary>
        /// The descriptor of the state of the instruct executor.
        /// This state object extends the base state with instruct-specific data.
        /// </summary>
        public class InstructExecutorState : ExecutorBaseState
        {
            /// <summary>
            /// Whether the executor is running for the first time (processing the initial prompt).
            /// </summary>
            [JsonPropertyName("is_prompt_run")]
            public bool IsPromptRun { get; set; }

            /// <summary>
            /// Tokenized instruction prefix tokens.
            /// </summary>
            [JsonPropertyName("inp_pfx")]
            public LLamaToken[] InputPrefixTokens { get; set; }

            /// <summary>
            /// Tokenized instruction suffix tokens.
            /// </summary>
            [JsonPropertyName("inp_sfx")]
            public LLamaToken[] InputSuffixTokens { get; set; }
        }
    }
}
