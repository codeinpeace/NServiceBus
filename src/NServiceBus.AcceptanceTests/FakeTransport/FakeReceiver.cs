namespace NServiceBus.AcceptanceTests.FakeTransport
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus.Transports;
    using CriticalError = NServiceBus.CriticalError;

    class FakeReceiver : IPushMessages
    {
        CriticalError criticalError;
        Exception throwCritical;

        public void Init(Func<PushContext, Task> pipe, PushSettings settings)
        {
        }

        public void Start(PushRuntimeSettings limitations)
        {
            if (throwCritical != null)
            {
                criticalError.Raise(throwCritical.Message, throwCritical);
            }
        }

        public Task StopAsync()
        {
            return Task.FromResult(0);
        }

        public FakeReceiver(CriticalError criticalError, Exception throwCritical)
        {
            this.criticalError = criticalError;
            this.throwCritical = throwCritical;
        }
    }
}