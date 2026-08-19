using System.IO;
using System.Text;
using System.Windows.Markup;

namespace SysTuneX.App.Diagnostics;

/// <summary>
/// Turns an exception into something a person can act on.
///
/// WPF reports a failed XAML load as "Provide value on 'TypeConverterMarkupExtension' threw an
/// exception", which names neither the file, the line, nor the value that could not be converted.
/// All of that sits in the inner exceptions and in <see cref="XamlParseException"/>'s line
/// information, so a report that stops at <c>exception.Message</c> throws the diagnosis away.
/// </summary>
public static class ExceptionReport
{
    /// <summary>Renders the full exception chain, innermost cause last, with XAML line info.</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        Append(builder, exception, depth: 0);
        return builder.ToString().TrimEnd();
    }

    /// <summary>The innermost exception - the one that actually names what went wrong.</summary>
    public static Exception RootCause(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var current = exception;
        while (true)
        {
            var next = current switch
            {
                AggregateException aggregate when aggregate.InnerExceptions.Count > 0 => aggregate.InnerExceptions[0],
                _ => current.InnerException,
            };

            if (next is null)
            {
                return current;
            }

            current = next;
        }
    }

    private static void Append(StringBuilder builder, Exception exception, int depth)
    {
        var indent = new string(' ', depth * 2);

        builder.Append(indent)
            .Append(depth == 0 ? string.Empty : "-> ")
            .Append(exception.GetType().Name)
            .Append(": ")
            .AppendLine(exception.Message);

        if (exception is XamlParseException parse)
        {
            builder.Append(indent).Append("   at line ").Append(parse.LineNumber)
                .Append(", position ").Append(parse.LinePosition);

            if (parse.BaseUri is not null)
            {
                builder.Append(" of ").Append(parse.BaseUri);
            }

            builder.AppendLine();
        }

        // Missing files and missing pack:// resources both land here, and the path is the
        // whole answer, so it is worth pulling out of the message when the message omits it.
        if (exception is FileNotFoundException { FileName: { Length: > 0 } fileName })
        {
            builder.Append(indent).Append("   file: ").AppendLine(fileName);
        }

        switch (exception)
        {
            case AggregateException aggregate:
                foreach (var inner in aggregate.InnerExceptions)
                {
                    Append(builder, inner, depth + 1);
                }

                break;

            case { InnerException: { } inner }:
                Append(builder, inner, depth + 1);
                break;
        }
    }
}
