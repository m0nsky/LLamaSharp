namespace LLama.Grammars.Generator;

public record GrammarGeneratorSettings
{
    // TODO : provide summery / better descriptions for settings, see InferenceParams.cs
    
    // Default value mode
    public DefaultValueMode DefaultValueMode = DefaultValueMode.Complete;
    
    // Array mode
    public ArrayMode ArrayMode = ArrayMode.Fill;
    
    // Use enum defaults
    public bool UseEnumDefaults = false;
    
    // Instantiate unknown types (if false, we generate the rule for it's Type instead)
    public bool InstantiateUnknownTypes = true;
    
    // Base rules for string (not including quotes for json)
    // https://github.com/ggerganov/llama.cpp/blob/master/grammars/json.gbnf
    public string StringRule = "( [^\"\\\\] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F]) )*"; 
    
    // JSON quote (double escape)
    public string JsonQuote = "\\\"";
    
    // GBNF quote
    public string GbnfQuote = "\"";
    
    // New line
    public string NewLine = "\n";
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
