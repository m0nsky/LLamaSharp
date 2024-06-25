namespace LLama.Examples.Examples.ObjectBased.ExampleObjects;

public class ExampleOutputObject
{
    // Favorite pizza
    public string FavoritePizza;
    
    // Favorite pizza restaurant
    public string FavoritePizzaRestaurant = "";
    
    // Favorite toppings
    public bool[] FiveDifferentBooleans;
    
    // Array of diary entries
    //public DiaryEntry[] DiaryEntries 
    
    // // Mood private
    // private Mood mood = Mood.Angry;
    //
    // // Mood
    // public Mood Mood
    // {
    //     get
    //     {
    //         return mood;
    //     }
    //     set
    //     {
    //         // Check if the value has changed
    //         if (value == mood)
    //             return;
    //         
    //         mood = value;
    //         OnMoodChanged();
    //     }
    // }
    //
    // private void OnMoodChanged()
    // {
    //     // Log "mood changed"
    //     //Console.WriteLine("LLM modified the Mood property!");
    // }

    // // Answer
    // private string answer = "";
    //
    // public string Answer
    // {
    //     get
    //     {
    //         return answer;
    //     }
    //     set
    //     {
    //         // Check if the value has changed
    //         if (value == answer)
    //             return;
    //         
    //         answer = value;
    //         OnAnswerChanged();
    //     }
    // }
    //
    // void OnAnswerChanged()
    // {
    //     // Log "answer changed"
    //     //Console.WriteLine("LLM modified the Answer property!");
    // }
    
    // ExampleOutputObjectSub
    //public ExampleOutputObjectSub MessageAuthor = new ExampleOutputObjectSub();
}

// Mood enum
public enum Mood
{
    Happy,
    Sad,
    Angry,
    Excited,
    Bored,
    Down,
    Depressed,
    Anxious,
    Cheerful,
    Lonely,
    Tired,
    Sleepy,
    Nervous,
    Calm,
    Playful,
    Fun,
    Stressed,
    Relaxed,
    Content,
    Frustrated,
    Irritated,
    Confused,
    Overwhelmed,
    Disappointed,
    Hopeful,
    Grateful,
    Proud,
    Guilty,
    Jealous
}