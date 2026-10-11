using System.Runtime.ExceptionServices;

namespace Memento.Video.Interop;

/// <summary>
/// DirectShow objects are created and driven from the multithreaded apartment, so their streaming threads call back
/// without a message pump. Thread-pool threads already are MTA; a call from an STA thread (the WPF dispatcher) runs on
/// a short-lived MTA thread instead.
/// </summary>
internal static class ComApartment
{
    public static T InMta<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return work();
        }

        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
#pragma warning disable CA1031 // Rethrown on the calling thread with its stack.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            Name = "Memento DirectShow (MTA)",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}
