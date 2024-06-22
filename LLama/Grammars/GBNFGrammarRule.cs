namespace LLama.Grammars;

public class GBNFGrammarRule
{
    // Name
    public string Name;
    
    // Rule
    public string Rule;
    
    // Constructor
    public GBNFGrammarRule(string name, string rule)
    {
        Name = name;
        Rule = rule;
    }
    
    // ToString
    public override string ToString()
    {
        return $"{Name}::={Rule}";
    }
}