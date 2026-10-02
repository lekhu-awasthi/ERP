using ErpApp.Application.Common.Exceptions;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>
/// Phase 64 -- what an order's Domain refusal is to its caller. Every field rule has already been a 400
/// in the validator, so what the aggregate still refuses is the order's <b>state</b>: it is no longer
/// open, a line has less outstanding than the waiter tried to serve, less on the order than they tried
/// to discard. That is a 409 carrying the aggregate's own sentence, never the 500 an unmapped
/// <see cref="InvalidOperationException"/> would be (phase 39).
/// </summary>
internal static class PosOrderCommands
{
    public static T Run<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
    }

    public static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
    }
}
