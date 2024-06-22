using System.Reflection;
using JsonRepairSharp;
using LLama.Common;
using LLama.Grammars;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LLama.Examples.Examples
{
    public class GrammarGeneratorResponse
    {
        public static async Task Run()
        {
            string modelPath = UserSettings.GetModelPath();

            var parameters = new ModelParams(modelPath)
            {
                Seed = 1337,
                GpuLayerCount = 32,
                Threads = 6,
                BatchThreads = 6,
                BatchSize = 2048
            };
            
            using var model = await LLamaWeights.LoadFromFileAsync(parameters);
            
            // Create a new object based executor
            using var context = model.CreateContext(parameters);
            var executor = new ObjectBasedExecutor(context);
            
            // Create a sample input object
            ExampleInputObject inputObject = new ExampleInputObject();
            inputObject.Question = "Do you like pizza?";
            
            // Create a sample output object
            ExampleOutputObject outputObject = new ExampleOutputObject();
            
            // Create inference params
            InferenceParams inferenceParams = new InferenceParams()
            {
                Temperature = 0.9f,
                AntiPrompts = new List<string> { "\n\n" }
            };
            
            // Infer the object
            await foreach (var obj in executor.InferObjectAsync(inputObject, outputObject, inferenceParams))
            {
                // // Clear the console
                // Console.Clear();
                //
                // // Set color to white
                // Console.ForegroundColor = ConsoleColor.White;
                //
                // // Write the prompt to the console
                // Console.WriteLine("[InputObject]");
                //
                // // Convert the input object to json and write it to the console
                // Console.WriteLine(JsonConvert.SerializeObject(inputObject, Formatting.Indented));
                //
                // // Newline
                // Console.WriteLine();
                //
                // // Set color to yellow
                // Console.ForegroundColor = ConsoleColor.Yellow;
                //
                // // Write the prompt to the console
                // Console.WriteLine("[OutputObject]");
                //
                // var settings = new JsonSerializerSettings();
                // settings.Converters.Add(new StringEnumConverter());
                // string json = JsonConvert.SerializeObject(obj, Formatting.Indented, settings);
                // Console.WriteLine(json);
            }
        }
    }
}