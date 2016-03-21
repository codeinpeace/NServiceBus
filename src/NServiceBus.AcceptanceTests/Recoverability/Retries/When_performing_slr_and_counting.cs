namespace NServiceBus.AcceptanceTests.Recoverability.Retries
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Config;
    using NServiceBus.Features;
    using NUnit.Framework;

    public class When_performing_slr_and_counting : NServiceBusAcceptanceTest
    {
        public class Context : ScenarioContext
        {
            public bool ForwardedToErrorQueue { get; set; }
            public string PhysicalMessageId { get; set; }
        }

        [Test]
        public async Task Should_reschedule_message_three_times_by_default()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<RetryEndpoint>(b => b
                    .When(session => session.SendLocal(new MessageToBeRetried()))
                    .DoNotFailOnErrorMessages())
                .Done(c => c.ForwardedToErrorQueue)
                .Run(TimeSpan.FromSeconds(120));

            Assert.IsTrue(context.ForwardedToErrorQueue);
            Assert.AreEqual(3, context.Logs.Count(l => l.Message
                .StartsWith($"Second Level Retry will reschedule message '{context.PhysicalMessageId}'")));
            Assert.AreEqual(1, context.Logs.Count(l => l.Message
                .StartsWith($"Giving up Second Level Retries for message '{context.PhysicalMessageId}'.")));
        }

        class ErrorNotificationSpy : Feature
        {
            protected override void Setup(FeatureConfigurationContext context)
            {
                context.Container.ConfigureComponent<ErrorNotificationSpyTask>(DependencyLifecycle.SingleInstance);
                context.RegisterStartupTask(b => b.Build<ErrorNotificationSpyTask>());
            }
        }

        class ErrorNotificationSpyTask : FeatureStartupTask
        {
            Context testContext;
            Notifications notifications;

            public ErrorNotificationSpyTask(Context testContext, Notifications notifications)
            {
                this.testContext = testContext;
                this.notifications = notifications;
            }

            protected override Task OnStart(IMessageSession session)
            {
                notifications.Errors.MessageSentToErrorQueue += (sender, message) =>
                {
                    testContext.ForwardedToErrorQueue = true;
                };
                return Task.FromResult(0);
            }

            protected override Task OnStop(IMessageSession session)
            {
                return Task.FromResult(0);
            }
        }

        public class RetryEndpoint : EndpointConfigurationBuilder
        {
            public RetryEndpoint()
            {
                EndpointSetup<DefaultServer>(configure =>
                {
                    configure.DisableFeature<FirstLevelRetries>();
                    configure.EnableFeature<SecondLevelRetries>();
                    configure.EnableFeature<TimeoutManager>();
                    configure.EnableFeature<ErrorNotificationSpy>();
                })
                .WithConfig<SecondLevelRetriesConfig>(c => c.TimeIncrease = TimeSpan.FromMilliseconds(1));
            }

            class MessageToBeRetriedHandler : IHandleMessages<MessageToBeRetried>
            {
                Context testContext;

                public MessageToBeRetriedHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(MessageToBeRetried message, IMessageHandlerContext context)
                {
                    testContext.PhysicalMessageId = context.MessageId;
                    throw new SimulatedException();
                }
            }
        }

        [Serializable]
        public class MessageToBeRetried : IMessage
        {
        }
    }
}