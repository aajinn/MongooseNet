namespace MongooseNet.Exceptions;

/// <summary>Base exception for all MongooseNet errors.</summary>
public class MongooseNetException : Exception
{
    public MongooseNetException(string message) : base(message) { }
    public MongooseNetException(string message, Exception inner) : base(message, inner) { }
}
