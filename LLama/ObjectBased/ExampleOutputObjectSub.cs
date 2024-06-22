namespace JsonRepairSharp;

public class ExampleOutputObjectSub
{
    // String first name
    public string FirstName = "";
    
    // String last name
    public string LastName = "";
    
    // Integer age
    public int Age = 0;
    
    // Most recent diary entry
    public DiaryEntry MostRecentDiaryEntry = new DiaryEntry();
    
    // Other diary entries (array)
    public DiaryEntry[] PreviousDiaryEntries;
}