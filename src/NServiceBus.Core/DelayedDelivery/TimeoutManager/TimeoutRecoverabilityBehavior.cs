namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using JetBrains.Annotations;
    using Logging;
    using Pipeline;
    using Routing;
    using Transports;

    class TimeoutRecoverabilityBehavior : Behavior<ITransportReceiveContext>
    {
        public TimeoutRecoverabilityBehavior(string errorQueueAddress, string localAddress, IDispatchMessages dispatcher, CriticalError criticalError)
        {
            this.localAddress = localAddress;
            this.errorQueueAddress = errorQueueAddress;
            this.dispatcher = dispatcher;
            this.criticalError = criticalError;
        }

        public override async Task Invoke(ITransportReceiveContext context, Func<Task> next)
        {
            var message = context.Message;
            var failureInfo = failures.GetFailureInfoForMessage(message.MessageId);

            if (ShouldAttemptAnotherRetry(failureInfo))
            {
                try
                {
                    await next().ConfigureAwait(false);
                    return;
                }
                catch (Exception exception)
                {
                    failures.RecordFailureInfoForMessage(message.MessageId, exception);

                    Logger.Debug($"Going to retry message '{message.MessageId}' from satellite '{localAddress}' because of an exception:", exception);

                    context.AbortReceiveOperation();
                    return;
                }
            }

            failures.ClearFailureInfoForMessage(message.MessageId);

            Logger.Debug($"Giving up Retries for message '{message.MessageId}' from satellite '{localAddress}' after {failureInfo.NumberOfFailedAttempts} attempts.");

            await MoveToErrorQueue(context, message, failureInfo).ConfigureAwait(false);
        }

        bool ShouldAttemptAnotherRetry([NotNull] ProcessingFailureInfo failureInfo)
        {
            return failureInfo.NumberOfFailedAttempts <= MaxNumberOfFailedRetries;
        }

        async Task MoveToErrorQueue(ITransportReceiveContext context, IncomingMessage message, ProcessingFailureInfo failureInfo)
        {
            try
            {
                Logger.Error($"Moving timeout message '{message.MessageId}' from '{localAddress}' to '{errorQueueAddress}' because processing failed due to an exception:", failureInfo.Exception);

                message.SetExceptionHeaders(failureInfo.Exception, localAddress);

                var outgoingMessage = new OutgoingMessage(message.MessageId, message.Headers, message.Body);
                var routingStrategy = new UnicastRoutingStrategy(errorQueueAddress);
                var addressTag = routingStrategy.Apply(new Dictionary<string, string>());

                var transportOperations = new TransportOperations(new TransportOperation(outgoingMessage, addressTag));

                await dispatcher.Dispatch(transportOperations, context.Extensions).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                criticalError.Raise("Failed to forward failed timeout message to error queue", ex);
                throw;
            }
        }

        CriticalError criticalError;
        IDispatchMessages dispatcher;
        string errorQueueAddress;

        FailureInfoStorage failures = new FailureInfoStorage();
        string localAddress;

        const int MaxNumberOfFailedRetries = 4;

        static ILog Logger = LogManager.GetLogger<TimeoutRecoverabilityBehavior>();
    }
}