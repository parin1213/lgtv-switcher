namespace LGTVSwitcher.LgWebOsClient;

public class LgTvRegistrationException : Exception
{
    public LgTvRegistrationException(string message)
        : base(message)
    {
    }

    public LgTvRegistrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class LgTvCommandException : Exception
{
    public LgTvCommandException(string message)
        : base(message)
    {
    }

    public LgTvCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}