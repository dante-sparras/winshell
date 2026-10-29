using System.Collections.Concurrent;
using Acornima;
using Acornima.Ast;

namespace Winshell.Runtime;

/// <summary>Turns QML binding and handler code into JavaScript function bodies.</summary>
public static class ScriptCompiler
{
    private static readonly ConcurrentDictionary<string, string> BlockCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> FunctionLiteralCache = new(StringComparer.Ordinal);

    /// <summary>How many distinct snippets the caches keep. Past this they drop, so edited bindings cannot grow forever.</summary>
    internal const int CacheLimit = 1024;

    internal static int BlockCacheCount => BlockCache.Count;

    /// <summary>The body of a binding: an expression returns its value; a block returns its last expression.</summary>
    public static string BindingBody(string code, bool isBlock)
    {
        if (!isBlock)
            return "return (" + code + "\n);";
        Trim(BlockCache);
        return BlockCache.GetOrAdd(code, ReturnTail);
    }

    /// <summary>
    /// The body of a signal handler. A handler written as a function (<c>onClicked: mouse => ...</c>) is called
    /// with the signal's arguments.
    /// </summary>
    public static string HandlerBody(string code, bool isBlock)
    {
        if (isBlock)
            return code;
        if (IsFunctionLiteral(code))
            return "return (" + code + "\n).apply(this, arguments);";
        return code + "\n;";
    }

    public static bool IsFunctionLiteral(string code)
    {
        Trim(FunctionLiteralCache);
        return FunctionLiteralCache.GetOrAdd(code, static c =>
        {
            try
            {
                var expr = new Parser().ParseExpression(c, "handler", false);
                return expr is ArrowFunctionExpression or FunctionExpression;
            }
            catch (ParseErrorException)
            {
                return false;
            }
        });
    }

    private static void Trim<T>(ConcurrentDictionary<string, T> cache)
    {
        if (cache.Count >= CacheLimit)
            cache.Clear();
    }

    /// <summary>Puts <c>return</c> before the statements whose value a block binding produces.</summary>
    private static string ReturnTail(string code)
    {
        const string prefix = "(function(){";
        var source = prefix + code + "\n})";
        FunctionBody body;
        try
        {
            var expr = new Parser().ParseExpression(source, "binding", false);
            body = ((FunctionExpression)expr).Body;
        }
        catch (ParseErrorException)
        {
            // "{ "a": "b" }" is not a block but an object, as QML reads it.
            try
            {
                new Parser().ParseExpression("({" + code + "})", "binding", false);
                return "return ({" + code + "\n});";
            }
            catch (ParseErrorException)
            {
            }
            // Let the engine report the error when the binding runs.
            return code;
        }
        var inserts = new List<(int Position, string Text)>();
        if (body.Body.Count > 0)
            MarkTail(body.Body[^1], inserts);
        if (inserts.Count == 0)
            return code;
        var result = code;
        foreach (var (position, text) in inserts.OrderByDescending(i => i.Position))
        {
            var at = position - prefix.Length;
            result = result.Insert(at, text);
        }
        return result;
    }

    private static void MarkTail(Statement statement, List<(int, string)> inserts)
    {
        switch (statement)
        {
            case ExpressionStatement e:
                inserts.Add((e.Expression.Start, "return ("));
                inserts.Add((e.Expression.End, ")"));
                break;
            case IfStatement i:
                MarkTail(i.Consequent, inserts);
                if (i.Alternate is not null)
                    MarkTail(i.Alternate, inserts);
                break;
            case BlockStatement b when b.Body.Count > 0:
                MarkTail(b.Body[^1], inserts);
                break;
            case TryStatement t:
                MarkTail(t.Block, inserts);
                if (t.Handler is not null)
                    MarkTail(t.Handler.Body, inserts);
                break;
        }
    }
}
