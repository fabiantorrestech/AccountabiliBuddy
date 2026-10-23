using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>The main window's tabs by index (<see cref="MainTab"/>, the same order as its tab list and pages).</summary>
public static class SearchTabs
{
    public const int Today = (int)MainTab.Today;
    public const int Profiles = (int)MainTab.Profiles;
    public const int DayPlan = (int)MainTab.DayPlan;
    public const int Calendar = (int)MainTab.Calendar;
    public const int History = (int)MainTab.History;
    public const int Devices = (int)MainTab.Devices;
    public const int Setup = (int)MainTab.Setup;
    public const int Integrations = (int)MainTab.Integrations;
    public const int Settings = (int)MainTab.Settings;
    public const int About = (int)MainTab.About;
}
