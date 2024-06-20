namespace LLama.Examples.Examples;

public class GrammarGeneratorResponseExample
{
    // Message field
    public string reply;
    
    // Mood field
    public string mood;
    
    // Intent field
    public string intent;
    
    // Cookie taste field
    public CookieTaste cookieTaste;
}

// Cookie taste enum
public enum CookieTaste
{
    // Sweet taste
    Sweet,
    
    // Salty taste
    Salty,
    
    // Bitter taste
    Bitter,
    
    // Sour taste
    Sour,
    
    // Spicy taste
    Spicy
}