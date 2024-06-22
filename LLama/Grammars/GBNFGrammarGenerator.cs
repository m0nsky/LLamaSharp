using System;
using System.Collections.Generic;
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
            
            // For lists, we will use the array rule. It looks like this:
            // array  ::=
            // "[" (
            //     value
            //         ("," value)*
            // )? "]"
            gbnf.Append("array::=\"[\"(value(\",\"value)*)?\"]\"\n");
            
            // Now, we create the rule for "value", this can be any of the types we defined above
            gbnf.Append("value::=string|int|uint|float|double|boolean|array\n");

            // Create the root rule
            string rootRule = $"root::={type.Name}\n{type.Name}::=\"{{\"";
            
            // Process fields
            
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
            
            // Process properties
            
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
            
            // // Clear the console
            // Console.Clear();
            //
            // // Log the generated GBNF rules
            // Console.WriteLine(gbnf.ToString());
            //
            // // Wait for the user to press a key
            // Console.ReadLine();

            // Return the GBNF rules as a string
            return gbnf.ToString();
        }
        
        private static string GenerateRuleForProperty(PropertyInfo property)
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
            else if (propertyType.IsArray)
            {
                // Generate the rule for the array field
                rule = $"{propertyType.Name}::=array\n";
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
                // The property is not any of the known types, so we will generate a rule for it's class (recursive)
                rule = $"{property.Name}::={propertyType.Name}\n";
                
                // Generate the rules for the class
                string classRules = GenerateFromClass(propertyType);
                
                // Append the class rules to the generated rule
                rule += classRules;
            }

            // Return the generated rule string
            return rule;
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
            else if (fieldType.IsArray)
            {
                // Generate the rule for the array field
                rule = $"{field.Name}::=array\n";
            }
            else
            {
                // The field is not any of the known types, so we will generate a rule for it's class (recursive)
                rule = $"{field.Name}::={fieldType.Name}\n";
                
                // Generate the rules for the class
                string classRules = GenerateFromClass(fieldType);
                
                // Append the class rules to the generated rule
                rule += classRules;
            }

            // Return the generated rule string
            return rule;
        }
    }
}

