using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LLama.Grammars
{
    public sealed class GBNFGrammarGenerator
    {
        // Converts a class to a GBNF grammar
        public static string GenerateFromClass(Type type)
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
            
            // Get the public fields of the class
            FieldInfo[] fields = type.GetFields();
            
            // Create a GBNF rule for each field
            foreach (FieldInfo field in fields)
            {
                // Generate the rule for the field
                string rule = GenerateRuleForField(field);
                
                // Append the rule to the GBNF rules
                gbnf.Append(rule);
            }

            // Create the root rule
            string rootRule = $"root::={type.Name}\n{type.Name}::=\"{{\"";
            
            // Add the field names to the root rule
            int fieldCount = fields.Length;
            
            for (int i = 0; i < fieldCount; i++)
            {
                // Get the field info
                FieldInfo field = fields[i];
                
                // Add the field name to the root rule
                rootRule += $"\"\\\"{field.Name}\\\":\"{field.Name}";
                
                // Add a comma if it's not the last field
                if (i < fieldCount - 1)
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

        private static string GenerateRuleForField(FieldInfo field)
        {
            // Create an empty string that will hold the generated rule
            string rule = "";
            
            // Get the field type
            Type fieldType = field.FieldType;
            
            // We will generate a rule based on the field type
            if (fieldType == typeof(string))
            {
                rule = $"{field.Name}::=string\n";
            }
            else if (fieldType == typeof(int))
            {
                rule = $"{field.Name}::=int\n";
            }
            else if (fieldType == typeof(uint))
            {
                rule = $"{field.Name}::=uint\n";
            }
            else if (fieldType == typeof(float))
            {
                rule = $"{field.Name}::=float\n";
            }
            else if (fieldType == typeof(double))
            {
                rule = $"{field.Name}::=double\n";
            }
            else if (fieldType == typeof(bool))
            {
                rule = $"{field.Name}::=boolean\n";
            }
            else if (fieldType.IsEnum)
            {
                // Get the enum values
                var enumValues = Enum.GetNames(fieldType);
                
                // Generate the rule for the enum field
                rule = $"{field.Name}::=" + string.Join("|", enumValues.Select(e => $"\"\\\"{e}\\\"\"")) + "\n";
            }
            else
            {
                // Throw an exception if the field type is not supported
                throw new NotSupportedException($"The field type '{fieldType.Name}' is not supported.");
            }

            // Return the generated rule string
            return rule;
        }
    }
}

