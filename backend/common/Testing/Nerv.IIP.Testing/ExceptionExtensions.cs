namespace Nerv.IIP.Testing;

public static class ExceptionExtensions
{
    public static IEnumerable<Exception> Flatten(this Exception? exception)
    {
        if (exception is null)
        {
            yield break;
        }

        yield return exception;
        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions.SelectMany(Flatten))
            {
                yield return innerException;
            }
        }

        if (exception.InnerException is not null)
        {
            foreach (var innerException in exception.InnerException.Flatten())
            {
                yield return innerException;
            }
        }
    }
}
