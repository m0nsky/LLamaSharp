using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LLama.Grammars
{
    public sealed class GBNFGrammarGenerator
    {
        // Known rules (dictionary: key = type, value = rule)
        private readonly Dictionary<Type, GBNFGrammarRule> _knownRules = new();
        
        // Initialize common rules
        public GBNFGrammarGenerator()
        {
            // Add the common rules
            _knownRules.Add(typeof(string), new GBNFGrammarRule(
                "string", 
                "\"\\\"\"([^\\\"]*)\"\\\"\"\n")
            );
            
            _knownRules.Add(typeof(bool), new GBNFGrammarRule(
                "boolean", 
                "\"true\"|\"false\"\n")
            );
            
            _knownRules.Add(typeof(int), new GBNFGrammarRule(
                "int", 
                "[-]?[0-9]+\n")
            );
            
            _knownRules.Add(typeof(uint), new GBNFGrammarRule(
                "uint", 
                "[0-9]+\n")
            );
            
            _knownRules.Add(typeof(float), new GBNFGrammarRule(
                "float", 
                "[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[fF]?\n")
            );
            
            _knownRules.Add(typeof(double), new GBNFGrammarRule(
                "double", 
                "[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[dD]?\n")
            );
            
            _knownRules.Add(typeof(Array), new GBNFGrammarRule(
                "array", 
                "\"[\"(value(\",\"value)*)?\"]\"\n")
            );
        }
        
        // Converts an object to a GBNF grammar
        public string GenerateFromObject(object obj, bool isRoot = true)
        {
            // Create a string builder to store the GBNF rules
            StringBuilder gbnf = new StringBuilder();
            
            // If this is the root node, we will add the common rules
            if (isRoot)
            {
                // Add the common rules
                foreach (var knownRule in _knownRules)
                {
                    gbnf.Append(knownRule.Value);
                }
                
                // Value rule for "generic" arrays (this will be removed in the future, because we only support arrays of specific types)
                gbnf.Append("value::=string|int|uint|float|double|boolean|array\n");
            }
            
            // Get the type of the object
            Type type = obj.GetType();
            
            string objectRule = "";
            
            // If this is the root node, we will add the root rule
            if (isRoot)
                objectRule += $"root::={type.Name}\n";
            
            // We use the type name as the root rule
            objectRule += $"{type.Name}::=\"{{\"";
            
            // Create a list to store all the members of the class
            List<MemberInfo> memberInfos = new List<MemberInfo>();
            
            // Note: We don't just simply get all the members below, because it destroys the original order of the members
            // The order of members can be important in LLM output (CoT/ReAct), so we get fields and properties separately
            
            // Get all fields
            memberInfos.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            
            // Get all properties
            memberInfos.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
            
            // Create a GBNF rule for each member
            foreach (MemberInfo memberInfo in memberInfos)
            {
                // Get the value of the member
                object? defaultValue = memberInfo.MemberType == MemberTypes.Field
                    ? ((FieldInfo)memberInfo).GetValue(obj)
                    : ((PropertyInfo)memberInfo).GetValue(obj);
                
                gbnf.Append(GenerateRuleForMember(memberInfo, defaultValue));
                
                // Add the member name to the root rule
                objectRule += $"\"\\\"{memberInfo.Name}\\\":\"{memberInfo.Name}";
            
                // Add a comma if it's not the last member
                if (memberInfo != memberInfos.Last())
                {
                    // Add a comma
                    objectRule += "\",\"";
                }
            }

            // Close the root rule
            objectRule += "\"}\"\n";

            // Combine the root rule and the property rules
            gbnf.Insert(0, objectRule);
            
            // Clear console
            Console.Clear();
            
            // Log the generated GBNF rules
            Console.WriteLine(gbnf.ToString());

            // Return the GBNF rules as a string
            return gbnf.ToString();
            
            // // Generate the GBNF grammar from the class
            // return GenerateFromType(type);
        }
        
        // // Converts a class to a GBNF grammar
        // public string GenerateFromType(Type type, bool isRoot = true)
        // {
        //
        //
        // }

        private string GenerateRuleForMember(MemberInfo member, object? defaultValue)
        {
            // Get the type of the member (field or property)
            Type memberType = member.MemberType == MemberTypes.Field
                ? ((FieldInfo)member).FieldType
                : ((PropertyInfo)member).PropertyType;
            
            // Custom rules for known types
            if (memberType == typeof(string))
            {
                // If the default value is not null (and not empty), we will generate a rule for it instead of allowing the LLM to generate a response
                if (defaultValue != null && (string)defaultValue != "")
                        return $"{member.Name}::=\"\\\"{defaultValue}\\\"\"\n";
                
                // If there is no default value, we will generate a generic rule for strings and allow the LLM to generate a response
                return $"{member.Name}::=string\n";
            }
            else if (memberType == typeof(int))
            {
                return $"{member.Name}::=int\n";
            }
            else if (memberType == typeof(uint))
            {
                return $"{member.Name}::=uint\n";
            }
            else if (memberType == typeof(float))
            {
                return $"{member.Name}::=float\n";
            }
            else if (memberType == typeof(double))
            {
                return $"{member.Name}::=double\n";
            }
            else if (memberType == typeof(bool))
            {
                return $"{member.Name}::=boolean\n";
            }
            else if (memberType.IsEnum)
            {
                return $"{member.Name}::=" + string.Join("|", Enum.GetNames(memberType).Select(e => $"\"\\\"{e}\\\"\"")) + "\n";
            }
            else if (memberType.IsArray)
            {
                return $"{member.Name}::=array\n";
            }
            else
            {
                // The member is not any of the known types, so we will generate a rule for its class (recursive)
                return $"{member.Name}::={memberType.Name}\n";
            }
        }

        // private string GenerateCustomTypeRule(MemberInfo member, Type memberType)
        // {
        //     // The member is not any of the known types, so we will generate a rule for its class (recursive)
        //     string rule = $"{member.Name}::={memberType.Name}\n";
        //     
        //     // Generate the rules for the class
        //     string classRules = GenerateFromType(memberType, false);
        //
        //     // Return the combined rules
        //     return rule + classRules;
        // }
    }
}

