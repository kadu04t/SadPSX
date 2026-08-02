namespace SadPSX.Frontend.UI.Screens;

internal sealed class UiScreenNavigator
{
    private readonly Stack<UiScreenId> _history = new();

    public UiScreenNavigator(UiScreenId initialScreen)
    {
        Current = initialScreen;
    }

    public UiScreenId Current { get; private set; }

    public bool CanGoBack => _history.Count > 0;

    public event Action<UiScreenId>? Changed;

    public void Navigate(UiScreenId screen, bool rememberCurrent = true)
    {
        if (screen == Current)
            return;

        if (rememberCurrent)
            _history.Push(Current);

        Current = screen;
        Changed?.Invoke(Current);
    }

    public bool TryGoBack()
    {
        if (!_history.TryPop(out UiScreenId previous))
            return false;

        Current = previous;
        Changed?.Invoke(Current);
        return true;
    }

    public void Reset(UiScreenId screen)
    {
        _history.Clear();
        if (screen == Current)
            return;

        Current = screen;
        Changed?.Invoke(Current);
    }
}
