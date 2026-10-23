using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of the stacked prompt: a site or app seen while planning, with the single prompt's scope choice (whole site or
/// only certain parts) and the row's answer.
/// </summary>
public sealed partial class ClassifyStackRowViewModel : ObservableObject
{
    public ClassifyStackRowViewModel(ClassifyPromptViewModel prompt)
    {
        Prompt = prompt;
        var options = new List<ScopeOption>();
        if (prompt.HasSite)
        {
            options.Add(new ScopeOption(prompt.SiteLabel, ClassifyScope.Site));
            if (prompt.HasPart)
            {
                options.Add(new ScopeOption(prompt.PartLabel, ClassifyScope.Part));
            }

            if (prompt.HasPage)
            {
                options.Add(new ScopeOption(prompt.PageLabel, ClassifyScope.Page));
            }
        }

        options.Add(new ScopeOption(prompt.WholeAppLabel, ClassifyScope.WholeApp));
        options.Add(new ScopeOption("Only this view (match the window title)", ClassifyScope.Title));
        ScopeOptions = options;
        SelectedScope = options.First(option => option.Scope == prompt.Scope);
    }

    /// <summary>The scope and pattern, as the single prompt offers them.</summary>
    public ClassifyPromptViewModel Prompt { get; }

    /// <summary>Whole site, a part, this page, the whole app or this view (a list per row; radio groups would mix rows).</summary>
    public IReadOnlyList<ScopeOption> ScopeOptions { get; }

    [ObservableProperty]
    public partial ScopeOption SelectedScope { get; set; }

    partial void OnSelectedScopeChanged(ScopeOption value)
    {
        Prompt.Scope = value.Scope;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDistracting), nameof(IsOnTrack), nameof(IsExempt))]
    public partial StackVerdict Verdict { get; set; }

    public bool IsDistracting
    {
        get => Verdict == StackVerdict.Distracting;
        set => Choose(StackVerdict.Distracting, value);
    }

    public bool IsOnTrack
    {
        get => Verdict == StackVerdict.OnTrack;
        set => Choose(StackVerdict.OnTrack, value);
    }

    public bool IsExempt
    {
        get => Verdict == StackVerdict.Exempt;
        set => Choose(StackVerdict.Exempt, value);
    }

    private void Choose(StackVerdict verdict, bool selected)
    {
        if (selected)
        {
            Verdict = verdict;
        }
        else if (Verdict == verdict)
        {
            Verdict = StackVerdict.Unanswered;
        }
    }
}
