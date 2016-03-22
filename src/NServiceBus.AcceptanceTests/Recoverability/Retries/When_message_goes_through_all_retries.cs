namespace NServiceBus.AcceptanceTests.Recoverability.Retries
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using EndpointTemplates;
    using Features;
    using NServiceBus.Config;
    using NServiceBus.Pipeline;
    using NUnit.Framework;
    using ScenarioDescriptors;

    public class When_message_goes_through_all_retries : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_not_send_outgoing_messages_using_batched_dispatch()
        {
            await Scenario.Define<Context>(c => c.Id = Guid.NewGuid())
                .WithEndpoint<Endpoint>(b => b.DoNotFailOnErrorMessages()
                    .When((session, context) => session.SendLocal(new IncomingMessage
                    {
                        Id = context.Id,
                        UseImmediateDispatch = false
                    }))
                )
                .WithEndpoint<ErrorSpy>()
                .Done(c => c.MessageMovedToErrorQueue)
                .Repeat(r => r.For<AllNativeMultiQueueTransactionTransports>())
                .Should(c => Assert.IsFalse(c.OutgoingMessageReceived, "Outgoing message was unexpectedly dispatched"))
                .Should(c => Assert.AreEqual(4, c.NumberOfProcessingAttempts))
                .Run();
        }

        [Test]
        public async Task Should_send_outgoing_messages_using_immediate_dispatch()
        {
            await Scenario.Define<Context>(c => c.Id = Guid.NewGuid())
                .WithEndpoint<Endpoint>(b => b.DoNotFailOnErrorMessages()
                    .When((session, context) => session.SendLocal(new IncomingMessage
                    {
                        Id = context.Id,
                        UseImmediateDispatch = true
                    }))
                )
                .WithEndpoint<ErrorSpy>()
                .Done(c => c.MessageMovedToErrorQueue)
                .Repeat(r => r.For<AllNativeMultiQueueTransactionTransports>())
                .Should(c => Assert.IsTrue(c.OutgoingMessageReceived, "Outgoing message was unexpectedly dropped"))
                .Should(c => Assert.AreEqual(4, c.NumberOfProcessingAttempts))
                .Run();
        }

        const string ErrorSpyQueueName = "error_spy_queue";

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
                    config.DisableFeature<FirstLevelRetries>();
                    config.EnableFeature<SecondLevelRetries>();
                    config.EnableFeature<TimeoutManager>();
                    config.Pipeline.Register(new RegisterThrowingBehavior("SecondLevelRetries"));
                    config.SendFailedMessagesTo(ErrorSpyQueueName);
                })
                .WithConfig<SecondLevelRetriesConfig>(slrConfig =>
                {
                    slrConfig.NumberOfRetries = 3;
                    slrConfig.TimeIncrease = TimeSpan.FromSeconds(1);
                });
            }

            class FailingHandler : IHandleMessages<IncomingMessage>
            {
                public Context TestContext { get; set; }

                public async Task Handle(IncomingMessage incomingMessage, IMessageHandlerContext context)
                {
                    if (incomingMessage.Id == TestContext.Id)
                    {
                        TestContext.NumberOfProcessingAttempts++;

                        var sendOptions = new SendOptions();

                        sendOptions.SetDestination(ErrorSpyQueueName);
                        if (incomingMessage.UseImmediateDispatch)
                        {
                            sendOptions.RequireImmediateDispatch();
                        }

                        await context.Send(new OutgoingMessage
                        {
                            Id = TestContext.Id
                        }, sendOptions);
                    }
                }
            }
        }

        class ErrorSpy : EndpointConfigurationBuilder
        {
            public ErrorSpy()
            {
                EndpointSetup<DefaultServer>().CustomEndpointName(ErrorSpyQueueName);
            }

            class Handler : IHandleMessages<IncomingMessage>
            {
                public Context TestContext { get; set; }

                public Task Handle(IncomingMessage incomingMessage, IMessageHandlerContext context)
                {
                    if (TestContext.Id == incomingMessage.Id)
                    {
                        TestContext.MessageMovedToErrorQueue = true;
                    }

                    return Task.FromResult(0);
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

        class IncomingMessage : IMessage
        {
            public Guid Id { get; set; }
            public bool UseImmediateDispatch { get; set; }
        }

        class OutgoingMessage : IMessage
        {
            public Guid Id { get; set; }
        }

        class RegisterThrowingBehavior : RegisterStep
        {
            public RegisterThrowingBehavior(string stepToInsertAfter) : base("ThrowingBehavior", typeof(ThrowingBehavior), "Behavior that always throws")
            {
                InsertAfter(stepToInsertAfter);
            }
        }

        class ThrowingBehavior : Behavior<ITransportReceiveContext>
        {
            public override async Task Invoke(ITransportReceiveContext context, Func<Task> next)
            {
                await next().ConfigureAwait(false);

                throw new SimulatedException();
            }
        }
    }
}