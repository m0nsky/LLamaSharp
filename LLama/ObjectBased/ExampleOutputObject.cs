namespace JsonRepairSharp;

public class ExampleOutputObject
{
    // Answer
    public string Answer = "";
    
    // Favorite pizza
    public string FavoritePizza = "";
    
    // Favorite topping
    public string FavoriteTopping = "";
    
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