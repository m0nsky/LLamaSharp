namespace JsonRepairSharp;

public class DiaryEntry
{
    // Date
    public string Date = "";
    
    // Entry
    public string Entry = "";
    
    // Mood
    public Mood Mood;
    
    // Array "lucky numbers for the day"
    public int[] LuckyNumbersForTheDay;
}