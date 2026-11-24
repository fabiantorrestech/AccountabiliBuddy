using System.Windows.Input;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// The bin pill (Tasks redesign checklist 3.68-3.73): shows <see cref="Bin"/> and offers the bins in a menu ("Move to" for an existing task,
/// "Add to" for a new one, <see cref="IsNewTask"/>). A pick is only raised (<see cref="BinPicked"/>, <see cref="PickCommand"/> with the
/// <see cref="TaskBin"/>); the caller applies it and sets <see cref="Bin"/>. A move into This session must go through the session's cap
/// (<c>TaskActionsService.MoveToSessionAsync</c>), as Move to does. Keys in the menu: ↑/↓ move, Enter picks, Esc closes.
/// </summary>
public partial class BinPill : UserControl
{
    public static readonly StyledProperty<TaskBin> BinProperty = AvaloniaProperty.Register<BinPill, TaskBin>(nameof(Bin), TaskBin.Sort);

    public static readonly StyledProperty<bool> IsNewTaskProperty = AvaloniaProperty.Register<BinPill, bool>(nameof(IsNewTask));

    public static readonly StyledProperty<ICommand?> PickCommandProperty = AvaloniaProperty.Register<BinPill, ICommand?>(nameof(PickCommand));

    public BinPill()
    {
        InitializeComponent();
        Menu.AddHandler(KeyDownEvent, OnMenuKeyDown, RoutingStrategies.Tunnel);
        Show();
    }

    /// <summary>A bin was picked in the menu (also when it is the task's bin now; the caller may ignore that).</summary>
    public event EventHandler<TaskBin>? BinPicked;

    /// <summary>The task's bin (a new task: where it will be added).</summary>
    public TaskBin Bin
    {
        get => GetValue(BinProperty);
        set => SetValue(BinProperty, value);
    }

    /// <summary>A task being added: the menu says "Add to", offers no This session and marks To sort "Default".</summary>
    public bool IsNewTask
    {
        get => GetValue(IsNewTaskProperty);
        set => SetValue(IsNewTaskProperty, value);
    }

    /// <summary>Run with the picked <see cref="TaskBin"/> (for binding; <see cref="BinPicked"/> is raised as well).</summary>
    public ICommand? PickCommand
    {
        get => GetValue(PickCommandProperty);
        set => SetValue(PickCommandProperty, value);
    }

    public bool IsMenuOpen => Menu.IsOpen;

    /// <summary>Opens the menu with the keyboard on the task's bin.</summary>
    public void OpenMenu()
    {
        Show();
        Menu.IsOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => FocusRow(Rows().FindIndex(row => row.IsChecked == true) is var at and >= 0 ? at : 0));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BinProperty || change.Property == IsNewTaskProperty)
        {
            Show();
        }
    }

    private void Show()
    {
        string name = BinPillChoice.NameOf(Bin);
        PillIcon.Kind = TaskBinIcons.Redesigned(Bin);
        PillName.Text = name;
        AutomationProperties.SetName(Pill, $"Bin: {name}. Change bin");
        MenuHeading.Text = IsNewTask ? "Add to" : "Move to";
        MenuRows.ItemsSource = BinPillChoice.For(Bin, IsNewTask);
    }

    private void OnPillClick(object? sender, RoutedEventArgs e)
    {
        if (Menu.IsOpen)
        {
            Menu.IsOpen = false;
            return;
        }

        OpenMenu();
    }

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: BinPillChoice choice })
        {
            return;
        }

        Menu.IsOpen = false;
        Show(); // the clicked toggle's own tick is undone: the pill shows the bin the caller sets
        BinPicked?.Invoke(this, choice.Bin);
        if (PickCommand?.CanExecute(choice.Bin) == true)
        {
            PickCommand.Execute(choice.Bin);
        }
    }

    private void OnMenuClosed(object? sender, EventArgs e)
    {
        if (Pill.IsEffectivelyVisible)
        {
            Pill.Focus(NavigationMethod.Directional);
        }
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        List<ToggleButton> rows = Rows();
        int at = rows.FindIndex(row => row.IsFocused);
        switch (e.Key)
        {
            case Key.Escape:
                Menu.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Down:
                FocusRow(at < 0 ? 0 : Math.Min(at + 1, rows.Count - 1));
                e.Handled = true;
                break;
            case Key.Up:
                FocusRow(at < 0 ? 0 : Math.Max(at - 1, 0));
                e.Handled = true;
                break;
        }
    }

    private List<ToggleButton> Rows() => MenuRows.GetVisualDescendants().OfType<ToggleButton>().ToList();

    private void FocusRow(int index)
    {
        List<ToggleButton> rows = Rows();
        if (index >= 0 && index < rows.Count)
        {
            rows[index].Focus(NavigationMethod.Directional);
        }
    }
}
