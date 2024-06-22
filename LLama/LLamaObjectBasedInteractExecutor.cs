using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama.Abstractions;
using LLama.Common;
using JsonRepairSharp;
using LLama.Grammars;
using Newtonsoft.Json;

namespace LLama;

public class ObjectBasedExecutor : InteractiveExecutor
{
    // Constructor
    public ObjectBasedExecutor(LLamaContext context) : base(context) { }
    
    // Infer Object Async
    public async IAsyncEnumerable<TOut?> InferObjectAsync<TIn, TOut>(TIn inputObj, TOut outputObj, IInferenceParams? inferenceParams = null, CancellationToken token = default)
    {
        // Instantiate a new GBNFGrammarGenerator
        GBNFGrammarGenerator grammarGenerator = new();
        
        // Generate grammar for the output object
        var gbnf = grammarGenerator.GenerateFromObject(outputObj);
        
        // Clear the console
        Console.Clear();
        
        // Log the generated grammar
        Console.WriteLine(gbnf);
        
        // Wait for readline
        Console.ReadLine();
        
        // Parse the grammar
        var grammar = Grammar.Parse(gbnf, "root");
        
        // Create a grammar instance
        using var grammarInstance = grammar.CreateInstance();
        
        // Add the grammar to the inference params
        if (inferenceParams == null)
        {
            // Create a new inference params object
            inferenceParams = new InferenceParams();
        }
            
        // Set the grammar instance
        inferenceParams.Grammar = grammarInstance;
        
        StringBuilder fullResponse = new();
        
        // Enable LLM mode, see:
        // https://github.com/thijse/JsonRepairSharp/commit/f3794683ff2cc293ad019a25c37f9d2e39d3cd80
        JsonRepair.Context = JsonRepair.InputType.LLM;
        
        await foreach (var response in InferAsync(JsonConvert.SerializeObject(inputObj), inferenceParams, token))
        {
            // Append the response to the full response
            fullResponse.Append(response);
            
            try
            {
                // // Clear the console
                // Console.Clear();
                //
                // // Log the raw response
                // Console.WriteLine(fullResponse);
                
                // Now, we'll try to repair and parse the response into JSON thus far, and use the repaired JSON to populate the output object
                JsonConvert.PopulateObject(JsonRepair.RepairJson(fullResponse.ToString()), outputObj);
            }
            catch
            {
                // If there's an error, we'll continue the inference, and try again
                continue;
            }

            // Return the output object
            yield return outputObj;
        }
    }
}