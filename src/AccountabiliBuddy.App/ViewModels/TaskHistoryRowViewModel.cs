using System.Windows.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One task in the History popover: its title, "Finished Tue 11/10 4:05 PM" (or Deleted / Archived), and its one action, Do again
/// (finished) or Bring back (deleted or archived).
/// </summary>
public sealed record TaskHistoryRowViewModel(string Title, string Sub, string ActionText, MaterialIconKind ActionIcon, ICommand Command);
