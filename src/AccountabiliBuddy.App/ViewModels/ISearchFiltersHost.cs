using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The search a Search filters panel works on (Tasks redesign item 7): the Shelf's (<see cref="ShelfViewModel"/>) or the Archive's
/// (<see cref="NotebookViewModel"/>). The panel reads and writes the filters and the search words through it, and (the Shelf only) the
/// label row's picks, which its label chips start from (7.23).
/// </summary>
public interface ISearchFiltersHost
{
    /// <summary>The filters in force; setting them applies them at once (7.34).</summary>
    TaskSearchFilters SearchFilters { get; set; }

    /// <summary>The search box's text.</summary>
    string SearchWords { get; set; }

    /// <summary>The task list (labels, categories, tags in use); null before it exists.</summary>
    TaskStore? SearchStore { get; }

    /// <summary>The label is lit in the label row (the Shelf only): its chip shows ✓ include.</summary>
    bool IsLabelInRow(Guid labelId);

    /// <summary>"No label" is lit in the row's +N menu (the Shelf only).</summary>
    bool IsNoLabelInRow { get; }

    /// <summary>Takes a label (null: "No label") off the label row, as the chip's state moves into the panel.</summary>
    void TakeOffRow(Guid? labelId);
}
