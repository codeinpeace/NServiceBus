namespace NServiceBus.AcceptanceTests.Recoverability.Retries
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using EndpointTemplates;
    using Features;
    using NServiceBus.Config;
    using NUnit.Framework;
    using Transports;

    public class When_performing_slr_with_non_min_policy : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_execute_twice()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<RetryEndpoint>(b => b
                    .When(bus => bus.SendLocal(new MessageToBeRetried()))
                    .DoNotFailOnErrorMessages())
                .Done(c => c.MessageSentToErrorQueue)
                .Run();

            Assert.AreEqual(context.Count, 2);
        }

        class Context : ScenarioContext
        {
            public bool MessageSentToErrorQueue { get; set; }
            public int Count { get; set; }
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
            public ErrorNotificationSpyTask(Context context, Notifications notifications)
            {
                this.notifications = notifications;
                this.context = context;
            }

            protected override Task OnStart(IMessageSession session)
            {
                notifications.Errors.MessageSentToErrorQueue += (sender, message) => { context.MessageSentToErrorQueue = true; };
                return Task.FromResult(0);
            }

            protected override Task OnStop(IMessageSession session)
            {
                return Task.FromResult(0);
            }

            Context context;
            Notifications notifications;
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
                    configure.SecondLevelRetries().CustomRetryPolicy(RetryPolicy);
                })
                    .WithConfig<SecondLevelRetriesConfig>(c => c.TimeIncrease = TimeSpan.FromMilliseconds(1));
            }

            TimeSpan RetryPolicy(IncomingMessage transportMessage)
            {
                if (count == 0)
                {
                    count++;
                    return TimeSpan.FromMilliseconds(10);
                }
                return TimeSpan.MinValue;
            }

            int count;

            class MessageToBeRetriedHandler : IHandleMessages<MessageToBeRetried>
            {
                public MessageToBeRetriedHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(MessageToBeRetried message, IMessageHandlerContext context)
                {
                    testContext.Count ++;
                    throw new SimulatedException();
                }

                Context testContext;
            }
        }

        [Serializable]
        public class MessageToBeRetried : IMessage
        {
        }
    }
}