using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LLama.Grammars
{
    internal sealed class GBNFGrammarGenerator
    {
        // Converts a class to a GBNF grammar
        public string GenerateFromClass(Type type)
        {
            // Create a string builder to store the GBNF rules
            StringBuilder gbnf = new StringBuilder();

            // Add the common rules
            gbnf.Append("string::=\"\\\"\"([^\\\"]*)\"\\\"\"\n");
            gbnf.Append("boolean::=\"true\"|\"false\"\n");
            gbnf.Append("int::=[-]?[0-9]+\n");
            gbnf.Append("uint::=[0-9]+\n");
            gbnf.Append("float::=[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[fF]?\n");
            gbnf.Append("double::=[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[dD]?\n");

            // Get the public properties of the class
            PropertyInfo[] properties = type.GetProperties();

            // Create a GBNF rule for each property
            foreach (PropertyInfo property in properties)
            {
                // Generate the rule for the property
                string rule = GenerateRuleForProperty(property);
                
                // Append the rule to the GBNF rules
                gbnf.Append(rule);
            }

            // Create the root rule
            string rootRule = $"root::={type.Name}\n{type.Name}::=\"{{\"";
            
            // Add the property names to the root rule
            int propertyCount = properties.Length;
            
            for (int i = 0; i < propertyCount; i++)
            {
                // Get the property info
                PropertyInfo property = properties[i];
                
                // Add the property name to the root rule
                rootRule += $"\"\\\"{property.Name}\\\":\"{property.Name}";
                
                // Add a comma if it's not the last property
                if (i < propertyCount - 1)
                {
                    rootRule += "\",\"";
                }
            }
            
            // Close the root rule
            rootRule += "\"}\"\n";

            // Combine the root rule and the property rules
            gbnf.Insert(0, rootRule);

            // Return the GBNF rules as a string
            return gbnf.ToString();
        }

        // Generates a GBNF rule for a property
        private string GenerateRuleForProperty(PropertyInfo property)
        {
            // Create an empty string that will hold the generated rule
            string rule = "";
            
            // Get the property type
            Type propertyType = property.PropertyType;
            
            // We will generate a rule based on the property type
            if (propertyType == typeof(string))
            {
                rule = $"{property.Name}::=string\n";
            }
            else if (propertyType == typeof(int))
            {
                rule = $"{property.Name}::=int\n";
            }
            else if (propertyType == typeof(uint))
            {
                rule = $"{property.Name}::=uint\n";
            }
            else if (propertyType == typeof(float))
            {
                rule = $"{property.Name}::=float\n";
            }
            else if (propertyType == typeof(double))
            {
                rule = $"{property.Name}::=double\n";
            }
            else if (propertyType == typeof(bool))
            {
                rule = $"{property.Name}::=boolean\n";
            }
            else if (propertyType.IsEnum)
            {
                // Get the enum values
                var enumValues = Enum.GetNames(propertyType);
                
                // Generate the rule for the enum property
                rule = $"{property.Name}::=" + string.Join("|", enumValues.Select(e => $"\"\\\"{e}\\\"\"")) + "\n";
            }
            else
            {
                // Throw an exception if the property type is not supported
                throw new NotSupportedException($"The property type '{propertyType.Name}' is not supported.");
            }

            // Return the generated rule string
            return rule;
        }
    }
}

