﻿namespace NServiceBus.Core.Tests.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Extensibility;
    using NServiceBus.Routing;
    using NServiceBus.Routing.MessageDrivenSubscriptions;
    using NServiceBus.Transports;
    using NUnit.Framework;
    using Unicast.Queuing;

    [TestFixture]
    public class MessageDrivenSubscribeTerminatorTests
    {
        [SetUp]
        public void SetUp()
        {
            var publishers = new Publishers();
            publishers.AddByAddress("publisher1", typeof(object));
            router = new SubscriptionRouter(publishers, new EndpointInstances(), new TransportAddresses(address => null));
            dispatcher = new FakeDispatcher();
            subscribeTerminator = new MessageDrivenSubscribeTerminator(router, "replyToAddress", new EndpointName("Endpoint"), dispatcher);
        }

        [Test]
        public async Task Should_include_TimeSent_and_Version_headers()
        {
            var unsubscribeTerminator = new MessageDrivenUnsubscribeTerminator(router, "replyToAddress", new EndpointName("Endpoint"), dispatcher);

            await subscribeTerminator.Invoke(new SubscribeContext(new FakeContext(), typeof(object), new SubscribeOptions()), c => TaskEx.CompletedTask);
            await unsubscribeTerminator.Invoke(new UnsubscribeContext(new FakeContext(), typeof(object), new UnsubscribeOptions()), c => TaskEx.CompletedTask);

            foreach (var dispatchedTransportOperation in dispatcher.DispatchedTransportOperations)
            {
                var unicastTransportOperations = dispatchedTransportOperation.UnicastTransportOperations;
                var operations = new List<UnicastTransportOperation>(unicastTransportOperations);

                Assert.IsTrue(operations[0].Message.Headers.ContainsKey(Headers.TimeSent));
                Assert.IsTrue(operations[0].Message.Headers.ContainsKey(Headers.NServiceBusVersion));
            }
        }

        [Test]
        public async Task Should_Dispatch_for_all_publishers()
        {
            await subscribeTerminator.Invoke(new SubscribeContext(new FakeContext(), typeof(object), new SubscribeOptions()), c => TaskEx.CompletedTask);

            Assert.AreEqual(1, dispatcher.DispatchedTransportOperations.Count);
        }

        [Test]
        public async Task Should_Dispatch_according_to_max_retries_when_dispatch_fails()
        {
            var options = new SubscribeOptions();
            var state = options.GetExtensions().GetOrCreate<MessageDrivenSubscribeTerminator.Settings>();
            state.MaxRetries = 10;
            state.RetryDelay = TimeSpan.Zero;
            dispatcher.FailDispatch(10);

            await subscribeTerminator.Invoke(new SubscribeContext(new FakeContext(), typeof(object), options), c => TaskEx.CompletedTask);

            Assert.AreEqual(1, dispatcher.DispatchedTransportOperations.Count);
            Assert.AreEqual(10, dispatcher.FailedNumberOfTimes);
        }

        [Test]
        public void Should_Throw_when_max_retries_reached()
        {
            var options = new SubscribeOptions();
            var state = options.GetExtensions().GetOrCreate<MessageDrivenSubscribeTerminator.Settings>();
            state.MaxRetries = 10;
            state.RetryDelay = TimeSpan.Zero;
            dispatcher.FailDispatch(11);

            Assert.That(async () => await subscribeTerminator.Invoke(new SubscribeContext(new FakeContext(), typeof(object), options), c => TaskEx.CompletedTask), Throws.InstanceOf<QueueNotFoundException>());

            Assert.AreEqual(0, dispatcher.DispatchedTransportOperations.Count);
            Assert.AreEqual(11, dispatcher.FailedNumberOfTimes);
        }

        FakeDispatcher dispatcher;
        SubscriptionRouter router;
        MessageDrivenSubscribeTerminator subscribeTerminator;

        class FakeDispatcher : IDispatchMessages
        {
            public int FailedNumberOfTimes { get; private set; }

            public List<TransportOperations> DispatchedTransportOperations { get; } = new List<TransportOperations>();

            public Task Dispatch(TransportOperations outgoingMessages, ContextBag context)
            {
                if (numberOfTimes.HasValue && FailedNumberOfTimes < numberOfTimes.Value)
                {
                    FailedNumberOfTimes++;
                    throw new QueueNotFoundException();
                }

                DispatchedTransportOperations.Add(outgoingMessages);
                return TaskEx.CompletedTask;
            }

            public void FailDispatch(int times)
            {
                numberOfTimes = times;
            }

            int? numberOfTimes;
        }

        class FakeContext : BehaviorContext
        {
            public FakeContext() : base(null)
            {
            }
        }
    }
}