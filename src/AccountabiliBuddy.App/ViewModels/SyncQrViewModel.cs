using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Sync;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The sync QR codes window (ADR 22): today's part of this PC's sync file as up to four codes for the phone to scan
/// with its camera, which opens AccountabiliBuddy and adds them up.
/// </summary>
public sealed partial class SyncQrViewModel : ObservableObject
{
    private readonly Func<Task<IReadOnlyList<string>>> _links;

    /// <param name="links">This PC's code links as of now (<see cref="QrSyncService.LinksForNowAsync"/>).</param>
    public SyncQrViewModel(Func<Task<IReadOnlyList<string>>> links)
    {
        _links = links;
        Text = "Making the codes…";
    }

    /// <summary>One code per item, in order, each with "1 of 3" under it.</summary>
    public ObservableCollection<SyncQrCode> Codes { get; } = new();

    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>Makes the codes again as of now (e.g. after a session ended).</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IReadOnlyList<string> links;
        try
        {
            links = await _links();
        }
        catch (SqliteException ex)
        {
            Text = $"The history can't be read right now ({ex.Message}).";
            return;
        }

        Codes.Clear();
        if (links.Count == 0)
        {
            Text = "This PC isn't linked to your other devices yet. Link it in Devices first.";
            return;
        }

        IReadOnlyList<Bitmap> pictures = QrSyncService.Render(links);
        for (int i = 0; i < pictures.Count; i++)
        {
            Codes.Add(new SyncQrCode(pictures[i], $"{i + 1} of {pictures.Count}"));
        }

        Text = pictures.Count == 1
            ? "Scan the code with your phone's camera: AccountabiliBuddy opens and takes in today."
            : $"Scan all {pictures.Count} codes with your phone's camera, in any order: AccountabiliBuddy opens and adds them up.";
    }
}
