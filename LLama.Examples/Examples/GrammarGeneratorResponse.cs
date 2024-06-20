using System.Reflection;
using JsonRepairSharp;
using LLama.Common;
using LLama.Grammars;
using Newtonsoft.Json;

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
                GpuLayerCount = 16
            };
            
            using var model = await LLamaWeights.LoadFromFileAsync(parameters);
            
            // Create a new object based executor
            using var context = model.CreateContext(parameters);
            var executor = new ObjectBasedExecutor(context);
            
            // Create a sample input object
            ExampleObject inputObject = new ExampleObject();
            inputObject.Message = "Do you like pizza?";
            inputObject.Mood = "happy";
            
            // Create a sample output object
            ExampleObject outputObject = new ExampleObject();
            
            // Infer the object
            await foreach (var obj in executor.InferObjectAsync(inputObject, outputObject))
            {
                // Clear the console
                Console.Clear();
                
                // Writeline test
                Console.WriteLine("Inferred object:");
                
                // Convert the object to json and write it to the console
                Console.WriteLine(JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
        }
    }
}