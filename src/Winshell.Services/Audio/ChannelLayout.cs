namespace Winshell.Audio;

/// <summary>Maps a Windows speaker mask onto <see cref="AudioChannel"/> values.</summary>
internal static class ChannelLayout
{
    private const int SpeakerBits = 18;

    /// <summary>The channels of a device from its WAVEFORMATEXTENSIBLE, or a guess from the channel count.</summary>
    public static AudioChannel[] FromFormat(byte[]? format, int count)
    {
        if (format is { Length: >= 24 } && BitConverter.ToUInt16(format, 0) == 0xFFFE)
        {
            var mask = BitConverter.ToUInt32(format, 20);
            var channels = new List<AudioChannel>();
            for (var bit = 0; bit < SpeakerBits; bit++)
                if ((mask & (1u << bit)) != 0)
                    channels.Add(AudioChannel.FrontLeft + bit);
            if (channels.Count == count)
                return channels.ToArray();
        }

        return count switch
        {
            1 => [AudioChannel.Mono],
            2 => [AudioChannel.FrontLeft, AudioChannel.FrontRight],
            _ => new AudioChannel[count]
        };
    }
}
