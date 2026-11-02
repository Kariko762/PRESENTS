namespace FisPresentationPlatform.Services;

public sealed class PageReadinessGate
{
    private bool _navigationPassed;
    private bool _messageReceived;
    private bool _failed;
    private bool _requireMessage;
    public string Token { get; private set; } = "";
    public bool IsReady => !_failed && _navigationPassed && (!_requireMessage || _messageReceived);

    public void Begin(bool requireMessage)
    {
        Token = Guid.NewGuid().ToString("N");
        _requireMessage = requireMessage;
        _navigationPassed = _messageReceived = _failed = false;
    }

    public void NavigationFinished(bool successful) { _navigationPassed = successful; _failed = !successful; }
    public void Fail() => _failed = true;
    public bool ReceiveReady(string? token)
    {
        if (_failed || Token.Length == 0 || !string.Equals(Token, token, StringComparison.Ordinal)) return false;
        _messageReceived = true;
        return true;
    }
}