namespace NServiceBus.AcceptanceTests.Recoverability.Retries
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using EndpointTemplates;
    using Features;
    using NServiceBus.Config;
    using NUnit.Framework;
    using ScenarioDescriptors;

    public class When_message_goes_through_all_retries : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_not_send_outgoing_messages_using_batched_dispatch()
        {
            await Scenario.Define<Context>(c => c.Id = Guid.NewGuid())
                .WithEndpoint<Endpoint>(b => b.DoNotFailOnErrorMessages()
                    .When((session, context) => session.SendLocal(new MessageToFail
                    {
                        Id = context.Id,
                        UseImmediateDispatch = false
                    }))
                )
                .WithEndpoint<ErrorSpy>()
                .Done(c => c.MessageMovedToErrorQueue)
                .Repeat(r => r.For<AllNativeMultiQueueTransactionTransports>())
                .Should(c => Assert.IsFalse(c.OutgoingMessageReceived))
                .Should(c => Assert.AreEqual(4, c.NumberOfProcessingAttempts))
                .Run();
        }

        [Test]
        public async Task Should_not_send_outgoing_messages_using_immediate_dispatch()
        {
            await Scenario.Define<Context>(c => c.Id = Guid.NewGuid())
                .WithEndpoint<Endpoint>(b => b.DoNotFailOnErrorMessages()
                    .When((session, context) => session.SendLocal(new MessageToFail
                    {
                        Id = context.Id,
                        UseImmediateDispatch = true
                    }))
                )
                .WithEndpoint<ErrorSpy>()
                .Done(c => c.MessageMovedToErrorQueue)
                .Repeat(r => r.For<AllNativeMultiQueueTransactionTransports>())
                .Should(c => Assert.IsFalse(c.OutgoingMessageReceived))
                .Should(c => Assert.AreEqual(4, c.NumberOfProcessingAttempts))
                .Run();
        }

        const string ErrorQueueName = "error_spy_queue";

        class Context : ScenarioContext
        {
            public Guid Id { get; set; }
            public bool MessageMovedToErrorQueue { get; set; }
            public bool OutgoingMessageReceived { get; set; }
            public int NumberOfProcessingAttempts { get; set; }
        }

        class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((config, context) =>
                {
                    config.UseTransport(context.GetTransportType()).Transactions(TransportTransactionMode.TransactionScope);
                    config.DisableFeature<FirstLevelRetries>();
                    config.EnableFeature<SecondLevelRetries>();
                    config.EnableFeature<TimeoutManager>();
                    config.SendFailedMessagesTo(ErrorQueueName);
                })
                .WithConfig<SecondLevelRetriesConfig>(slrConfig =>
                {
                    slrConfig.NumberOfRetries = 3;
                    slrConfig.TimeIncrease = TimeSpan.FromSeconds(1);
                });
            }

            class FailingHandler : IHandleMessages<MessageToFail>
            {
                public Context TestContext { get; set; }

                public async Task Handle(MessageToFail message, IMessageHandlerContext context)
                {
                    if (message.Id == TestContext.Id)
                    {
                        TestContext.NumberOfProcessingAttempts++;

                        var sendOptions = new SendOptions();
                        sendOptions.RouteToThisInstance();

                        if (message.UseImmediateDispatch)
                        {
                            sendOptions.RequireImmediateDispatch();
                        }

                        await context.Send(new OutgoingMessage
                        {
                            Id = TestContext.Id
                        }, sendOptions);
                    }

                    throw new SimulatedException();
                }
            }

            class OutgoingMessageHandler : IHandleMessages<OutgoingMessage>
            {
                public Context TestContext { get; set; }

                public Task Handle(OutgoingMessage message, IMessageHandlerContext context)
                {
                    if (message.Id == TestContext.Id)
                    {
                        TestContext.OutgoingMessageReceived = true;
                    }

                    return Task.FromResult(0);
                }
            }
        }

        class ErrorSpy : EndpointConfigurationBuilder
        {
            public ErrorSpy()
            {
                EndpointSetup<DefaultServer>().CustomEndpointName(ErrorQueueName);
            }

            class Handler : IHandleMessages<MessageToFail>
            {
                public Context TestContext { get; set; }

                public Task Handle(MessageToFail message, IMessageHandlerContext context)
                {
                    if (TestContext.Id == message.Id)
                    {
                        TestContext.MessageMovedToErrorQueue = true;
                    }

                    return Task.FromResult(0);
                }
            }
        }

        class MessageToFail : IMessage
        {
            public Guid Id { get; set; }
            public bool UseImmediateDispatch { get; set; }
        }

        class OutgoingMessage : IMessage
        {
            public Guid Id { get; set; }
        }
    }
}