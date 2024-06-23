namespace LLama.Grammars;

public class GrammarGeneratorRule
{
    // Name
    public string Name;
    
    // Namespace
    public string Namespace;
    
    // Rule
    public string Rule;
    
    // Constructor
    public GrammarGeneratorRule(string name, string rule, string ns = "")
    {
        Name = name;
        Rule = rule;
        Namespace = ns;
    }
    
    // ToString
    public override string ToString()
    {
        // Without namespace
        return $"{Name}::={Rule}";
        
        // With namespace
        //return $"{Namespace}_{Name}::={Rule}";
    }
}