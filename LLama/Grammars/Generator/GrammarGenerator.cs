using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LLama.Grammars.Generator;

public sealed class GrammarGenerator
{
    // Known rules (dictionary: key = type, value = rule)
    private readonly Dictionary<Type, GrammarGeneratorRule> _knownRules = new();
    
    // Default value mode
    public DefaultValueMode DefaultValueMode = DefaultValueMode.Complete;
    
    // Array mode
    public ArrayMode ArrayMode = ArrayMode.Fill;
    
    // Use enum defaults
    public bool UseEnumDefaults = false;
    
    // Instantiate unknown types (if false, we generate the rule for it's Type instead)
    public bool InstantiateUnknownTypes = true;
    
    // Base rules for string (not including quotes for json)
    //private string StringRule = "([^\\\"]*)";
    private string StringRule = "( [^\"\\\\] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F]) )*"; // https://github.com/ggerganov/llama.cpp/blob/master/grammars/json.gbnf
    
    // JSON quote start segment (this is a simple quote in json, also in between gbnf quotes start and end)
    private string JsonQuote = "\\\"";
    
    // Initialize common rules
    public GrammarGenerator()
    {
        // Add the common rules
        _knownRules.Add(typeof(string), new GrammarGeneratorRule(
            "string", 
            $"\"{JsonQuote}\"{StringRule}\"{JsonQuote}\"\n")
        );
        
        _knownRules.Add(typeof(bool), new GrammarGeneratorRule(
            "boolean", 
            "\"true\"|\"false\"\n")
        );
        
        _knownRules.Add(typeof(int), new GrammarGeneratorRule(
            "int", 
            "[-]?[0-9]+\n")
        );
        
        _knownRules.Add(typeof(uint), new GrammarGeneratorRule(
            "uint", 
            "[0-9]+\n")
        );
        
        _knownRules.Add(typeof(float), new GrammarGeneratorRule(
            "float", 
            "[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[fF]?\n")
        );
        
        _knownRules.Add(typeof(double), new GrammarGeneratorRule(
            "double", 
            "[-]?[0-9]+\".\"?[0-9]*([eE][-+]?[0-9]+)?[dD]?\n")
        );
    }
    
    // Converts an object to a GBNF grammar
    public string GenerateFromObject(object obj, bool isRoot = true)
    {
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
        
        // If this is the root node, we will add the common rules
        if (isRoot)
        {
            // Add the common rules
            foreach (var knownRule in _knownRules)
            {
                gbnf.Append(knownRule.Value);
            }
        }
        
        // Create a GBNF rule for each member
        foreach (MemberInfo memberInfo in memberInfos)
        {
            // Get the value of the member in this instance (for now, we can't get the default value if we don't have an instance)
            object? instanceValue = memberInfo.MemberType == MemberTypes.Field
                ? ((FieldInfo)memberInfo).GetValue(obj)
                : ((PropertyInfo)memberInfo).GetValue(obj);
            
            gbnf.Append(GenerateRuleForMember(memberInfo, instanceValue));
            
            // Add the member name to the root rule
            objectRule += $"\"{JsonQuote}{memberInfo.Name}{JsonQuote}:\"{GetRuleName(memberInfo)}";
        
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
        
        // // Clear console
        // Console.Clear();
        //
        // // Log the generated GBNF rules
        // Console.WriteLine(gbnf.ToString());
        //
        // // Wait for readline
        // Console.ReadLine();

        // Return the GBNF rules as a string
        return gbnf.ToString();
        
        // // Generate the GBNF grammar from the class
        // return GenerateFromType(type);
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
            
            rootRule += $"\"{JsonQuote}{memberInfo.Name}{JsonQuote}:\"{GetRuleName(memberInfo)}";
        
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
        
        // // Clear console
        // Console.Clear();
        //
        // // Log the generated GBNF rules
        // Console.WriteLine(gbnf.ToString());

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
            if (DefaultValueMode == DefaultValueMode.Keep)
            {
                // If the default value is not null (and not empty), we will generate a rule that just keeps the default value
                if (defaultValue != null && (string) defaultValue != "")
                {
                    return $"{ruleName}::=\"{JsonQuote}{defaultValue}{JsonQuote}\"\n";
                }

            }
            else if (DefaultValueMode == DefaultValueMode.Discard)
            {
                // We will generate a rule that just discards the value, and allows the LLM to generate a string
                return $"{ruleName}::=string\n";
            }
            else if (DefaultValueMode == DefaultValueMode.Complete)
            {
                // If the default value isn't null, and isn't empty, we allow autocompletion of the default value
                if (defaultValue != null && (string) defaultValue != "")
                {
                    return $"{ruleName}::=\"{JsonQuote}{defaultValue}\" {StringRule} \"{JsonQuote}\"\n";
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
            if (defaultValue != null && UseEnumDefaults)
                return $"{ruleName}::=\"{JsonQuote}{defaultValue}{JsonQuote}\"\n";
            
            return $"{ruleName}::=" + string.Join("|", Enum.GetNames(memberType).Select(e => $"\"{JsonQuote}{e}{JsonQuote}\"")) + "\n";
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
            if (_knownRules.ContainsKey(elementType))
            {
                // // Log and wait for readline
                // Console.WriteLine($"Using existing rule for {elementType.Name}");
                //
                // // Wait for readline
                // Console.ReadLine();
                
                // Get the known rule
                GrammarGeneratorRule knownGeneratorRule = _knownRules[elementType];
                
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
                _knownRules.Add(elementType, new GrammarGeneratorRule(elementType.Name, elementRule));
                
                // Generate the array rule
                return $"{ruleName}::=\"[\"({elementRule}(\",\"{elementRule})*)?\"]\"\n";
            }
        }
        else
        {
            // The member is not any of the known types, so we will generate a rule for its class (recursive)
            string rule = $"{ruleName}::={memberType.Name}\n";
            string classRules;

            if (defaultValue == null)
            {
                if (InstantiateUnknownTypes)
                {
                    // We will try to create an instance of the class
                    // If we fail, we will generate the rules for the type instead
                    try
                    {
                        // We don't have an instance, so we will create one
                        object instance = Activator.CreateInstance(memberType);

                        // Generate the rules for the new instance
                        classRules = GenerateFromObject(instance, false);
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
                classRules = GenerateFromObject(defaultValue, false);
            }
            
            // Add the rule to the known rules
            _knownRules.Add(memberType, new GrammarGeneratorRule(memberType.Name, classRules));

            // Return the combined rules
            return rule + classRules;
        }
    }
}

// Default value mode enum (Discard/Overwrite/Complete)
public enum DefaultValueMode
{
    Keep,
    Discard,
    Complete
}
    
// Array mode enum (Fill/Select)
public enum ArrayMode
{
    Fill,
    Select
}

