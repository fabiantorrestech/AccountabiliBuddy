using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The flyout behind a category's colour swatch: 24 presets to start from, red, green and blue to adjust, and a hex box. All of it works
/// on a draft: the colour is kept when the flyout closes, not while dragging, because saving rebuilds the category rows and would take
/// the flyout away. Esc leaves the colour as it was. Colours stay "#RRGGBB" (ADR 62).
/// </summary>
public sealed partial class CategoryColorEditorViewModel : ObservableObject
{
    private readonly Action<string> _save;
    private string _original;
    private bool _cancelled;
    private bool _settingHex;

    /// <param name="save">Keeps a changed colour.</param>
    public CategoryColorEditorViewModel(string color, Action<string> save)
    {
        _save = save;
        _original = Normal(color) ?? TaskCategory.Palette[0];
        Red = new ColorChannelViewModel("Red", OnChannelChanged);
        Green = new ColorChannelViewModel("Green", OnChannelChanged);
        Blue = new ColorChannelViewModel("Blue", OnChannelChanged);
        Channels = [Red, Green, Blue];
        Presets = TaskCategory.Presets.Select(hex => new ColorPresetViewModel(hex, Pick)).ToList();
        HexText = _original;
        PreviewBrush = TaskCategoryBrushes.Of(_original);
        Show(_original);
    }

    public ColorChannelViewModel Red { get; }

    public ColorChannelViewModel Green { get; }

    public ColorChannelViewModel Blue { get; }

    public IReadOnlyList<ColorChannelViewModel> Channels { get; }

    public IReadOnlyList<ColorPresetViewModel> Presets { get; }

    [ObservableProperty]
    public partial IBrush PreviewBrush { get; set; }

    [ObservableProperty]
    public partial string HexText { get; set; }

    /// <summary>The hex box holds something that isn't a colour; the last good colour stays the draft.</summary>
    [ObservableProperty]
    public partial bool IsHexBad { get; set; }

    /// <summary>The flyout opens: the draft starts from the colour the category has now.</summary>
    public void Begin(string color)
    {
        _original = Normal(color) ?? _original;
        _cancelled = false;
        Show(_original);
    }

    /// <summary>Esc: closing the flyout keeps nothing.</summary>
    public void Cancel()
    {
        _cancelled = true;
    }

    /// <summary>The flyout closed: a changed colour is kept, unless Esc closed it.</summary>
    public void Finish()
    {
        string draft = Draft;
        if (!_cancelled && !string.Equals(draft, _original, StringComparison.OrdinalIgnoreCase))
        {
            _original = draft;
            _save(draft);
        }
    }

    private string Draft => $"#{Red.Whole:X2}{Green.Whole:X2}{Blue.Whole:X2}";

    private void Pick(string hex)
    {
        Show(hex);
    }

    private void Show(string hex)
    {
        int rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        Red.SetQuietly((byte)(rgb >> 16));
        Green.SetQuietly((byte)(rgb >> 8));
        Blue.SetQuietly((byte)rgb);
        ShowDraft();
    }

    private void OnChannelChanged()
    {
        ShowDraft();
    }

    private void ShowDraft()
    {
        string draft = Draft;
        PreviewBrush = TaskCategoryBrushes.Of(draft);
        _settingHex = true;
        HexText = draft;
        _settingHex = false;
        IsHexBad = false;
        foreach (ColorPresetViewModel preset in Presets)
        {
            preset.IsSelected = string.Equals(preset.Hex, draft, StringComparison.OrdinalIgnoreCase);
        }
    }

    partial void OnHexTextChanged(string value)
    {
        if (_settingHex)
        {
            return;
        }

        if (Normal(value) is string hex)
        {
            Show(hex);
        }
        else
        {
            IsHexBad = true;
        }
    }

    // "#RRGGBB" in capitals from what was typed (the # may be left out); null when it isn't six hex digits.
    private static string? Normal(string? text)
    {
        string digits = (text ?? string.Empty).Trim().TrimStart('#');
        return digits.Length == 6 && digits.All(char.IsAsciiHexDigit) ? $"#{digits.ToUpperInvariant()}" : null;
    }
}
