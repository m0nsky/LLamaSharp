using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LLama.Grammars.Generator;

public sealed class GrammarGenerator
{
    // Common rules (Dictionary: key = type, value = rule string without new line)
    // This will include things like int, float, string, etc.
    private Dictionary<Type, GrammarGeneratorRule> commonRules = new();
    
    // Custom rules (Dictionary: key = name string, value = rule string without new line)
    // This will include the fields we come across per object and their subjects
    private Dictionary<string, string> customRules = new();
    
    // Settings
    public GrammarGeneratorSettings Settings = new();
    
    // Initialize Common Rules
    public void RegisterCommonRules()
    {
        // Add the common rules
        commonRules.Add(typeof(string), new GrammarGeneratorRule(
            "string", 
            $"{Settings.GbnfQuote}{Settings.JsonQuote}{Settings.GbnfQuote}{Settings.StringRule}{Settings.GbnfQuote}{Settings.JsonQuote}{Settings.GbnfQuote}")
        );
        
        commonRules.Add(typeof(bool), new GrammarGeneratorRule(
            "boolean", 
            $"{Settings.GbnfQuote}true{Settings.GbnfQuote}|{Settings.GbnfQuote}false{Settings.GbnfQuote}")
        );
        
        commonRules.Add(typeof(int), new GrammarGeneratorRule(
            "int", 
            $"[-]?[0-9]+")
        );
        
        commonRules.Add(typeof(uint), new GrammarGeneratorRule(
            "uint", 
            $"[0-9]+")
        );
        
        commonRules.Add(typeof(float), new GrammarGeneratorRule(
            "float", 
            $"[-]?[0-9]+{Settings.GbnfQuote}.{Settings.GbnfQuote}?[0-9]*([eE][-+]?[0-9]+)?[fF]?")
        );
        
        commonRules.Add(typeof(double), new GrammarGeneratorRule(
            "double", 
            $"[-]?[0-9]+{Settings.GbnfQuote}.{Settings.GbnfQuote}?[0-9]*([eE][-+]?[0-9]+)?[dD]?")
        );
    }
    
    public GrammarGenerator(GrammarGeneratorSettings? settings = null)
    {
        // Register common rules
        RegisterCommonRules();
        
        // Set the settings
        if (settings != null)
            Settings = settings;
    }
    
    // Get rule name
    public string GetRuleName(MemberInfo member)
    {
        // Get the name of the rule
        string ruleName = $"{member.DeclaringType?.Namespace}-{member.DeclaringType?.Name}-{member.Name}";
        
        // Replace dots with dashes (underscores won't work and will throw an exception)
        ruleName = ruleName.Replace(".", "-");
        
        return ruleName;
    }

    public void ProcessNode(MemberInfo member, string nodeName = "")
    {

        
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
            // Get the value of the member in this instance (for now, we can't get the default value if we don't have an instance)
            object? instanceValue = memberInfo.MemberType == MemberTypes.Field
                ? ((FieldInfo)memberInfo).GetValue(obj)
                : ((PropertyInfo)memberInfo).GetValue(obj);
            
            gbnf.Append(GenerateRuleForMember(memberInfo, instanceValue));
            
            // Add the member name to the root rule
            objectRule += $"\"{Settings.JsonQuote}{memberInfo.Name}{Settings.JsonQuote}:\"{GetRuleName(memberInfo)}";
        
            // Add a comma if it's not the last member
            if (memberInfo != memberInfos.Last())
            {
                // Add a comma
                objectRule += "\",\"";
            }
        }
        
        if (nodeName == "")
        {
            customRules.Add("root", type.Name);
        }
        else
        {
            customRules.Add(nodeName, type.Name);
        }
    }
    
    // Converts an object to a GBNF grammar
    public string Generate<T>(T obj)
    {
        // Create a string builder to store the GBNF rules
        StringBuilder gbnf = new StringBuilder();
        
        // Get the type of the object
        Type type = obj.GetType();
        
        // Add root rule
        customRules.Add("root", type.Name);
        
        // Start recursively processing the object
        ProcessNode(type);
        
        // Add the common rules
        foreach (var knownRule in commonRules)
        {
            gbnf.Append(knownRule.Value);
        }
        
        // Add the custom rules
        foreach (var customRule in customRules)
        {
            gbnf.Append(customRule.Value);
        }

        // Return the GBNF rules as a string
        return gbnf.ToString();
    }
}
