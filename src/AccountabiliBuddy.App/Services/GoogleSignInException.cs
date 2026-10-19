namespace AccountabiliBuddy.App.Services;

/// <summary>Google sign-in did not complete; the message is shown to the user as is.</summary>
public sealed class GoogleSignInException : Exception
{
    public GoogleSignInException(string message)
        : base(message)
    {
    }
}
