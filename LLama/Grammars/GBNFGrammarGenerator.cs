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
        public static string GenerateFromClass(Type type, bool isRoot = true)
        {
            // Create a string builder to store the GBNF rules
            StringBuilder gbnf = new StringBuilder();

            if (isRoot)
            {
                // Add the common rules
                gbnf.Append("string::=\"\\\"\"([^\\\"]*)\"\\\"\"\n");
                gbnf.Append("boolean::=\"true\"|\"false\"\n");
                gbnf.Append("int::=[-]?[0-9]+\n");
                gbnf.Append("uint::=[0-9]+\n");
                gbnf.Append("float::=[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[fF]?\n");
                gbnf.Append("double::=[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[dD]?\n");
                gbnf.Append("array::=\"[\"(value(\",\"value)*)?\"]\"\n");

                // Now, we create the rule for "value", this can be any of the types we defined above
                gbnf.Append("value::=string|int|uint|float|double|boolean|array\n");
            }

            string rootRule = "";
            
            // Create the root rule
            if (isRoot)
            {
                // We use "root" as the type name
                rootRule += $"root::={type.Name}\n{type.Name}::=\"{{\"";
            }
            else
            {
                // We use the type name as the root rule
                rootRule += $"{type.Name}::=\"{{\"";
            }

            
            // Create a list to store all the members of the class
            List<MemberInfo> memberInfos = new List<MemberInfo>();
            
            // We don't just simply get all the members below, because it destroys the original order of the members
            // The order of members can be important in LLM output (CoT/ReAct), so we get fields and properties separately
            
            // Get all fields
            memberInfos.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            
            // Get all properties
            memberInfos.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));

            // Create a GBNF rule for each member
            foreach (MemberInfo memberInfo in memberInfos)
            {
                gbnf.Append(GenerateRuleForMember(memberInfo));
                
                rootRule += $"\"\\\"{memberInfo.Name}\\\":\"{memberInfo.Name}";
            
                // Add a comma if it's not the last member
                if (memberInfo != memberInfos.Last())
                {
                    rootRule += "\",\"";
                }
            }

            // Close the root rule
            rootRule += "\"}\"\n";

            // Combine the root rule and the property rules
            gbnf.Insert(0, rootRule);
            
            // Clear console
            Console.Clear();
            
            // Log the generated GBNF rules
            Console.WriteLine(gbnf.ToString());

            // Return the GBNF rules as a string
            return gbnf.ToString();
        }

        private static string GenerateRuleForMember(MemberInfo member)
        {
            // Get the type of the member (field or property)
            Type memberType = member.MemberType == MemberTypes.Field
                ? ((FieldInfo)member).FieldType
                : ((PropertyInfo)member).PropertyType;

            // Generate rule based on the member type
            return memberType switch
            {
                Type _ when memberType == typeof(string) => $"{member.Name}::=string\n",
                Type _ when memberType == typeof(int) => $"{member.Name}::=int\n",
                Type _ when memberType == typeof(uint) => $"{member.Name}::=uint\n",
                Type _ when memberType == typeof(float) => $"{member.Name}::=float\n",
                Type _ when memberType == typeof(double) => $"{member.Name}::=double\n",
                Type _ when memberType == typeof(bool) => $"{member.Name}::=boolean\n",
                Type _ when memberType.IsEnum => $"{member.Name}::=" + string.Join("|", Enum.GetNames(memberType).Select(e => $"\"\\\"{e}\\\"\"")) + "\n",
                Type _ when memberType.IsArray => $"{member.Name}::=array\n",
                _ => GenerateComplexTypeRule(member, memberType)
            };
        }

        private static string GenerateComplexTypeRule(MemberInfo member, Type memberType)
        {
            // The member is not any of the known types, so we will generate a rule for its class (recursive)
            string rule = $"{member.Name}::={memberType.Name}\n";
            
            // Generate the rules for the class
            string classRules = GenerateFromClass(memberType, false);

            // Return the combined rules
            return rule + classRules;
        }
    }
}

