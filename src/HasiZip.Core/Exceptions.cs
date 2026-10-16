namespace HasiZip.Core;

/// <summary>Das Archiv ist verschlüsselt und es wurde kein Passwort angegeben.</summary>
public sealed class PasswordRequiredException : Exception
{
    public PasswordRequiredException() : base("Das Archiv ist mit einem Passwort geschützt.") { }
}

/// <summary>Das angegebene Passwort passt nicht (oder das verschlüsselte Archiv ist beschädigt).</summary>
public sealed class WrongPasswordException : Exception
{
    public WrongPasswordException(Exception? inner = null) : base("Das Passwort ist falsch.", inner) { }
}

/// <summary>Die Datei ist kein unterstütztes Archiv oder beschädigt.</summary>
public sealed class UnsupportedArchiveException : Exception
{
    public UnsupportedArchiveException(string message, Exception? inner = null) : base(message, inner) { }
}
