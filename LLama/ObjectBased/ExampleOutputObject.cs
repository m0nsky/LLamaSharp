using System;
using System.Collections.Generic;

namespace JsonRepairSharp;

public class ExampleOutputObject
{
    // Favorite pizza
    public string FavoritePizza = "";
    
    // Favorite topping (list of strings, just cheese and pepperoni for now)
    List<string> FavoriteToppingChoice = new List<string> { "Cheese", "Pepperoni" };
    
    // Mood private
    private Mood mood = Mood.Angry;
    
    // Mood
    public Mood Mood
    {
        get
        {
            return mood;
        }
        set
        {
            // Check if the value has changed
            if (value == mood)
                return;
            
            mood = value;
            OnMoodChanged();
        }
    }

    private void OnMoodChanged()
    {
        // Log "mood changed"
        Console.WriteLine("LLM modified the Mood property!");
    }

    // Answer
    private string answer = "";
    
    public string Answer
    {
        get
        {
            return answer;
        }
        set
        {
            // Check if the value has changed
            if (value == answer)
                return;
            
            answer = value;
            OnAnswerChanged();
        }
    }
    
    void OnAnswerChanged()
    {
        answer += "!";
        
        // Log "answer changed"
        Console.WriteLine("LLM modified the Answer property!");
    }
}

// Mood enum
public enum Mood
{
    Happy,
    Sad,
    Angry,
    Excited
}