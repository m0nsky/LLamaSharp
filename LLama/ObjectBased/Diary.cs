namespace JsonRepairSharp;

public class Diary
{
    // Diary entries
    public DiaryEntry[] Entries;
}

public class DiaryEntry
{
    // Date
    public string Date = "";
    
    // Entry
    public string Entry = "";
    
    // Mood
    public Mood Mood = Mood.Happy;
}