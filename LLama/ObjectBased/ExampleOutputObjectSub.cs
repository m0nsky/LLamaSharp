namespace JsonRepairSharp;

public class ExampleOutputObjectSub
{
    // String first name
    public string FirstName = "Peter";
    
    // String last name
    public string LastName = "";
    
    // Integer age
    public int Age = 20;
    
    // Most recent diary entry
    public DiaryEntry MostRecentDiaryEntry;

    // Other diary entries (array)
    public int[] FavoriteNumbers;
    
    // Other diary entries (array)
    public int[] LeastFavoriteNumbers;
    
    // Other diary entries (array)
    public DiaryEntry[] PreviousDiaryEntries;
}