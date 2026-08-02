namespace SadPSX.Frontend.UI.Navigation;

internal sealed class UiFocusNavigator
{
    private readonly Func<int, bool> _isEnabled;

    public UiFocusNavigator(
        int itemCount,
        Func<int, bool>? isEnabled = null)
    {
        if (itemCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(itemCount));

        ItemCount = itemCount;
        _isEnabled = isEnabled ?? (_ => true);
        SelectedIndex = FindEnabledIndex(0, 1);
    }

    public int ItemCount { get; }

    public int SelectedIndex { get; private set; }

    public bool Move(UiAction action)
    {
        int direction = action switch
        {
            UiAction.Up or UiAction.Left => -1,
            UiAction.Right or UiAction.Down => 1,
            _ => 0,
        };
        if (direction == 0)
            return false;

        int nextIndex = FindEnabledIndex(SelectedIndex + direction, direction);
        if (nextIndex < 0 || nextIndex == SelectedIndex)
            return false;

        SelectedIndex = nextIndex;
        return true;
    }

    public bool Select(int index)
    {
        if (index < 0 || index >= ItemCount || !_isEnabled(index))
            return false;

        bool changed = SelectedIndex != index;
        SelectedIndex = index;
        return changed;
    }

    public void Refresh()
    {
        if (SelectedIndex >= 0 && _isEnabled(SelectedIndex))
            return;

        SelectedIndex = FindEnabledIndex(0, 1);
    }

    private int FindEnabledIndex(int startIndex, int direction)
    {
        for (int offset = 0; offset < ItemCount; offset++)
        {
            int index = Wrap(startIndex + (offset * direction));
            if (_isEnabled(index))
                return index;
        }

        return -1;
    }

    private int Wrap(int index)
    {
        int wrapped = index % ItemCount;
        return wrapped < 0 ? wrapped + ItemCount : wrapped;
    }
}
