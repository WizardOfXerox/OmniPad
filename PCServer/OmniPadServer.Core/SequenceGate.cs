namespace OmniPadServer.Core;

/// <summary>
/// Discards UDP packets that are older or duplicate compared to one already seen.
/// Handles 32-bit unsigned rollover gracefully.
/// </summary>
public sealed class SequenceGate
{
    private uint _last;
    private bool _seenAny;

    public uint Last => _last;

    public bool Accept(uint sequence)
    {
        // First packet always accepted
        if (!_seenAny)
        {
            _seenAny = true;
            _last = sequence;
            return true;
        }

        // Subtraction as unsigned, cast to signed int:
        // Handles rollover across 0xFFFFFFFF -> 0 correctly
        if ((int)(sequence - _last) <= 0)
            return false;

        _last = sequence;
        return true;
    }

    public void Reset()
    {
        _last = 0;
        _seenAny = false;
    }
}
