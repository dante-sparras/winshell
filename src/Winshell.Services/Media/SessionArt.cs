using Windows.Storage.Streams;

namespace Winshell.Media;

/// <summary>
/// One session's track art, read from the thumbnail Windows gives into the <see cref="ArtCache"/>. Only the newest
/// read is shown; each url it stops showing is released. Lives on the UI thread.
/// </summary>
/// <param name="loop">Where a finished read is applied.</param>
/// <param name="missed">Runs with the reason when art cannot be read, once until art arrives again.</param>
/// <param name="changed">Runs when <see cref="Url"/> changes.</param>
internal sealed class SessionArt(IEventLoop loop, Action<string> missed, Action changed)
{
    private int _request;
    private int _applied;
    private bool _missLogged;
    private bool _released;

    /// <summary>A <c>file:///</c> url of the art, or empty.</summary>
    public string Url { get; private set; } = "";

    /// <summary>A read is running. Starting another drops its result.</summary>
    public bool Loading => _applied != _request;

    public void Load(IRandomAccessStreamReference? thumbnail)
    {
        var request = Interlocked.Increment(ref _request);
        if (thumbnail is null)
        {
            Set("", request);
            return;
        }
        _ = Read();

        async Task Read()
        {
            try
            {
                using var stream = await thumbnail.OpenReadAsync().AsTask().ConfigureAwait(false);
                using var input = stream.AsStreamForRead();
                var url = await ArtCache.StoreAsync(input, stream.ContentType).ConfigureAwait(false);
                if (url is null)
                    Skip("it is not a picture under the size limit");
                else
                    loop.Post(() => Set(url, request));
            }
#pragma warning disable CA1031 // A closed session fails with COMException or ObjectDisposedException; nobody awaits this.
            catch (Exception e)
#pragma warning restore CA1031
            {
                Skip(e.Message);
            }
        }

        // A failed read is not "this track has no art". Clearing the url here blanks art that already arrived.
        void Skip(string reason) => loop.Post(() =>
        {
            if (request == _request)
                _applied = request;
            if (_missLogged)
                return;
            _missLogged = true;
            missed(reason);
        });
    }

    public void Release()
    {
        _released = true;
        ArtCache.ReleaseLater(Url);
    }

    private void Set(string url, int request)
    {
        if (_released || request != _request)
        {
            ArtCache.ReleaseLater(url);
            return;
        }
        _applied = request;
        if (url.Length > 0)
            _missLogged = false;
        if (url == Url)
        {
            ArtCache.ReleaseLater(url);
            return;
        }
        ArtCache.ReleaseLater(Url);
        Url = url;
        changed();
    }
}
