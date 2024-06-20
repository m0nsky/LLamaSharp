namespace JsonRepairSharp;

public class ExampleObject
{
    // Message
    public string Message = "";
    
    // Mood
    public Mood Mood = Mood.Happy;
}

// Mood enum
public enum Mood
{
    Happy,
    Sad,
    Angry,
    Excited
}