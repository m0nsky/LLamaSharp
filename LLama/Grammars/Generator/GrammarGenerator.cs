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
            $"\"{Settings.JsonQuote}\"{Settings.StringRule}\"{Settings.JsonQuote}\"\n")
        );
        
        commonRules.Add(typeof(bool), new GrammarGeneratorRule(
            "boolean", 
            "\"true\"|\"false\"\n")
        );
        
        commonRules.Add(typeof(int), new GrammarGeneratorRule(
            "int", 
            "[-]?[0-9]+\n")
        );
        
        commonRules.Add(typeof(uint), new GrammarGeneratorRule(
            "uint", 
            "[0-9]+\n")
        );
        
        commonRules.Add(typeof(float), new GrammarGeneratorRule(
            "float", 
            "[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[fF]?\n")
        );
        
        commonRules.Add(typeof(double), new GrammarGeneratorRule(
            "double", 
            "[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[dD]?\n")
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
    
    // Converts an object to a GBNF grammar
    public string Generate<T>(T obj, bool isRoot = true)
    {
        // Check if we are dealing with an object or a class
        if (obj is object)
        {
            // Log and wait for readline
            Console.WriteLine("Generating rules for object");
            
            // Wait for readline
            Console.ReadLine();
        }
        else
        {
            // Log and wait for readline
            Console.WriteLine("Generating rules for class");
            
            // Wait for readline
            Console.ReadLine();
        }
        
        // Create a string builder to store the GBNF rules
        StringBuilder gbnf = new StringBuilder();
        
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
        
        // If this is the root node, we will add the common rules
        if (isRoot)
        {
            // Add the common rules
            foreach (var knownRule in commonRules)
            {
                gbnf.Append(knownRule.Value);
            }
        }

        // Close the root rule
        objectRule += "\"}\"\n";

        // Combine the root rule and the property rules
        gbnf.Insert(0, objectRule);

        // Return the GBNF rules as a string
        return gbnf.ToString();
    }
    
    public string GenerateFromType(Type type, bool isRoot = true)
    {
        // Create a string builder to store the GBNF rules
        StringBuilder gbnf = new StringBuilder();

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
            gbnf.Append(GenerateRuleForMember(memberInfo, null));
            
            rootRule += $"\"{Settings.JsonQuote}{memberInfo.Name}{Settings.JsonQuote}:\"{GetRuleName(memberInfo)}";
        
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

        // Return the GBNF rules as a string
        return gbnf.ToString();
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

    private string GenerateRuleForMember(MemberInfo member, object? defaultValue)
    {
        // Get the type of the member (field or property)
        Type memberType = member.MemberType == MemberTypes.Field
            ? ((FieldInfo)member).FieldType
            : ((PropertyInfo)member).PropertyType;
        
        // Get the name of the rule
        string ruleName = GetRuleName(member);
        
        // Custom rules for known types
        if (memberType == typeof(string))
        {
            if (Settings.DefaultValueMode == DefaultValueMode.Keep)
            {
                // If the default value is not null (and not empty), we will generate a rule that just keeps the default value
                if (defaultValue != null && (string) defaultValue != "")
                {
                    return $"{ruleName}::=\"{Settings.JsonQuote}{defaultValue}{Settings.JsonQuote}\"\n";
                }

            }
            else if (Settings.DefaultValueMode == DefaultValueMode.Discard)
            {
                // We will generate a rule that just discards the value, and allows the LLM to generate a string
                return $"{ruleName}::=string\n";
            }
            else if (Settings.DefaultValueMode == DefaultValueMode.Complete)
            {
                // If the default value isn't null, and isn't empty, we allow autocompletion of the default value
                if (defaultValue != null && (string) defaultValue != "")
                {
                    return $"{ruleName}::=\"{Settings.JsonQuote}{defaultValue}\" {Settings.StringRule} \"{Settings.JsonQuote}\"\n";
                }
            }
            
            return $"{ruleName}::=string\n";
        }
        else if (memberType == typeof(int))
        {
            // If the default value is not null, we will generate a rule for it instead of allowing the LLM to generate a response
            if (defaultValue != null)
                return $"{ruleName}::=\"{defaultValue}\"\n";
            
            return $"{ruleName}::=int\n";
        }
        else if (memberType == typeof(uint))
        {
            // If the default value is not null, we will generate a rule for it instead of allowing the LLM to generate a response
            if (defaultValue != null)
                return $"{ruleName}::=\"{defaultValue}\"\n";
            
            return $"{ruleName}::=uint\n";
        }
        else if (memberType == typeof(float))
        {
            // If the default value is not null, we will generate a rule for it instead of allowing the LLM to generate a response
            if (defaultValue != null)
                return $"{ruleName}::=\"{defaultValue}\"\n";
            
            return $"{ruleName}::=float\n";
        }
        else if (memberType == typeof(double))
        {
            // If the default value is not null, we will generate a rule for it instead of allowing the LLM to generate a response
            if (defaultValue != null)
                return $"{ruleName}::=\"{defaultValue}\"\n";
            
            return $"{ruleName}::=double\n";
        }
        else if (memberType == typeof(bool))
        {
            // If the default value is not null, we will generate a rule for it instead of allowing the LLM to generate a response
            if (defaultValue != null)
                return $"{ruleName}::=\"{defaultValue}\"\n";
            
            return $"{ruleName}::=boolean\n";
        }
        else if (memberType.IsEnum)
        {
            // if the default value is not null, we will generate a rule for it instead of allowing the LLM to generate a response
            // Enums always have a default value, so there is a specific setting for this
            if (defaultValue != null && Settings.UseEnumDefaults)
                return $"{ruleName}::=\"{Settings.JsonQuote}{defaultValue}{Settings.JsonQuote}\"\n";
            
            return $"{ruleName}::=" + string.Join("|", Enum.GetNames(memberType).Select(e => $"\"{Settings.JsonQuote}{e}{Settings.JsonQuote}\"")) + "\n";
        }
        else if (memberType.IsArray)
        {
            // Now, we have to generate an array rule.
            // Array rules look like this : "\"[\"(Type(\",\"Type)*)?\"]\"\n"
            // We first need to get the type of the array elements, then look for it in our known types
            // If it's already in our known types, we get it from there and get the Name from it, so it will become Name + Array (e.g. stringArray)
            // If it's not in our known types, we generate a rule for it and add it to the known types
            // Then we generate the array rule and return it
            
            // Get the type of the array elements
            Type elementType = memberType.GetElementType();
            
            // Check if the element type is a known type
            if (commonRules.ContainsKey(elementType))
            {
                // // Log and wait for readline
                // Console.WriteLine($"Using existing rule for {elementType.Name}");
                //
                // // Wait for readline
                // Console.ReadLine();
                
                // Get the known rule
                GrammarGeneratorRule knownGeneratorRule = commonRules[elementType];
                
                // Generate the array rule
                return $"{ruleName}::=\"[\"({knownGeneratorRule.Name}(\",\"{knownGeneratorRule.Name})*)?\"]\"\n";
            }
            else
            {
                // // Log and wait for readline
                // Console.WriteLine($"Generating rule for {elementType.Name}");
                //
                // // Log the namespace of the element type
                // Console.WriteLine(elementType.Namespace);
                //
                // // Wait for readline
                // Console.ReadLine();
                
                // Generate the rule for the element type
                string elementRule = GenerateFromType(elementType, false);
                
                // Add the rule to the known rules
                commonRules.Add(elementType, new GrammarGeneratorRule(GetRuleName(elementType), elementRule));
                
                // Generate the array rule
                return $"{ruleName}::=\"[\"({GetRuleName(elementType)}(\",\"{GetRuleName(elementType)})*)?\"]\"\n";
            }
        }
        else
        {
            // The member is not any of the known types, so we will generate a rule for its class (recursive)
            string rule = $"{ruleName}::={memberType.Name}\n";
            string classRules;

            if (defaultValue == null)
            {
                if (Settings.InstantiateUnknownTypes)
                {
                    // We will try to create an instance of the class
                    // If we fail, we will generate the rules for the type instead
                    try
                    {
                        // We don't have an instance, so we will create one
                        object instance = Activator.CreateInstance(memberType);

                        // Generate the rules for the new instance
                        classRules = Generate(instance, false);
                    }
                    catch (Exception e)
                    {
                        // Generate the rules for the type
                        classRules = GenerateFromType(memberType, false);
                    }
                }
                else
                {
                    // Generate the rules for the type
                    classRules = GenerateFromType(memberType, false);
                }

            }
            else
            {
                // Generate the rules for the existing instance
                classRules = Generate(defaultValue, false);
            }
            
            // Add the rule to the known rules
            commonRules.Add(memberType, new GrammarGeneratorRule(memberType.Name, classRules));

            // Return the combined rules
            return rule + classRules;
        }
    }
}
