﻿namespace NServiceBus.AcceptanceTests.Recoverability.Retries
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using EndpointTemplates;
    using Features;
    using NServiceBus.Config;
    using NUnit.Framework;

    public class When_fails_with_retries_set_to_0 : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_not_retry_the_message_using_flr()
        {
            var context = await Scenario.Define<Context>(c => { c.Id = Guid.NewGuid(); })
                .WithEndpoint<RetryEndpoint>(b => b
                    .DoNotFailOnErrorMessages())
                .Done(c => c.GaveUp)
                .Run();

            Assert.AreEqual(1, context.NumberOfTimesInvoked, "No FLR should be in use if MaxRetries is set to 0");
        }

        class Context : ScenarioContext
        {
            public Guid Id { get; set; }
            public int NumberOfTimesInvoked { get; set; }

            public bool GaveUp { get; set; }
        }

        class ErrorNotificationSpy : Feature
        {
            protected override void Setup(FeatureConfigurationContext context)
            {
                context.Container.ConfigureComponent<ErrorNotificationSpyTask>(DependencyLifecycle.SingleInstance);
                context.RegisterStartupTask(b => b.Build<ErrorNotificationSpyTask>());
            }

            class ErrorNotificationSpyTask : FeatureStartupTask
            {
                public ErrorNotificationSpyTask(Notifications notifications, Context context)
                {
                    this.notifications = notifications;
                    this.context = context;
                }

                protected override Task OnStart(IMessageSession session)
                {
                    notifications.Errors.MessageSentToErrorQueue += (sender, message) => context.GaveUp = true;
                    return session.SendLocal(new MessageToBeRetried
                    {
                        ContextId = context.Id
                    });
                }

                protected override Task OnStop(IMessageSession session)
                {
                    return Task.FromResult(0);
                }

                Notifications notifications;
                Context context;
            }
        }

        public class RetryEndpoint : EndpointConfigurationBuilder
        {
            public RetryEndpoint()
            {
                EndpointSetup<DefaultServer>(b =>
                {
                    b.EnableFeature<FirstLevelRetries>();
                    b.EnableFeature<ErrorNotificationSpy>();
                })
                    .WithConfig<TransportConfig>(c => { c.MaxRetries = 0; });
            }

            class MessageToBeRetriedHandler : IHandleMessages<MessageToBeRetried>
            {
                public Context Context { get; set; }

                public Task Handle(MessageToBeRetried message, IMessageHandlerContext context)
                {
                    if (Context.Id != message.ContextId)
                    {
                        return Task.FromResult(0);
                    }
                    Context.NumberOfTimesInvoked++;
                    throw new SimulatedException();
                }
            }
        }

        [Serializable]
        public class MessageToBeRetried : IMessage
        {
            public Guid ContextId { get; set; }
        }
    }
}