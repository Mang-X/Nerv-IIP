namespace Nerv.IIP.Testing;

/// <summary>
/// Test doubles that must stay pending until somebody cancels them.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the <c>Task.Delay(Timeout.InfiniteTimeSpan, token)</c> sentinel. That sentinel never
/// waits on time — its meaning is "never complete until this token is canceled" — but it is written in
/// the vocabulary of a wall-clock delay, so every determinism review has to re-derive that the test is
/// not sleeping. Stating the intent directly removes the ambiguity and creates no timer at all, on the
/// system clock or on a <see cref="TimeProvider"/>.
/// </para>
/// <para>
/// The observable behaviour is identical to the sentinel it replaces: the returned task completes only
/// when <paramref name="cancellationToken"/> is canceled, and it then throws an
/// <see cref="OperationCanceledException"/> carrying that same token. A token that can never be
/// canceled yields a task that never completes, exactly like the infinite delay did.
/// </para>
/// </remarks>
public static class PendingOperation
{
    /// <summary>
    /// Returns a task that stays pending until <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    public static Task UntilCanceledAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(
            static state =>
            {
                var (source, token) = ((TaskCompletionSource Source, CancellationToken Token))state!;
                source.TrySetCanceled(token);
            },
            (Source: completion, Token: cancellationToken));

        return AwaitAsync(completion.Task, registration);

        static async Task AwaitAsync(Task pending, CancellationTokenRegistration registration)
        {
            using (registration)
            {
                await pending.ConfigureAwait(false);
            }
        }
    }
}
