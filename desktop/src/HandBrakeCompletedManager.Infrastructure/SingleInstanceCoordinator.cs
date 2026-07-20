namespace HandBrakeCompletedManager.Infrastructure;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly EventWaitHandle _activationEvent;
    private readonly Mutex _instanceMutex;
    private readonly RegisteredWaitHandle? _activationRegistration;
    private bool _ownsMutex;
    private bool _isDisposed;

    public SingleInstanceCoordinator(string applicationId, Action activationRequested)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("A stable application identifier is required.", nameof(applicationId));
        }

        ArgumentNullException.ThrowIfNull(activationRequested);
        var name = applicationId.Trim();
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
        _instanceMutex = new Mutex(true, $@"Local\{name}.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        IsPrimaryInstance = createdNew;
        if (IsPrimaryInstance)
        {
            _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                (_, _) => RequestActivationSafely(activationRequested),
                null,
                Timeout.Infinite,
                false);
        }
        else
        {
            _activationEvent.Set();
        }
    }

    public bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _activationRegistration?.Unregister(null);
        if (_ownsMutex)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The operating system already released ownership during shutdown.
            }

            _ownsMutex = false;
        }

        _instanceMutex.Dispose();
        _activationEvent.Dispose();
    }

    private static void RequestActivationSafely(Action activationRequested)
    {
        try
        {
            activationRequested();
        }
        catch (InvalidOperationException)
        {
            // Application shutdown raced the activation request.
        }
    }
}
