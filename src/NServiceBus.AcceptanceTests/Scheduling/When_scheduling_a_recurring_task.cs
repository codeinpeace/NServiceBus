namespace NServiceBus.AcceptanceTests.Scheduling
{
    using System;
    using System.Threading.Tasks;
    using EndpointTemplates;
    using AcceptanceTesting;
    using NServiceBus.Features;
    using NUnit.Framework;
    using ScenarioDescriptors;

    public class When_scheduling_a_recurring_task : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_execute_the_task()
        {
            await Scenario.Define<Context>()
                    .WithEndpoint<SchedulingEndpoint>()
                    .Done(c => c.InvokedAt.HasValue)
                    .Repeat(r => r.For(Transports.Default))
                    .Should(c =>
                    {
                        Assert.True(c.InvokedAt.HasValue);
                        Assert.Greater(c.InvokedAt.Value - c.RequestedAt, TimeSpan.FromMilliseconds(5));
                    })
                  .Run(TimeSpan.FromSeconds(60));
        }

        public class Context : ScenarioContext
        {
            public DateTime? InvokedAt{ get; set; }
            public DateTime RequestedAt{ get; set; }
        }

        class SetupScheduledAction : Feature
        {
            protected override void Setup(FeatureConfigurationContext context)
            {
                context.Container.ConfigureComponent<SetupScheduledActionTask>(DependencyLifecycle.SingleInstance);
                context.RegisterStartupTask(b => b.Build<SetupScheduledActionTask>());
            }
        }

        class SetupScheduledActionTask : FeatureStartupTask
        {
            Context context;

            public SetupScheduledActionTask(Context context)
            {
                this.context = context;
            }

            protected override Task OnStart(IMessageSession session)
            {
                context.RequestedAt = DateTime.UtcNow;

                return session.ScheduleEvery(TimeSpan.FromMilliseconds(5), "MyTask", c =>
                {
                    context.InvokedAt = DateTime.UtcNow;
                    return Task.FromResult(0);
                });
            }

            protected override Task OnStop(IMessageSession session)
            {
                return Task.FromResult(0);
            }
        }

        public class SchedulingEndpoint : EndpointConfigurationBuilder
        {
            public SchedulingEndpoint()
            {
                EndpointSetup<DefaultServer>(config =>
                {
                    config.EnableFeature<TimeoutManager>(); 
                    config.EnableFeature<SetupScheduledAction>(); 
                });
            }

        }
    }


}