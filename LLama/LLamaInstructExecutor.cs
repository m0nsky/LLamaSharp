using LLama.Abstractions;
using LLama.Common;
using LLama.Native;
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
    /// The LLama executor for instruct mode.
    /// In instruct mode the executor guides the model's responses using a structured template.
    /// It uses an instruction prefix and suffix to delineate the instruction and the expected response.
    /// Like InteractiveExecutor, it leverages the base class for context management, session handling,
    /// and asynchronous streaming inference via InferAsync.
    /// </summary>
    public class InstructExecutor
        : StatefulExecutorBase
    {
        // Indicates whether the executor is processing the initial prompt.
        private bool _is_prompt_run = true;
        // Holds the instruction prefix string (e.g., "\n\n### Instruction:\n\n") to be added before user input.
        private readonly string _instructionPrefix;
        // Tokenized version of the instruction prefix.
        private LLamaToken[] _inp_pfx;
        // Tokenized version of the instruction suffix (e.g., "\n\n### Response:\n\n") to be added after user input.
        private LLamaToken[] _inp_sfx;

        // Optional reference to a sampling pipeline (configured externally if needed).
        private ISamplingPipeline? _pipeline;

        /// <summary>
        /// Constructs an InstructExecutor with the given LLamaContext, instruction template, and optional logger.
        /// The instruction prefix and suffix are tokenized immediately for later use in input preprocessing.
        /// </summary>
        /// <param name="context">The LLama context used for tokenization, inference, and model properties.</param>
        /// <param name="instructionPrefix">
        /// The instruction prefix string that is prepended to the user prompt.
        /// Defaults to "\n\n### Instruction:\n\n".
        /// </param>
        /// <param name="instructionSuffix">
        /// The instruction suffix string that is appended after the user prompt.
        /// Defaults to "\n\n### Response:\n\n".
        /// </param>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public InstructExecutor(LLamaContext context,
                                string instructionPrefix = "\n\n### Instruction:\n\n",
                                string instructionSuffix = "\n\n### Response:\n\n",
                                ILogger? logger = null)
            : base(context, logger)
        {
            // Tokenize the instruction prefix with a beginning-of-sequence token.
            _inp_pfx = Context.Tokenize(instructionPrefix, true, true);
            // Tokenize the instruction suffix without adding a BOS token.
            _inp_sfx = Context.Tokenize(instructionSuffix, false, true);
            _instructionPrefix = instructionPrefix;
        }

        /// <inheritdoc />
        public override ExecutorBaseState GetStateData()
        {
            // Package the current state into an InstructExecutorState.
            // This includes both the shared state (from the base class) and instruct-specific tokens.
            InstructExecutorState state = new()
            {
                ConsumedSessionCount = _n_session_consumed,
                EmbedInps = _embed_inps.ToArray(),
                IsPromptRun = _is_prompt_run,
                ConsumedTokensCount = _consumedTokensCount,
                Embeds = _embeds.ToArray(),
                LastTokens = _last_n_tokens.ToArray(),
                // Instruct-specific state: tokenized instruction prefix.
                InputPrefixTokens = _inp_pfx,
                // Instruct-specific state: tokenized instruction suffix.
                InputSuffixTokens = _inp_sfx,
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
            if(data is InstructExecutorState state)
            {
                // Restore shared state from the base class.
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
                throw new ArgumentException("Invalid state data type.");
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override async Task SaveState(string filename)
        {
            var state = (InstructExecutorState)GetStateData();
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                await JsonSerializer.SerializeAsync(fs, state);
            }
        }
        /// <inheritdoc />
        public override async Task LoadState(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                var state = await JsonSerializer.DeserializeAsync<InstructExecutorState>(fs);
                await LoadState(state);
            }
        }

        /// <inheritdoc />
        protected override Task<bool> GetLoopCondition(InferStateArgs args)
        {
            // Continue generation as long as there are remaining tokens,
            // or if the initial prompt run has not yet finished.
            return Task.FromResult(args.RemainedTokens != 0 || _is_prompt_run);
        }

        /// <inheritdoc />
        protected override Task PreprocessInputs(string? text, InferStateArgs args)
        {
            // Ensure the antiprompt list exists.
            args.Antiprompts ??= new List<string>();
            // Add the instruction prefix to the antiprompt list if it's not already present.
            // This helps signal to the model where the instruction begins.
            if (!args.Antiprompts.Contains(_instructionPrefix))
                args.Antiprompts.Add(_instructionPrefix);

            if (_is_prompt_run)
            {
                // For the first (initial) prompt run, the prompt must be provided.
                if (text == null) throw new ArgumentException("Prompt cannot be null to trigger continuation if a prompt has not been provided previously.");
                // Tokenize the prompt with a BOS token.
                _embed_inps = Context.Tokenize(text, true, true).ToList();
            }
            else
            {
                // For continuation requests, mark that all previous input tokens have been processed.
                _consumedTokensCount = _embed_inps.Count;

                // If new text is provided, process it:
                if (text != null)
                {
                    // Ensure the prompt ends with a newline.
                    if (!text.EndsWith("\n"))
                    {
                        text += "\n";
                    }
                    // Append the tokenized instruction prefix before the new input.
                    _embed_inps.AddRange(_inp_pfx);

                    // Tokenize the new input text (without adding a BOS token).
                    var line_inp = Context.Tokenize(text, false, true);
                    _embed_inps.AddRange(line_inp);

                    // Append the tokenized instruction suffix after the new input.
                    _embed_inps.AddRange(_inp_sfx);

                    // Adjust the remaining token count based on the user input.
                    args.RemainedTokens -= line_inp.Length;
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task<(bool, IReadOnlyList<string>)> PostProcess(IInferenceParams inferenceParams, InferStateArgs args)
        {
            if (_embed_inps.Count <= _consumedTokensCount)
            {
                // Check if the generated tokens end with any of the antiprompts (including the instruction prefix).
                if (_last_n_tokens.TokensEndsWithAnyString(args.Antiprompts, Context.NativeHandle.ModelHandle, Context.Encoding))
                {
                    args.WaitForInput = true;
                    return (true, Array.Empty<string>());
                }

                // If some tokens have been processed and we are waiting for user input,
                // emit a prompt indicator (here, "\n> ").
                if (_pastTokensCount > 0 && args.WaitForInput)
                {
                    return (true, new[] { "\n> " });
                }
            }

            // If the last generated token is EOS, signal to wait for further input.
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
            return (false, Array.Empty<string>());
        }

        /// <inheritdoc />
        protected override async Task InferInternal(IInferenceParams inferenceParams, InferStateArgs args)
        {
            var batch = new LLamaBatch();

            if (_embeds.Count > 0)
            {
                // Mark the end of the initial prompt run.
                _is_prompt_run = false;
                // For instruct mode, if the current tokens plus past tokens exceed the context window,
                // always keep the full input token size (i.e. the entire prompt with instruction template).
                if (_pastTokensCount + _embeds.Count > Context.ContextSize)
                {
                    var tokensToKeep = _embed_inps.Count;
                    HandleRunOutOfContext(tokensToKeep);
                }

                // Attempt to reuse a matching prefix from a saved session, leveraging base class functionality.
                TryReuseMatchingPrefix();

                // Decode the current tokens in _embeds.
                var (result, _, pastTokensCount) = await Context.DecodeAsync(_embeds, LLamaSeqId.Zero, batch, _pastTokensCount);
                _pastTokensCount = pastTokensCount;

                if (result != DecodeResult.Ok)
                    throw new LLamaDecodeError(result);

                // If using a session file, update the session tokens history.
                if (_embeds.Count > 0 && !string.IsNullOrEmpty(_pathSession))
                {
                    _session_tokens.AddRange(_embeds);
                    _n_session_consumed = _session_tokens.Count;
                }
            }

            // Clear _embeds after processing.
            _embeds.Clear();

            if (_embed_inps.Count <= _consumedTokensCount && !args.WaitForInput)
            {
                // Optionally save the session to disk on the first sample to accelerate future prompt loading.
                if (!string.IsNullOrEmpty(_pathSession) && args.NeedToSaveSession)
                {
                    args.NeedToSaveSession = false;
                    SaveSessionFile(_pathSession);
                }

                // Sample the next token using the sampling pipeline.
                var id = inferenceParams.SamplingPipeline.Sample(Context.NativeHandle, batch.TokenCount - 1);

                _last_n_tokens.Enqueue(id);
                _embeds.Add(id);

                args.RemainedTokens--;
                args.ReturnValue = true;
            }
            else
            {
                // If there are pending input tokens, move them into the current batch until the batch is full.
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
        /// The descriptor of the state of the instruct executor.
        /// This state object extends the base state with instruct-specific data
        /// (namely, the tokenized instruction prefix and suffix).
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
