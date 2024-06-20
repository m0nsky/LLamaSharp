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
    public ObjectBasedExecutor(LLamaContext context) : base(context)
    {
    }
    
    // InferAsync override (but call base InferAsync), no modifications for now
    public override async IAsyncEnumerable<string> InferAsync(string text, IInferenceParams? inferenceParams = null, CancellationToken token = default)
    {
        //text += 
        
        // Log the prompt (in purple)
        Console.WriteLine($"\u001b[35m{text}\u001b[0m");
        
        await foreach (var response in base.InferAsync(text, inferenceParams, token))
        {
            yield return response;
        }
    }
    
    // Now, we do a custom InferObjectAsync.
    // 1. This method accepts an object (generic), and first transforms it to a json string using JsonConvert.SerializeObject.
    // 2. Then, it calls InferAsync with the json string.
    // 3. Every loop, the result is appended to a StringBuilder, and we try to parse the latest state using JsonRepair
    // 4. If the parsing is successful, we yield return the parsed object.
    // 5. If the parsing fails, we continue the loop.
    public async IAsyncEnumerable<T?> InferObjectAsync<T>(T inputObj, T outputObj, IInferenceParams? inferenceParams = null, CancellationToken token = default)
    {
        // Generate grammar for the output object
        var gbnf = GBNFGrammarGenerator.GenerateFromClass(outputObj.GetType());
        
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
        
        string json = JsonConvert.SerializeObject(inputObj);
        
        // Log the json
        //Console.WriteLine(json);
        
        StringBuilder sb = new();
        
        await foreach (var response in InferAsync(json, inferenceParams, token))
        {
            sb.Append(response);
            string repaired = "";

            T? parsed = default;
            
            // Log sb.ToString()
            //Console.WriteLine(sb.ToString());
            
            try
            {
                // Try to repair the json
                repaired = JsonRepair.RepairJson(sb.ToString());
                //repaired = sb.ToString();
                
                // Parse the repaired json
                parsed = JsonConvert.DeserializeObject<T>(repaired);
                
            }
            catch
            {
                continue;
            }

            // If the parsed object isn't null, yield return it
            if (parsed != null)
            {
                yield return parsed;
            }
        }
    }
}