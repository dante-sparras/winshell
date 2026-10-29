namespace Winshell.Runtime.Tests;

/// <summary>Edited bindings must not grow the compiler caches without a bound.</summary>
public class ScriptCacheTests
{
    [Fact]
    public void The_block_cache_drops_once_it_is_full()
    {
        for (var i = 0; i < ScriptCompiler.CacheLimit + 3; i++)
            ScriptCompiler.BindingBody("{ var n = " + i + " }", true);

        Assert.True(ScriptCompiler.BlockCacheCount < ScriptCompiler.CacheLimit);
        Assert.Contains("return", ScriptCompiler.BindingBody("{ 4 }", true), StringComparison.Ordinal);
    }
}
